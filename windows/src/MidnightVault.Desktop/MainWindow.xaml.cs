using Microsoft.UI.Xaml;
using MidnightVault.Desktop.DesignSystem;
using MidnightVault.Desktop.Testing;

namespace MidnightVault.Desktop;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = GeneratedDesignTokens.MinimumWindowWidth;
            presenter.PreferredMinimumHeight = GeneratedDesignTokens.MinimumWindowHeight;
        }
        if (VisualTestOptions.Enabled)
        {
            AppWindow.Resize(new Windows.Graphics.SizeInt32(
                VisualTestOptions.Width,
                VisualTestOptions.Height));
        }
        RootFrame.Navigate(typeof(MainPage));
    }
}
