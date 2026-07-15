using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MidnightVault.Desktop.Pages;

public sealed partial class LibraryPage : Page
{
    public LibraryPage()
    {
        InitializeComponent();
    }

    private void LibraryPage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool isCompact = e.NewSize.Width < 760;
        Grid.SetColumnSpan(SearchBox, isCompact ? 2 : 1);
        Grid.SetRow(TypeFilter, isCompact ? 1 : 0);
        Grid.SetColumn(TypeFilter, isCompact ? 0 : 1);
        Grid.SetRow(LibraryCommandBar, isCompact ? 1 : 0);
        Grid.SetColumn(LibraryCommandBar, isCompact ? 1 : 2);
        FilterThirdColumn.Width = isCompact ? new GridLength(0) : GridLength.Auto;
    }
}
