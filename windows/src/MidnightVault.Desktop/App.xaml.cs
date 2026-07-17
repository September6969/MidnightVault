using Microsoft.UI.Xaml;
using MidnightVault.Desktop.Services;

namespace MidnightVault.Desktop;

public partial class App : Application
{
    public static MainWindow? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();
        MainWindow.Activate();
        if (!Testing.VisualTestOptions.Enabled)
        {
            _ = AppServices.ReceiverProcess.EnsureRunningAsync();
        }
    }
}
