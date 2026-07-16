using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MidnightVault.Desktop.Services;
using Windows.Storage.Pickers;

namespace MidnightVault.Desktop.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    private async void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        var settings = await AppServices.Settings.LoadAsync();
        ControlPlaneUrlBox.Text = settings.ControlPlaneUrl ?? "";
        ControlPlaneStatusText.Text = await AppServices.Credentials.ReadControlTokenAsync() is null
            ? "尚未保存控制令牌"
            : "控制令牌已使用 Windows 用户级数据保护保存";
        await RefreshStorageStatusAsync();
    }

    private void SettingsPage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool isCompact = e.NewSize.Width < 900;
        bool isPhone = e.NewSize.Width < 640;

        SettingsContent.Padding = (Thickness)Application.Current.Resources[
            isPhone
                ? "MidnightPhonePagePadding"
                : isCompact
                    ? "MidnightCompactPagePadding"
                    : "MidnightPagePadding"];
        PageTitle.FontSize = isPhone ? 28 : 32;

        if (isPhone)
        {
            DriveImageColumn.Width = new GridLength(1, GridUnitType.Star);
            DriveTextColumn.Width = new GridLength(0);
            DriveActionColumn.Width = new GridLength(0);
            Grid.SetRow(DriveSetupImage, 0);
            Grid.SetColumn(DriveSetupImage, 0);
            Grid.SetRow(DriveSetupText, 1);
            Grid.SetColumn(DriveSetupText, 0);
            Grid.SetRow(ChooseDriveButton, 2);
            Grid.SetColumn(ChooseDriveButton, 0);
            ChooseDriveButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
        else
        {
            DriveImageColumn.Width = GridLength.Auto;
            DriveTextColumn.Width = new GridLength(1, GridUnitType.Star);
            DriveActionColumn.Width = isCompact ? new GridLength(0) : GridLength.Auto;
            Grid.SetRow(DriveSetupImage, 0);
            Grid.SetColumn(DriveSetupImage, 0);
            Grid.SetRow(DriveSetupText, 0);
            Grid.SetColumn(DriveSetupText, 1);
            Grid.SetRow(ChooseDriveButton, isCompact ? 1 : 0);
            Grid.SetColumn(ChooseDriveButton, isCompact ? 1 : 2);
            ChooseDriveButton.HorizontalAlignment = isCompact
                ? HorizontalAlignment.Left
                : HorizontalAlignment.Right;
        }

        ChooseDriveButton.Margin = isCompact ? new Thickness(0, 4, 0, 0) : new Thickness(0);
        DriveSetupImage.Width = isPhone ? 112 : 126;
        DriveSetupImage.Height = isPhone ? 112 : 126;
    }

    private async void ChooseDrive_Click(object sender, RoutedEventArgs e)
    {
        var window = App.MainWindow;
        if (window is null) return;

        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder,
            ViewMode = PickerViewMode.List
        };
        picker.FileTypeFilter.Add("*");
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;

        ChooseDriveButton.IsEnabled = false;
        SettingsInfoBar.Severity = InfoBarSeverity.Informational;
        SettingsInfoBar.Title = "正在绑定备份盘";
        SettingsInfoBar.Message = "正在创建硬盘身份、缓存目录并启动接收服务…";
        SettingsInfoBar.IsOpen = true;
        try
        {
            var health = await AppServices.ConfigureStorageAsync(folder.Path);
            SettingsInfoBar.Severity = InfoBarSeverity.Success;
            SettingsInfoBar.Title = "备份盘已绑定";
            SettingsInfoBar.Message = health is null
                ? "配置已保存，但接收服务尚未就绪。"
                : $"接收服务已启动，可用空间 {FormatBytes(health.Storage.AvailableFreeSpace)}。";
            await RefreshStorageStatusAsync();
        }
        catch (Exception exception)
        {
            SettingsInfoBar.Severity = InfoBarSeverity.Error;
            SettingsInfoBar.Title = "无法绑定备份盘";
            SettingsInfoBar.Message = exception.Message;
        }
        finally
        {
            ChooseDriveButton.IsEnabled = true;
        }
    }

    private async Task RefreshStorageStatusAsync()
    {
        var settings = await AppServices.Settings.LoadAsync();
        if (string.IsNullOrWhiteSpace(settings.BackupRootPath))
        {
            DrivePathText.Text = "尚未选择备份盘";
            DriveStatusText.Text = "选择后将按卷唯一 ID 绑定。硬盘断开或空间不足时会暂停。";
            CachePathText.Text = "预览缓存将在选择硬盘后创建。";
            return;
        }

        DrivePathText.Text = settings.BackupRootPath;
        CachePathText.Text = $"预览缓存：{settings.CachePath}";
        var health = await AppServices.ReceiverProcess.EnsureRunningAsync();
        DriveStatusText.Text = health is null
            ? "接收服务离线，请重新选择硬盘或检查服务日志。"
            : $"卷 {health.Storage.VolumeId[..8]} · 可用 {FormatBytes(health.Storage.AvailableFreeSpace)}";
    }

    private async void SaveControlPlane_Click(object sender, RoutedEventArgs e)
    {
        SaveControlPlaneButton.IsEnabled = false;
        try
        {
            var url = ControlPlaneUrlBox.Text.Trim().TrimEnd('/');
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback))
            {
                throw new InvalidOperationException("控制面必须使用 HTTPS；仅本机调试可使用 HTTP。 ");
            }
            var settings = await AppServices.Settings.LoadAsync();
            await AppServices.Settings.SaveAsync(settings with { ControlPlaneUrl = url });
            if (!string.IsNullOrWhiteSpace(ControlPlaneTokenBox.Password))
            {
                await AppServices.Credentials.SaveControlTokenAsync(ControlPlaneTokenBox.Password);
                ControlPlaneTokenBox.Password = "";
            }
            if (await AppServices.Credentials.ReadControlTokenAsync() is null)
            {
                throw new InvalidOperationException("请输入控制面管理员令牌。");
            }

            var online = await AppServices.ControlPlane.CheckHealthAsync();
            ControlPlaneStatusText.Text = online ? "控制面在线" : "配置已保存，但控制面当前离线";
            SettingsInfoBar.Severity = online ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
            SettingsInfoBar.Title = online ? "手机服务器已连接" : "手机服务器暂时不可达";
            SettingsInfoBar.Message = online
                ? "现在可以查看设备任务并从 PC 下发立即备份指令。"
                : "请检查 Cloudflare Tunnel、证书和 Termux 服务状态。";
            SettingsInfoBar.IsOpen = true;
        }
        catch (Exception exception)
        {
            SettingsInfoBar.Severity = InfoBarSeverity.Error;
            SettingsInfoBar.Title = "控制面配置失败";
            SettingsInfoBar.Message = exception.Message;
            SettingsInfoBar.IsOpen = true;
        }
        finally
        {
            SaveControlPlaneButton.IsEnabled = true;
        }
    }

    private static string FormatBytes(long bytes)
    {
        var gibibytes = bytes / 1024d / 1024d / 1024d;
        return $"{gibibytes:0.0} GB";
    }
}
