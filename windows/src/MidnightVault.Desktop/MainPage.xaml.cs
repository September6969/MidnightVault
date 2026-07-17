using MidnightVault.Desktop.DesignSystem;
using MidnightVault.Desktop.Pages;
using MidnightVault.Desktop.Testing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace MidnightVault.Desktop;

public sealed partial class MainPage : Page
{
    private bool _visualCaptureStarted;

    public MainPage()
    {
        InitializeComponent();
        if (VisualTestOptions.Enabled)
        {
            RequestedTheme = VisualTestOptions.Theme switch
            {
                "light" => ElementTheme.Light,
                "dark" => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
            Background = new SolidColorBrush(
                VisualTestOptions.Theme == "dark"
                    ? GeneratedDesignTokens.DarkBackground
                    : GeneratedDesignTokens.LightBackground);
        }
    }

    private async void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (ContentFrame.Content is null)
        {
            ShellNavigationView.SelectedItem = VisualTestOptions.Enabled
                ? VisualTestOptions.Page switch
                {
                    "library" => LibraryNavigationItem,
                    "devices" => DevicesNavigationItem,
                    "settings" => SettingsNavigationItem,
                    _ => DashboardNavigationItem,
                }
                : DashboardNavigationItem;
        }

        if (_visualCaptureStarted ||
            !VisualTestOptions.Enabled ||
            VisualTestOptions.OutputPath is not { } outputPath)
        {
            return;
        }

        _visualCaptureStarted = true;
        try
        {
            if (ContentFrame.Content is FrameworkElement content)
            {
                await WaitUntilLoadedAsync(content);
            }

            // 等待子页面模板、尺寸变化处理和图像解码完成，再固定本轮布局。
            await Task.Yield();
            await Task.Delay(1_500);
            var captureRoot = XamlRoot?.Content as FrameworkElement ?? this;
            var targetWidth = Math.Max(1, VisualTestOptions.Width - 16);
            var targetHeight = Math.Max(1, VisualTestOptions.Height - 9);
            if (captureRoot.ActualWidth + 1 < targetWidth ||
                captureRoot.ActualHeight + 1 < targetHeight)
            {
                // 显示器工作区不足时进行离屏布局，仍可验证 2K 等超出当前屏幕的窗口尺寸。
                captureRoot.Width = targetWidth;
                captureRoot.Height = targetHeight;
                captureRoot.Measure(new Windows.Foundation.Size(targetWidth, targetHeight));
                captureRoot.Arrange(new Windows.Foundation.Rect(0, 0, targetWidth, targetHeight));
            }
            captureRoot.UpdateLayout();
            await SavePngAsync(captureRoot, outputPath);
        }
        catch (Exception exception)
        {
            await File.WriteAllTextAsync(outputPath + ".error.txt", exception.ToString());
        }
    }

    private static async Task WaitUntilLoadedAsync(FrameworkElement element)
    {
        if (element.IsLoaded)
        {
            return;
        }

        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void OnLoaded(object sender, RoutedEventArgs args)
        {
            element.Loaded -= OnLoaded;
            completion.TrySetResult(true);
        }

        element.Loaded += OnLoaded;
        await completion.Task;
    }

    private static async Task SavePngAsync(FrameworkElement element, string outputPath)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("视觉基线路径必须包含有效目录。");
        }

        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap();
        var rasterScale = VisualTestOptions.RasterScalePercent / 100.0;
        await bitmap.RenderAsync(
            element,
            Math.Max(1, checked((int)Math.Round(element.ActualWidth * rasterScale))),
            Math.Max(1, checked((int)Math.Round(element.ActualHeight * rasterScale))));
        var pixelBuffer = await bitmap.GetPixelsAsync();
        if (bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0 || pixelBuffer.Length == 0)
        {
            throw new InvalidOperationException("RenderTargetBitmap 未返回有效像素。");
        }

        var pixels = new byte[checked((int)pixelBuffer.Length)];
        using (var reader = DataReader.FromBuffer(pixelBuffer))
        {
            reader.ReadBytes(pixels);
        }

        // Window 的 Mica 区域可能保留透明度，写入 PNG 前按当前主题合成为不透明背景。
        var background = string.Equals(
            VisualTestOptions.Theme,
            "dark",
            StringComparison.OrdinalIgnoreCase)
            ? GeneratedDesignTokens.DarkBackground
            : GeneratedDesignTokens.LightBackground;
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            var alpha = pixels[offset + 3];
            if (alpha == byte.MaxValue) continue;
            var inverseAlpha = byte.MaxValue - alpha;
            pixels[offset] = (byte)Math.Min(
                byte.MaxValue,
                pixels[offset] + ((background.B * inverseAlpha + 127) / byte.MaxValue));
            pixels[offset + 1] = (byte)Math.Min(
                byte.MaxValue,
                pixels[offset + 1] + ((background.G * inverseAlpha + 127) / byte.MaxValue));
            pixels[offset + 2] = (byte)Math.Min(
                byte.MaxValue,
                pixels[offset + 2] + ((background.R * inverseAlpha + 127) / byte.MaxValue));
            pixels[offset + 3] = byte.MaxValue;
        }

        using var output = await FileRandomAccessStream.OpenAsync(
            outputPath,
            FileAccessMode.ReadWrite,
            StorageOpenOptions.None,
            FileOpenDisposition.CreateAlways);
        output.Size = 0;

        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
        var dpi = 96.0 * rasterScale;
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            checked((uint)bitmap.PixelWidth),
            checked((uint)bitmap.PixelHeight),
            dpi,
            dpi,
            pixels);
        await encoder.FlushAsync();
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

    private void DashboardAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        SelectNavigationItem(DashboardNavigationItem, args);
    }

    private void LibraryAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        SelectNavigationItem(LibraryNavigationItem, args);
    }

    private void DevicesAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        SelectNavigationItem(DevicesNavigationItem, args);
    }

    private void SettingsAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        SelectNavigationItem(SettingsNavigationItem, args);
    }

    private void EscapeAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ShellNavigationView.IsPaneOpen)
        {
            ShellNavigationView.IsPaneOpen = false;
            args.Handled = true;
        }
    }

    private void SelectNavigationItem(
        NavigationViewItem item,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        ShellNavigationView.SelectedItem = item;
        args.Handled = true;
    }
}
