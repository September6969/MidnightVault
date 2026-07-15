using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MidnightVault.Desktop;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        InitializeComponent();
    }

    private void BackupNow_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "请先选择外接硬盘并完成手机配对";
    }
}
