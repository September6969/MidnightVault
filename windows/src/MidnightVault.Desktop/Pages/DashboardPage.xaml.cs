using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MidnightVault.Desktop.Pages;

public sealed partial class DashboardPage : Page
{
    public DashboardPage()
    {
        InitializeComponent();
        NextRunText.Text = GetNextRunText();
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
        Grid.SetRow(HeaderActionButton, isCompact ? 1 : 0);
        Grid.SetColumn(HeaderActionButton, isCompact ? 0 : 1);
        HeaderActionButton.HorizontalAlignment = isCompact
            ? HorizontalAlignment.Left
            : HorizontalAlignment.Right;
        HeaderActionButton.Margin = isCompact ? new Thickness(0, 8, 0, 0) : new Thickness(0);

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
    }

    private void BackupNow_Click(object sender, RoutedEventArgs e)
    {
        SetupInfoBar.Severity = InfoBarSeverity.Warning;
        SetupInfoBar.Title = "暂时无法开始备份";
        SetupInfoBar.Message = "请先在“设置”中选择外接硬盘，并在“设备”中完成手机配对。";
    }
}
