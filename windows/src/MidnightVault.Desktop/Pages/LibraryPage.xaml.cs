using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MidnightVault.Desktop.Services;

namespace MidnightVault.Desktop.Pages;

public sealed partial class LibraryPage : Page
{
    private CancellationTokenSource? _searchCancellation;
    private readonly List<Window> _previewWindows = [];

    public LibraryPage()
    {
        InitializeComponent();
    }

    private async void LibraryPage_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadLibraryAsync();
    }

    private async Task LoadLibraryAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var health = await AppServices.ReceiverProcess.EnsureRunningAsync(cancellationToken);
            if (health is null)
            {
                ShowError("请先在“设置”中选择 E 盘上的备份目录。");
                SetControlsEnabled(false);
                return;
            }

            SetControlsEnabled(true);
            var category = TypeFilter.SelectedIndex switch
            {
                1 => LibraryCategory.Photo,
                2 => LibraryCategory.Video,
                3 => LibraryCategory.Download,
                4 => LibraryCategory.Document,
                _ => (LibraryCategory?)null
            };
            var page = await AppServices.ReceiverApi.QueryLibraryAsync(
                SearchBox.Text,
                category,
                cancellationToken: cancellationToken);
            LibraryItemsControl.ItemsSource = page.Items;
            LibraryEmptyState.Visibility = page.Items.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            LibraryResults.Visibility = page.Items.Count == 0
                ? Visibility.Collapsed
                : Visibility.Visible;
            ResultCountText.Text = $"共 {page.Total} 个文件，当前显示 {page.Items.Count} 个";
            LibraryInfoBar.IsOpen = false;
        }
        catch (OperationCanceledException)
        {
            // 搜索防抖取消属于正常流程。
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private async void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();
        try
        {
            await Task.Delay(300, _searchCancellation.Token);
            await LoadLibraryAsync(_searchCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // 用户继续输入时取消上一次查询。
        }
    }

    private async void TypeFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        await LoadLibraryAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadLibraryAsync();
    }

    private async void PreviewFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: LibraryItem item }) return;
        try
        {
            var path = await AppServices.ReceiverApi.GetLocalOrCachedPathAsync(item);
            var preview = await PreviewWindow.CreateAsync(path);
            if (preview is null) return;
            _previewWindows.Add(preview);
            preview.Closed += (_, _) => _previewWindows.Remove(preview);
            preview.Activate();
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private void SetControlsEnabled(bool enabled)
    {
        SearchBox.IsEnabled = enabled;
        TypeFilter.IsEnabled = enabled;
        RefreshButton.IsEnabled = enabled;
    }

    private void ShowError(string message)
    {
        LibraryInfoBar.Title = "资料库暂时不可用";
        LibraryInfoBar.Message = message;
        LibraryInfoBar.Severity = InfoBarSeverity.Error;
        LibraryInfoBar.IsOpen = true;
    }

    private void LibraryPage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool isCompact = e.NewSize.Width < 760;
        bool isPhone = e.NewSize.Width < 640;

        LibraryContent.Padding = (Thickness)Application.Current.Resources[
            isPhone
                ? "MidnightPhonePagePadding"
                : isCompact
                    ? "MidnightCompactPagePadding"
                    : "MidnightPagePadding"];
        PageTitle.FontSize = isPhone ? 28 : 32;

        Grid.SetColumnSpan(SearchBox, isCompact ? 2 : 1);
        Grid.SetRow(TypeFilter, isCompact ? 1 : 0);
        Grid.SetColumn(TypeFilter, isCompact ? 0 : 1);
        Grid.SetRow(LibraryCommandBar, isCompact ? 1 : 0);
        Grid.SetColumn(LibraryCommandBar, isCompact ? 1 : 2);
        FilterThirdColumn.Width = isCompact ? new GridLength(0) : GridLength.Auto;
        TypeFilter.HorizontalAlignment = isCompact
            ? HorizontalAlignment.Stretch
            : HorizontalAlignment.Left;

        EmptyImageColumn.Width = isCompact
            ? new GridLength(1, GridUnitType.Star)
            : GridLength.Auto;
        EmptyTextColumn.Width = isCompact
            ? new GridLength(0)
            : new GridLength(1, GridUnitType.Star);
        Grid.SetRow(EmptyLibraryImage, 0);
        Grid.SetColumn(EmptyLibraryImage, 0);
        Grid.SetRow(EmptyTextPanel, isCompact ? 1 : 0);
        Grid.SetColumn(EmptyTextPanel, isCompact ? 0 : 1);
        EmptyLibraryImage.Width = isPhone ? 136 : 176;
        EmptyLibraryImage.Height = isPhone ? 136 : 176;
        EmptyTextPanel.HorizontalAlignment = isCompact
            ? HorizontalAlignment.Center
            : HorizontalAlignment.Stretch;
    }
}
