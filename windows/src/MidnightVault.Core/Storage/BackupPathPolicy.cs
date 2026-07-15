using MidnightVault.Core.Models;

namespace MidnightVault.Core.Storage;

public sealed class BackupPathPolicy
{
    private static readonly IReadOnlyDictionary<BackupCategory, string> CategoryFolders =
        new Dictionary<BackupCategory, string>
        {
            [BackupCategory.Photo] = "照片",
            [BackupCategory.Video] = "视频",
            [BackupCategory.Download] = "下载",
            [BackupCategory.Document] = "文档",
            [BackupCategory.Custom] = "自定义"
        };

    public string ResolveDestination(string rootPath, UploadDescriptor descriptor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ValidateDescriptor(descriptor);

        var rawSegments = descriptor.RelativePath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (rawSegments.Length == 0 || rawSegments.Any(segment => segment is "." or ".."))
        {
            throw new ArgumentException("相对路径无效。", nameof(descriptor));
        }

        var dateFolder = descriptor.IsInitialBackup
            ? $"首次备份_{descriptor.BackupDate:yyyy-MM-dd}"
            : $"{descriptor.BackupDate:yyyy-MM-dd}";

        var segments = new List<string>
        {
            FileNameSanitizer.SanitizeSegment(descriptor.DeviceName),
            dateFolder,
            CategoryFolders[descriptor.Category]
        };
        segments.AddRange(rawSegments.Select(FileNameSanitizer.SanitizeSegment));

        var fullRoot = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var destination = Path.GetFullPath(Path.Combine([fullRoot, .. segments]));
        if (!destination.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("目标路径越过了备份根目录。 ");
        }

        return destination;
    }

    private static void ValidateDescriptor(UploadDescriptor descriptor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.DeviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.DeviceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.RelativePath);

        if (descriptor.Size < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(descriptor), "文件大小不能为负数。 ");
        }

        if (descriptor.Sha256.Length != 64 || !descriptor.Sha256.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("SHA-256 必须是 64 位十六进制字符串。", nameof(descriptor));
        }
    }
}
