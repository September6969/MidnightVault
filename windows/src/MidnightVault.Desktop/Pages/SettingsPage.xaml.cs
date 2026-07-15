using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MidnightVault.Desktop.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    private void SettingsPage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool isCompact = e.NewSize.Width < 760;
        DriveActionColumn.Width = isCompact ? new GridLength(0) : GridLength.Auto;
        Grid.SetRow(ChooseDriveButton, isCompact ? 1 : 0);
        Grid.SetColumn(ChooseDriveButton, isCompact ? 0 : 1);
        ChooseDriveButton.HorizontalAlignment = isCompact
            ? HorizontalAlignment.Left
            : HorizontalAlignment.Right;
        ChooseDriveButton.Margin = isCompact ? new Thickness(0, 14, 0, 0) : new Thickness(0);
    }

    private void ChooseDrive_Click(object sender, RoutedEventArgs e)
    {
        SettingsInfoBar.Title = "硬盘绑定尚未接入";
        SettingsInfoBar.Message = "界面已按卷唯一 ID 的方案预留入口；实际选择器和空间检测将在 M4 里实现。";
        SettingsInfoBar.IsOpen = true;
    }
}
