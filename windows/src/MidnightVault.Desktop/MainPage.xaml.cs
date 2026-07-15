using MidnightVault.Desktop.Pages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MidnightVault.Desktop;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        InitializeComponent();
    }

    private void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (ContentFrame.Content is null)
        {
            ShellNavigationView.SelectedItem = DashboardNavigationItem;
        }
    }

    private void ShellNavigationView_SelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string pageTag)
        {
            return;
        }

        Type? pageType = pageTag switch
        {
            "dashboard" => typeof(DashboardPage),
            "library" => typeof(LibraryPage),
            "devices" => typeof(DevicesPage),
            "settings" => typeof(SettingsPage),
            _ => null,
        };

        if (pageType is not null && ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }
    }
}
