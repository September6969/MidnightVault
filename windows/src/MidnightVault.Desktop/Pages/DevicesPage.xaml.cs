using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MidnightVault.Desktop.Pages;

public sealed partial class DevicesPage : Page
{
    public DevicesPage()
    {
        InitializeComponent();
    }

    private void PairDevice_Click(object sender, RoutedEventArgs e)
    {
        PairingInfoBar.Title = "二维码配对尚未接入";
        PairingInfoBar.Message = "界面已预留配对流程；设备密钥交换与防重放签名将在 M2 里实现。";
        PairingInfoBar.IsOpen = true;
    }
}
