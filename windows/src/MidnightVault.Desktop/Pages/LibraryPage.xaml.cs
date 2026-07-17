using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MidnightVault.Desktop.DesignSystem;
using MidnightVault.Desktop.Services;
using MidnightVault.Desktop.Testing;
using Windows.ApplicationModel.DataTransfer;

namespace MidnightVault.Desktop.Pages;

public sealed partial class LibraryPage : Page
{
    private CancellationTokenSource? _searchCancellation;
    private readonly List<Window> _previewWindows = [];
    private DesktopLayoutMode? _layoutMode;

    public LibraryPage()
    {
        InitializeComponent();
        Unloaded += (_, _) => _searchCancellation?.Cancel();
    }

    private async void LibraryPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (VisualTestOptions.Enabled) return;
        await RequestReloadAsync();
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
            LibraryItemsRepeater.ItemsSource = page.Items;
            LibraryEmptyState.Visibility = page.Items.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            LibraryResults.Visibility = page.Items.Count == 0
                ? Visibility.Collapsed
                : Visibility.Visible;
            ResultCountText.Text = $"共 {page.Total} 个文件，当前显示 {page.Items.Count} 个";
            LibraryInfoBar.IsOpen = false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 搜索防抖取消属于正常流程。
        }
        catch (Exception exception)
        {
            ShowError($"请确认备份盘和接收服务在线，然后重试。详细信息：{exception.Message}");
        }
    }

    private async Task RequestReloadAsync(TimeSpan? delay = null)
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        var request = new CancellationTokenSource();
        _searchCancellation = request;
        LibraryLoadingIndicator.IsActive = true;
        LibraryLoadingIndicator.Visibility = Visibility.Visible;
        try
        {
            if (delay is not null)
            {
                await Task.Delay(delay.Value, request.Token);
            }
            await LoadLibraryAsync(request.Token);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
            // 后发查询取代前一次查询时取消旧请求。
        }
        finally
        {
            if (ReferenceEquals(_searchCancellation, request))
            {
                _searchCancellation = null;
                LibraryLoadingIndicator.IsActive = false;
                LibraryLoadingIndicator.Visibility = Visibility.Collapsed;
            }
            request.Dispose();
        }
    }

    private async void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        await RequestReloadAsync(TimeSpan.FromMilliseconds(300));
    }

    private async void TypeFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        await RequestReloadAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RequestReloadAsync();
    }

    private async void PreviewFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: LibraryItem item }) return;
        await PreviewItemAsync(item);
    }

    private async void LibraryItem_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: LibraryItem item }) return;
        e.Handled = true;
        await PreviewItemAsync(item);
    }

    private async Task PreviewItemAsync(LibraryItem item)
    {
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

    private async void OpenInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: LibraryItem item }) return;
        try
        {
            var path = await AppServices.ReceiverApi.GetLocalOrCachedPathAsync(item);
            Process.Start(new ProcessStartInfo("explorer.exe")
            {
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception exception)
        {
            ShowError($"无法在资源管理器中定位文件。详细信息：{exception.Message}");
        }
    }

    private void CopyOriginalPath_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: LibraryItem item }) return;
        var data = new DataPackage();
        data.SetText(item.RelativePath);
        Clipboard.SetContent(data);
        LibraryInfoBar.Title = "原始路径已复制";
        LibraryInfoBar.Message = "可粘贴到记录或搜索框中；复制操作不会修改备份文件。";
        LibraryInfoBar.Severity = InfoBarSeverity.Success;
        LibraryInfoBar.IsOpen = true;
    }

    private void FocusSearch_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        SearchBox.Focus(FocusState.Keyboard);
        args.Handled = true;
    }

    private async void RefreshAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await RequestReloadAsync();
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
        var layoutMode = AdaptiveLayout.Classify(e.NewSize.Width);
        if (_layoutMode == layoutMode) return;
        _layoutMode = layoutMode;
        bool isCompact = layoutMode != DesktopLayoutMode.Expanded;
        bool isPhone = layoutMode == DesktopLayoutMode.Compact;

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
