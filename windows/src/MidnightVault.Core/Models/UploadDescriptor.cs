namespace MidnightVault.Core.Models;

public sealed record UploadDescriptor(
    string DeviceId,
    string DeviceName,
    DateOnly BackupDate,
    BackupCategory Category,
    string RelativePath,
    long Size,
    string Sha256,
    DateTimeOffset OriginalModifiedAt,
    bool IsInitialBackup = false);
