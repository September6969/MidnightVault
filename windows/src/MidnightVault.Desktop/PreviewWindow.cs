using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Windowing;
using Windows.Media.Core;
using Windows.Storage;
using Windows.System;

namespace MidnightVault.Desktop;

public sealed class PreviewWindow : Window
{
    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".heic" };
    private static readonly HashSet<string> VideoExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mov", ".mkv", ".avi", ".webm", ".m4v" };
    private static readonly HashSet<string> TextExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".txt", ".md", ".json", ".xml", ".csv", ".log" };

    private PreviewWindow(string title)
    {
        Title = $"{title} | MidnightVault";
        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        var width = Math.Min(1000, Math.Max(640, displayArea.WorkArea.Width - 80));
        var height = Math.Min(720, Math.Max(480, displayArea.WorkArea.Height - 80));
        AppWindow.Resize(new Windows.Graphics.SizeInt32(width, height));
    }

    public static async Task<PreviewWindow?> CreateAsync(string path)
    {
        var extension = Path.GetExtension(path);
        var window = new PreviewWindow(Path.GetFileName(path));
        if (ImageExtensions.Contains(extension))
        {
            window.Content = new Grid
            {
                Padding = new Thickness(20),
                Children =
                {
                    new Image
                    {
                        Source = new BitmapImage(new Uri(path)) { DecodePixelWidth = 1800 },
                        Stretch = Stretch.Uniform
                    }
                }
            };
        }
        else if (VideoExtensions.Contains(extension))
        {
            window.Content = new MediaPlayerElement
            {
                AreTransportControlsEnabled = true,
                AutoPlay = true,
                Source = MediaSource.CreateFromUri(new Uri(path))
            };
        }
        else if (TextExtensions.Contains(extension))
        {
            var text = await ReadTextPreviewAsync(path);
            var textBox = new TextBox
            {
                Text = text,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Cascadia Mono"),
                Padding = new Thickness(20)
            };
            ScrollViewer.SetVerticalScrollBarVisibility(textBox, ScrollBarVisibility.Auto);
            ScrollViewer.SetHorizontalScrollBarVisibility(textBox, ScrollBarVisibility.Auto);
            window.Content = textBox;
        }
        else if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            window.Content = new WebView2 { Source = new Uri(path) };
        }
        else
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            await Launcher.LaunchFileAsync(file);
            window.Close();
            return null;
        }
        return window;
    }

    private static async Task<string> ReadTextPreviewAsync(string path)
    {
        const int maximumCharacters = 2 * 1024 * 1024;
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        var buffer = new char[maximumCharacters];
        var read = await reader.ReadBlockAsync(buffer.AsMemory());
        var extra = new char[1];
        var truncated = read == buffer.Length && await reader.ReadAsync(extra.AsMemory()) > 0;
        var suffix = truncated ? "\n\n预览仅显示前 2 MB" : "";
        return new string(buffer, 0, read) + suffix;
    }
}
