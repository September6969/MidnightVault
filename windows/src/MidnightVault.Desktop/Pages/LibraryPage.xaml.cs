using System.Diagnostics;
using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using MidnightVault.Desktop.DesignSystem;
using MidnightVault.Desktop.Services;
using MidnightVault.Desktop.Testing;
using Windows.ApplicationModel.DataTransfer;

namespace MidnightVault.Desktop.Pages;

public sealed partial class LibraryPage : Page
{
    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _thumbnailCancellation;
    private readonly List<Window> _previewWindows = [];
    private IReadOnlyList<LibraryFolderSection> _folderSections = [];
    private LibraryFileCard? _selectedCard;
    private long _totalCount;
    private bool _updatingFolderFilter;
    private DesktopLayoutMode? _layoutMode;

    public LibraryPage()
    {
        InitializeComponent();
        Unloaded += (_, _) =>
        {
            _searchCancellation?.Cancel();
            _thumbnailCancellation?.Cancel();
        };
    }

    private async void LibraryPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (VisualTestOptions.Enabled)
        {
            LoadVisualTestLibrary();
            return;
        }
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
                limit: 500,
                cancellationToken: cancellationToken);
            var cards = page.Items.Select(item => new LibraryFileCard(item)).ToList();
            _totalCount = page.Total;
            _folderSections = BuildFolderSections(cards);
            UpdateFolderOptions();
            ApplyFolderFilter();
            LibraryEmptyState.Visibility = cards.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            LibraryResults.Visibility = cards.Count == 0
                ? Visibility.Collapsed
                : Visibility.Visible;
            LibraryInfoBar.IsOpen = false;
            StartThumbnailLoading(cards);

            var selected = _selectedCard is null
                ? null
                : cards.FirstOrDefault(card => card.Item.Id == _selectedCard.Item.Id);
            selected ??= cards.FirstOrDefault(card => card.IsImage) ?? cards.FirstOrDefault();
            if (selected is not null)
            {
                await SelectCardAsync(selected, cancellationToken);
            }
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

    private void FolderFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingFolderFilter) return;
        ApplyFolderFilter();
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
        if (sender is not FrameworkElement { Tag: LibraryFileCard card }) return;
        e.Handled = true;
        await PreviewItemAsync(card.Item);
    }

    private async void LibraryCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: LibraryFileCard card }) return;
        await SelectCardAsync(card);
    }

    private async void OpenSelectedPreview_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCard is null) return;
        await PreviewItemAsync(_selectedCard.Item);
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
        FolderFilter.IsEnabled = enabled && _folderSections.Count > 0;
        RefreshButton.IsEnabled = enabled;
    }

    private static IReadOnlyList<LibraryFolderSection> BuildFolderSections(
        IReadOnlyList<LibraryFileCard> cards)
    {
        return cards
            .GroupBy(card =>
            {
                var folder = GetFolderPath(card.RelativePath);
                return new
                {
                    card.Item.DeviceId,
                    card.Item.DeviceName,
                    card.Item.BackupDate,
                    card.Item.Category,
                    card.CategoryLabel,
                    Folder = folder
                };
            })
            .OrderBy(group => group.Key.DeviceName, StringComparer.CurrentCultureIgnoreCase)
            .ThenByDescending(group => group.Key.BackupDate)
            .ThenBy(group => group.Key.Category)
            .ThenBy(group => group.Key.Folder, StringComparer.CurrentCultureIgnoreCase)
            .Select(group =>
            {
                var folderName = group.Key.Folder.Length == 0
                    ? "根目录"
                    : group.Key.Folder.Split('/').Last();
                var key = string.Join(
                    '|',
                    group.Key.DeviceId,
                    group.Key.BackupDate.ToString("O"),
                    group.Key.Category,
                    group.Key.Folder);
                var breadcrumb = $"{group.Key.DeviceName}  ›  {group.Key.BackupDate:yyyy-MM-dd}  ›  {group.Key.CategoryLabel}";
                if (group.Key.Folder.Length > 0)
                {
                    breadcrumb += "  ›  " + group.Key.Folder.Replace("/", "  ›  ");
                }
                return new LibraryFolderSection(
                    key,
                    folderName,
                    breadcrumb,
                    group.OrderBy(card => card.FileName, StringComparer.CurrentCultureIgnoreCase).ToList());
            })
            .ToList();
    }

    private static string GetFolderPath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').Trim('/');
        var separator = normalized.LastIndexOf('/');
        return separator <= 0 ? "" : normalized[..separator];
    }

    private void UpdateFolderOptions()
    {
        var selectedKey = (FolderFilter.SelectedItem as LibraryFolderOption)?.Key;
        var options = new List<LibraryFolderOption> { new("全部文件夹", null) };
        options.AddRange(_folderSections.Select(section =>
            new LibraryFolderOption($"{section.Name} · {section.Items.Count}", section.Key)));

        _updatingFolderFilter = true;
        FolderFilter.ItemsSource = options;
        FolderFilter.SelectedItem = options.FirstOrDefault(option => option.Key == selectedKey) ?? options[0];
        FolderFilter.IsEnabled = _folderSections.Count > 0;
        _updatingFolderFilter = false;
    }

    private void ApplyFolderFilter()
    {
        var selectedKey = (FolderFilter.SelectedItem as LibraryFolderOption)?.Key;
        var visibleSections = selectedKey is null
            ? _folderSections
            : _folderSections.Where(section => section.Key == selectedKey).ToList();
        LibraryFolderRepeater.ItemsSource = visibleSections;
        var visibleCount = visibleSections.Sum(section => section.Items.Count);
        ResultCountText.Text = _totalCount > visibleCount
            ? $"资料库共 {_totalCount} 个文件，当前分区显示 {visibleCount} 个"
            : $"共 {visibleCount} 个文件，分为 {visibleSections.Count} 个文件夹";

        if (_selectedCard is not null &&
            !visibleSections.SelectMany(section => section.Items).Contains(_selectedCard))
        {
            var first = visibleSections.SelectMany(section => section.Items).FirstOrDefault();
            if (first is not null) _ = SelectCardAsync(first);
        }
        DispatcherQueue.TryEnqueue(ConfigurePreviewStickyAnimation);
    }

    private void StartThumbnailLoading(IReadOnlyList<LibraryFileCard> cards)
    {
        _thumbnailCancellation?.Cancel();
        _thumbnailCancellation?.Dispose();
        _thumbnailCancellation = new CancellationTokenSource();
        _ = LoadThumbnailsAsync(cards, _thumbnailCancellation.Token);
    }

    private async Task LoadThumbnailsAsync(
        IReadOnlyList<LibraryFileCard> cards,
        CancellationToken cancellationToken)
    {
        foreach (var card in cards.Where(card => card.IsImage))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var path = await AppServices.ReceiverApi.GetLocalOrCachedPathAsync(
                    card.Item,
                    cancellationToken);
                card.ThumbnailSource = CreateBitmap(path, 360);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                // 单张图片解码失败时保留文件类型占位图，不影响其他分区。
            }
        }
    }

    private async Task SelectCardAsync(
        LibraryFileCard card,
        CancellationToken cancellationToken = default)
    {
        _selectedCard = card;
        SelectedPreviewPanel.DataContext = card;
        PreviewHintText.Visibility = Visibility.Collapsed;
        SelectedPreviewContent.Visibility = Visibility.Visible;
        SelectedFileName.Text = card.FileName;
        SelectedFilePath.Text = card.RelativePath;
        SelectedFileDetail.Text = $"{card.Detail} · {card.CategoryLabel}";
        SelectedPreviewImage.Source = null;
        SelectedPreviewImage.Visibility = card.IsImage ? Visibility.Visible : Visibility.Collapsed;
        SelectedPreviewFallback.Visibility = Visibility.Visible;
        OpenSelectedPreviewButton.Content = card.IsImage ? "打开大图" : "打开预览";

        if (!card.IsImage) return;
        try
        {
            var path = await AppServices.ReceiverApi.GetLocalOrCachedPathAsync(
                card.Item,
                cancellationToken);
            if (!ReferenceEquals(_selectedCard, card)) return;
            var source = CreateBitmap(path, 960);
            SelectedPreviewImage.Source = source;
            SelectedPreviewFallback.Visibility = Visibility.Collapsed;
            card.ThumbnailSource ??= CreateBitmap(path, 360);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 新查询会替换旧选择。
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(_selectedCard, card))
            {
                ShowError($"无法加载图片预览。详细信息：{exception.Message}");
            }
        }
    }

    private static BitmapImage CreateBitmap(string path, int decodePixelWidth) => new(new Uri(path))
    {
        DecodePixelWidth = decodePixelWidth
    };

    private void LoadVisualTestLibrary()
    {
        var items = new List<LibraryItem>
        {
            new LibraryItem(1, "phone-a", "主力手机", new DateOnly(2026, 7, 18), LibraryCategory.Photo,
                "DCIM/Camera/湖边晚霞.jpg", "", 4_820_112, "a", DateTimeOffset.Now, false, DateTimeOffset.Now),
            new LibraryItem(2, "phone-a", "主力手机", new DateOnly(2026, 7, 18), LibraryCategory.Photo,
                "DCIM/Camera/城市夜景.jpg", "", 3_410_660, "b", DateTimeOffset.Now, false, DateTimeOffset.Now),
            new LibraryItem(3, "phone-a", "主力手机", new DateOnly(2026, 7, 18), LibraryCategory.Photo,
                "Pictures/Screenshots/备份设置.png", "", 1_280_024, "c", DateTimeOffset.Now, false, DateTimeOffset.Now),
            new LibraryItem(4, "phone-b", "备用手机", new DateOnly(2026, 7, 17), LibraryCategory.Document,
                "Documents/旅行清单.pdf", "", 860_160, "d", DateTimeOffset.Now, false, DateTimeOffset.Now)
        };
        for (var index = 0; index < 8; index++)
        {
            items.Add(new LibraryItem(
                10 + index,
                "phone-a",
                "主力手机",
                new DateOnly(2026, 7, 16),
                LibraryCategory.Photo,
                $"Pictures/Archive/往年照片_{index + 1:00}.jpg",
                "",
                2_400_000 + index * 120_000,
                $"visual-{index}",
                DateTimeOffset.Now,
                false,
                DateTimeOffset.Now));
        }
        var cards = items.Select(item => new LibraryFileCard(item)).ToList();
        var sampleImage = new BitmapImage(new Uri(
            "ms-appx:///Assets/Generated/Processed/empty-library.png"));
        foreach (var card in cards.Where(card => card.IsImage))
        {
            card.ThumbnailSource = sampleImage;
        }

        _totalCount = cards.Count;
        _folderSections = BuildFolderSections(cards);
        UpdateFolderOptions();
        ApplyFolderFilter();
        SetControlsEnabled(true);
        LibraryEmptyState.Visibility = Visibility.Collapsed;
        LibraryResults.Visibility = Visibility.Visible;

        var selected = cards[0];
        _selectedCard = selected;
        SelectedPreviewPanel.DataContext = selected;
        PreviewHintText.Visibility = Visibility.Collapsed;
        SelectedPreviewContent.Visibility = Visibility.Visible;
        SelectedFileName.Text = selected.FileName;
        SelectedFilePath.Text = selected.RelativePath;
        SelectedFileDetail.Text = $"{selected.Detail} · {selected.CategoryLabel}";
        SelectedPreviewImage.Source = sampleImage;
        SelectedPreviewImage.Visibility = Visibility.Visible;
        SelectedPreviewFallback.Visibility = Visibility.Collapsed;
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

        ResultsBodyGrid.ColumnDefinitions[1].Width = isCompact
            ? new GridLength(0)
            : new GridLength(340);
        Grid.SetRow(SelectedPreviewPanel, isCompact ? 0 : 0);
        Grid.SetColumn(SelectedPreviewPanel, isCompact ? 0 : 1);
        Grid.SetColumnSpan(SelectedPreviewPanel, isCompact ? 1 : 1);
        Grid.SetRow(LibraryFolderRepeater, isCompact ? 1 : 0);
        Grid.SetColumn(LibraryFolderRepeater, 0);
        SelectedPreviewPanel.Margin = isCompact
            ? new Thickness(0, 0, 0, 18)
            : new Thickness(18, 0, 0, 0);
        SelectedPreviewPanel.MinHeight = isCompact ? 240 : 300;
        SelectedPreviewMedia.Height = isCompact ? 180 : 260;
        ConfigurePreviewStickyAnimation();
    }

    private void StickyLayout_SizeChanged(object sender, SizeChangedEventArgs e) =>
        DispatcherQueue.TryEnqueue(ConfigurePreviewStickyAnimation);

    private void ConfigurePreviewStickyAnimation()
    {
        var previewVisual = ElementCompositionPreview.GetElementVisual(SelectedPreviewPanel);
        ElementCompositionPreview.SetIsTranslationEnabled(SelectedPreviewPanel, true);
        previewVisual.StopAnimation("Translation.Y");
        SelectedPreviewPanel.Translation = Vector3.Zero;

        if (_layoutMode != DesktopLayoutMode.Expanded ||
            SelectedPreviewPanel.ActualHeight <= 0)
        {
            return;
        }

        var scrollProperties = ElementCompositionPreview
            .GetScrollViewerManipulationPropertySet(LibraryScrollViewer);
        var animation = previewVisual.Compositor.CreateExpressionAnimation(
            "Max(-scroll.Translation.Y, 0)");
        animation.SetReferenceParameter("scroll", scrollProperties);
        previewVisual.StartAnimation("Translation.Y", animation);
    }
}
