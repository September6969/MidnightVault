using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using MidnightVault.Desktop.Services;
using QRCoder;
using Windows.Storage.Streams;

namespace MidnightVault.Desktop.Pages;

public sealed partial class DevicesPage : Page
{
    private CancellationTokenSource? _pairingCancellation;

    public DevicesPage()
    {
        InitializeComponent();
        Unloaded += (_, _) => _pairingCancellation?.Cancel();
    }

    private async void DevicesPage_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshDevicesAsync();
    }

    private void DevicesPage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool isCompact = e.NewSize.Width < 760;
        bool isPhone = e.NewSize.Width < 640;

        DevicesContent.Padding = (Thickness)Application.Current.Resources[
            isPhone
                ? "MidnightPhonePagePadding"
                : isCompact
                    ? "MidnightCompactPagePadding"
                    : "MidnightPagePadding"];
        PageTitle.FontSize = isPhone ? 28 : 32;

        Grid.SetRow(PairDeviceButton, isCompact ? 1 : 0);
        Grid.SetColumn(PairDeviceButton, isCompact ? 0 : 1);
        PairDeviceButton.HorizontalAlignment = isPhone
            ? HorizontalAlignment.Stretch
            : isCompact
                ? HorizontalAlignment.Left
                : HorizontalAlignment.Right;
        PairDeviceButton.Margin = isCompact ? new Thickness(0, 6, 0, 0) : new Thickness(0);

        PairingImageColumn.Width = isCompact
            ? new GridLength(1, GridUnitType.Star)
            : GridLength.Auto;
        PairingTextColumn.Width = isCompact
            ? new GridLength(0)
            : new GridLength(1, GridUnitType.Star);
        Grid.SetRow(PairingGuideImage, 0);
        Grid.SetColumn(PairingGuideImage, 0);
        Grid.SetRow(PairingGuideText, isCompact ? 1 : 0);
        Grid.SetColumn(PairingGuideText, isCompact ? 0 : 1);
        PairingGuideImage.Width = isPhone ? 128 : 156;
        PairingGuideImage.Height = isPhone ? 128 : 156;
        PairingGuideText.HorizontalAlignment = isCompact
            ? HorizontalAlignment.Center
            : HorizontalAlignment.Stretch;

        ReflowDeviceStatus(DeviceSlotOneStatus, DeviceSlotOneStatusColumn, isPhone);
        ReflowDeviceStatus(DeviceSlotTwoStatus, DeviceSlotTwoStatusColumn, isPhone);
    }

    private static void ReflowDeviceStatus(
        Border status,
        ColumnDefinition statusColumn,
        bool isPhone)
    {
        statusColumn.Width = isPhone ? new GridLength(0) : GridLength.Auto;
        Grid.SetRow(status, isPhone ? 1 : 0);
        Grid.SetColumn(status, isPhone ? 1 : 2);
        status.HorizontalAlignment = isPhone
            ? HorizontalAlignment.Left
            : HorizontalAlignment.Right;
        status.Margin = isPhone ? new Thickness(0, 2, 0, 0) : new Thickness(0);
    }

    private async void PairDevice_Click(object sender, RoutedEventArgs e)
    {
        PairDeviceButton.IsEnabled = false;
        _pairingCancellation?.Cancel();
        _pairingCancellation?.Dispose();
        _pairingCancellation = new CancellationTokenSource();
        try
        {
            var health = await AppServices.ReceiverProcess.EnsureRunningAsync(_pairingCancellation.Token)
                ?? throw new InvalidOperationException("请先在“设置”中选择备份盘。");
            var settings = await AppServices.Settings.LoadAsync(_pairingCancellation.Token);
            var receiverUrl = ReceiverProcessManager.GetPairingUrls(settings.ReceiverPort).FirstOrDefault()
                ?? throw new InvalidOperationException("未找到可供手机访问的局域网 IPv4 地址。");
            var pairing = await AppServices.ReceiverApi.CreatePairingSessionAsync(_pairingCancellation.Token);
            var deepLink = "midnightvault://pair?receiver=" + Uri.EscapeDataString(receiverUrl) +
                "&code=" + Uri.EscapeDataString(pairing.Code);
            PairingGuideImage.Source = await CreateQrBitmapAsync(deepLink);
            PairingGuideTitle.Text = "请用手机系统相机扫码";
            PairingGuideDescription.Text =
                $"手机与电脑需在同一局域网。二维码只含一次性码，不包含长期上传令牌。接收地址：{receiverUrl}";
            PairingExpiryText.Text = $"{pairing.ExpiresAt.ToLocalTime():HH:mm:ss} 前有效，仅可使用一次";
            PairingInfoBar.Severity = InfoBarSeverity.Informational;
            PairingInfoBar.Title = "等待手机确认";
            PairingInfoBar.Message = "扫码后系统会打开 MidnightVault Android 客户端并自动保存连接。";
            PairingInfoBar.IsOpen = true;

            while (DateTimeOffset.UtcNow < pairing.ExpiresAt)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), _pairingCancellation.Token);
                var devices = await RefreshDevicesAsync(_pairingCancellation.Token);
                if (devices.Count > 0)
                {
                    PairingInfoBar.Severity = InfoBarSeverity.Success;
                    PairingInfoBar.Title = "设备已配对";
                    PairingInfoBar.Message = "Android 已取得接收凭据，可以开始首次备份。";
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 页面离开或重新生成二维码时取消轮询。
        }
        catch (Exception exception)
        {
            PairingInfoBar.Severity = InfoBarSeverity.Error;
            PairingInfoBar.Title = "无法开始配对";
            PairingInfoBar.Message = exception.Message;
            PairingInfoBar.IsOpen = true;
        }
        finally
        {
            PairDeviceButton.IsEnabled = true;
        }
    }

    private async Task<IReadOnlyList<PairedDevice>> RefreshDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var health = await AppServices.ReceiverProcess.EnsureRunningAsync(cancellationToken);
            if (health is null) return [];
            var devices = await AppServices.ReceiverApi.GetDevicesAsync(cancellationToken);
            ApplyDeviceSlot(
                devices.ElementAtOrDefault(0),
                DeviceSlotOneName,
                DeviceSlotOneDetail,
                DeviceSlotOneStatusText,
                DeviceSlotOneRevokeButton);
            ApplyDeviceSlot(
                devices.ElementAtOrDefault(1),
                DeviceSlotTwoName,
                DeviceSlotTwoDetail,
                DeviceSlotTwoStatusText,
                DeviceSlotTwoRevokeButton);
            var settings = await AppServices.Settings.LoadAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(settings.ControlPlaneUrl) &&
                await AppServices.Credentials.ReadControlTokenAsync(cancellationToken) is not null)
            {
                var controlDevices = await AppServices.ControlPlane.GetDevicesAsync(cancellationToken);
                ApplyControlDevice(
                    controlDevices.ElementAtOrDefault(0),
                    DeviceSlotOneName,
                    DeviceSlotOneDetail,
                    DeviceSlotOneStatusText,
                    DeviceSlotOneRevokeButton);
                ApplyControlDevice(
                    controlDevices.ElementAtOrDefault(1),
                    DeviceSlotTwoName,
                    DeviceSlotTwoDetail,
                    DeviceSlotTwoStatusText,
                    DeviceSlotTwoRevokeButton);
            }
            return devices;
        }
        catch (HttpRequestException)
        {
            return [];
        }
    }

    private static void ApplyControlDevice(
        ControlDevice? device,
        TextBlock name,
        TextBlock detail,
        TextBlock status,
        Button revokeButton)
    {
        if (device is null) return;
        name.Text = device.DisplayName;
        detail.Text = $"最近签名连接：{device.LastSeenAt.ToLocalTime():yyyy-MM-dd HH:mm}";
        status.Text = device.RevokedAt is null ? "控制面在线" : "已撤销";
        revokeButton.Tag = device.Id;
        revokeButton.Visibility = device.RevokedAt is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void RevokeDevice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string deviceId }) return;
        try
        {
            await AppServices.ReceiverApi.RevokeDeviceAsync(deviceId);
            var settings = await AppServices.Settings.LoadAsync();
            if (!string.IsNullOrWhiteSpace(settings.ControlPlaneUrl) &&
                await AppServices.Credentials.ReadControlTokenAsync() is not null)
            {
                await AppServices.ControlPlane.RevokeDeviceAsync(deviceId);
            }
            PairingInfoBar.Severity = InfoBarSeverity.Success;
            PairingInfoBar.Title = "设备已撤销";
            PairingInfoBar.Message = "Windows 数据面和手机控制面都将拒绝该设备后续签名请求。";
            PairingInfoBar.IsOpen = true;
            await RefreshDevicesAsync();
        }
        catch (Exception exception)
        {
            PairingInfoBar.Severity = InfoBarSeverity.Error;
            PairingInfoBar.Title = "撤销失败";
            PairingInfoBar.Message = exception.Message;
            PairingInfoBar.IsOpen = true;
        }
    }

    private static void ApplyDeviceSlot(
        PairedDevice? device,
        TextBlock name,
        TextBlock detail,
        TextBlock status,
        Button revokeButton)
    {
        if (device is null) return;
        name.Text = device.DeviceName;
        detail.Text = $"设备 ID {device.DeviceId[..Math.Min(8, device.DeviceId.Length)]} · " +
            $"配对于 {device.PairedAt.ToLocalTime():yyyy-MM-dd HH:mm}";
        status.Text = "已配对";
        revokeButton.Tag = device.DeviceId;
        revokeButton.Visibility = Visibility.Visible;
    }

    private static async Task<BitmapImage> CreateQrBitmapAsync(string payload)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var bytes = new PngByteQRCode(data).GetGraphic(10);
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }
        stream.Seek(0);
        var bitmap = new BitmapImage();
        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }
}
