using System.Text.Json.Serialization;

namespace MidnightVault.Desktop.Services;

[JsonConverter(typeof(JsonStringEnumConverter<LibraryCategory>))]
public enum LibraryCategory
{
    Photo,
    Video,
    Download,
    Document,
    Custom
}

public sealed record LibraryItem(
    long Id,
    string DeviceId,
    string DeviceName,
    DateOnly BackupDate,
    LibraryCategory Category,
    string RelativePath,
    string RelativeStoredPath,
    long Size,
    string Sha256,
    DateTimeOffset OriginalModifiedAt,
    bool IsInitialBackup,
    DateTimeOffset CompletedAt)
{
    public string FileName => Path.GetFileName(RelativePath);

    public string CategoryLabel => Category switch
    {
        LibraryCategory.Photo => "照片",
        LibraryCategory.Video => "视频",
        LibraryCategory.Download => "下载",
        LibraryCategory.Document => "文档",
        _ => "自定义"
    };

    public string Detail => $"{DeviceName} · {BackupDate:yyyy-MM-dd} · {FormatBytes(Size)}";

    public string Extension => Path.GetExtension(RelativePath).ToLowerInvariant();

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }
}

public sealed record LibraryPage(
    IReadOnlyList<LibraryItem> Items,
    int Offset,
    int Limit,
    long Total);

public sealed record LibrarySummary(
    long FileCount,
    long TotalBytes,
    int DeviceCount,
    DateTimeOffset? LatestCompletedAt);

public sealed record ReceiverStorage(
    string RootPath,
    string VolumeId,
    string DriveName,
    long AvailableFreeSpace,
    long TotalSize,
    long MinimumFreeSpaceBytes);

public sealed record ReceiverHealth(
    string Status,
    string Service,
    DateTimeOffset Utc,
    ReceiverStorage Storage,
    LibrarySummary Library);

public sealed record PairingSession(string Code, DateTimeOffset ExpiresAt);

public sealed record PairedDevice(
    string DeviceId,
    string DeviceName,
    DateTimeOffset PairedAt,
    DateTimeOffset LastSeenAt);
