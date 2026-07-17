using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Media;
using MidnightVault.Desktop.Services;

namespace MidnightVault.Desktop.Pages;

public sealed class LibraryFileCard(LibraryItem item) : INotifyPropertyChanged
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".heic"
    };

    private ImageSource? _thumbnailSource;

    public LibraryItem Item { get; } = item;

    public string FileName => Item.FileName;

    public string RelativePath => Item.RelativePath;

    public string Detail => Item.Detail;

    public string CategoryLabel => Item.CategoryLabel;

    public bool IsImage => ImageExtensions.Contains(Item.Extension);

    public ImageSource? ThumbnailSource
    {
        get => _thumbnailSource;
        set
        {
            if (ReferenceEquals(_thumbnailSource, value)) return;
            _thumbnailSource = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record LibraryFolderSection(
    string Key,
    string Name,
    string Breadcrumb,
    IReadOnlyList<LibraryFileCard> Items);

public sealed record LibraryFolderOption(string Label, string? Key);
