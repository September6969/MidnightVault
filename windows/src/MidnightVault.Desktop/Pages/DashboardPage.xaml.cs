using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MidnightVault.Desktop.Services;

namespace MidnightVault.Desktop.Pages;

public sealed partial class DashboardPage : Page
{
    public DashboardPage()
    {
        InitializeComponent();
        NextRunText.Text = GetNextRunText();
    }

    private async void DashboardPage_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshDashboardAsync();
    }

    private static string GetNextRunText()
    {
        TimeZoneInfo chinaTimeZone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        DateTime chinaNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, chinaTimeZone);
        DateTime nextRun = chinaNow.Date.AddDays(1);
        return nextRun.ToString("M月d日 00:00");
    }

    private void DashboardPage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool isCompact = e.NewSize.Width < 900;
        bool isPhone = e.NewSize.Width < 640;

        DashboardContent.Padding = (Thickness)Application.Current.Resources[
            isPhone
                ? "MidnightPhonePagePadding"
                : isCompact
                    ? "MidnightCompactPagePadding"
                    : "MidnightPagePadding"];
        PageTitle.FontSize = isPhone ? 28 : 32;

        Grid.SetRow(HeaderActionButton, isCompact ? 1 : 0);
        Grid.SetColumn(HeaderActionButton, isCompact ? 0 : 1);
        HeaderActionButton.HorizontalAlignment = isPhone
            ? HorizontalAlignment.Stretch
            : isCompact
                ? HorizontalAlignment.Left
                : HorizontalAlignment.Right;
        HeaderActionButton.Margin = isCompact ? new Thickness(0, 6, 0, 0) : new Thickness(0);

        MetricColumnOne.Width = isCompact
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(1.25, GridUnitType.Star);
        MetricColumnTwo.Width = isCompact
            ? new GridLength(0)
            : new GridLength(1, GridUnitType.Star);
        MetricColumnThree.Width = isCompact
            ? new GridLength(0)
            : new GridLength(1, GridUnitType.Star);

        Grid.SetRow(DeviceCard, isCompact ? 1 : 0);
        Grid.SetColumn(DeviceCard, isCompact ? 0 : 1);
        Grid.SetRow(DriveCard, isCompact ? 2 : 0);
        Grid.SetColumn(DriveCard, isCompact ? 0 : 2);

        bool stackEmptyState = e.NewSize.Width < 760;
        EmptyStateImageColumn.Width = stackEmptyState ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        EmptyStateTextColumn.Width = stackEmptyState ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetRow(EmptyStateImage, 0);
        Grid.SetColumn(EmptyStateImage, 0);
        Grid.SetRow(EmptyStateTextPanel, stackEmptyState ? 1 : 0);
        Grid.SetColumn(EmptyStateTextPanel, stackEmptyState ? 0 : 1);
        EmptyStateImage.Width = stackEmptyState ? 128 : 150;
        EmptyStateImage.Height = stackEmptyState ? 128 : 150;
        EmptyStateTextPanel.HorizontalAlignment = stackEmptyState
            ? HorizontalAlignment.Center
            : HorizontalAlignment.Stretch;
    }

    private async void BackupNow_Click(object sender, RoutedEventArgs e)
    {
        HeaderActionButton.IsEnabled = false;
        try
        {
            var health = await AppServices.ReceiverProcess.EnsureRunningAsync();
            SetupInfoBar.Severity = health is null
                ? InfoBarSeverity.Warning
                : InfoBarSeverity.Success;
            SetupInfoBar.Title = health is null ? "接收服务尚未配置" : "PC 已准备接收";
            if (health is null)
            {
                SetupInfoBar.Message = "请先在“设置”中选择 E 盘上的备份目录。";
            }
            else
            {
                var settings = await AppServices.Settings.LoadAsync();
                var controlToken = await AppServices.Credentials.ReadControlTokenAsync();
                if (!string.IsNullOrWhiteSpace(settings.ControlPlaneUrl) && controlToken is not null)
                {
                    var devices = await AppServices.ControlPlane.GetDevicesAsync();
                    var activeDevices = devices.Where(device => device.RevokedAt is null).Take(2).ToList();
                    foreach (var device in activeDevices)
                    {
                        await AppServices.ControlPlane.CreateBackupCommandAsync(device.Id);
                    }
                    SetupInfoBar.Message = activeDevices.Count == 0
                        ? "控制面在线，但还没有已登记的 Android 设备。"
                        : $"已向 {activeDevices.Count} 台手机下发立即备份指令；手机最迟在下一次 15 分钟轮询时执行。";
                }
                else
                {
                    SetupInfoBar.Message = "接收服务在线。请在 Android 客户端点击“立即备份”，或先配置手机服务器以便远程下发。";
                }
            }
            await RefreshDashboardAsync();
        }
        catch (Exception exception)
        {
            SetupInfoBar.Severity = InfoBarSeverity.Error;
            SetupInfoBar.Title = "接收服务启动失败";
            SetupInfoBar.Message = exception.Message;
        }
        finally
        {
            HeaderActionButton.IsEnabled = true;
        }
    }

    private async Task RefreshDashboardAsync()
    {
        var settings = await AppServices.Settings.LoadAsync();
        if (string.IsNullOrWhiteSpace(settings.BackupRootPath))
        {
            SetupInfoBar.IsOpen = true;
            return;
        }

        try
        {
            var health = await AppServices.ReceiverProcess.EnsureRunningAsync();
            if (health is null) return;
            SetupInfoBar.Severity = InfoBarSeverity.Success;
            SetupInfoBar.Title = "接收服务运行中";
            SetupInfoBar.Message = $"备份盘 {health.Storage.DriveName} 在线，Android 可通过局域网上传。";
            SetupInfoBar.IsOpen = true;
            PairedDeviceCountText.Text = $"{Math.Min(health.Library.DeviceCount, 2)} / 2";
            DriveMetricText.Text = health.Storage.DriveName.TrimEnd('\\');
            DriveMetricDetailText.Text = $"可用 {FormatBytes(health.Storage.AvailableFreeSpace)}";

            if (health.Library.FileCount > 0)
            {
                RecentTaskTitleText.Text = "最近备份已完成";
                RecentTaskDescriptionText.Text =
                    $"资料库已有 {health.Library.FileCount} 个文件，共 {FormatBytes(health.Library.TotalBytes)}；所有文件均已通过 SHA-256 校验。";
                RecentTaskNextText.Text = health.Library.LatestCompletedAt is null
                    ? "接收端索引正常"
                    : $"最近完成：{health.Library.LatestCompletedAt.Value.ToLocalTime():yyyy-MM-dd HH:mm}";
            }
        }
        catch (Exception exception)
        {
            SetupInfoBar.Severity = InfoBarSeverity.Error;
            SetupInfoBar.Title = "接收服务不可用";
            SetupInfoBar.Message = exception.Message;
            SetupInfoBar.IsOpen = true;
        }
    }

    private static string FormatBytes(long bytes)
    {
        var gibibytes = bytes / 1024d / 1024d / 1024d;
        return gibibytes >= 1
            ? $"{gibibytes:0.##} GB"
            : $"{bytes / 1024d / 1024d:0.##} MB";
    }
}
