using MidnightVault.Core.Models;

namespace MidnightVault.Receiver.Contracts;

public sealed record LibraryItemResponse(
    long Id,
    string DeviceId,
    string DeviceName,
    DateOnly BackupDate,
    BackupCategory Category,
    string RelativePath,
    string RelativeStoredPath,
    long Size,
    string Sha256,
    DateTimeOffset OriginalModifiedAt,
    bool IsInitialBackup,
    DateTimeOffset CompletedAt);

public sealed record LibraryPageResponse(
    IReadOnlyList<LibraryItemResponse> Items,
    int Offset,
    int Limit,
    long Total);

public sealed record LibrarySummaryResponse(
    long FileCount,
    long TotalBytes,
    int DeviceCount,
    DateTimeOffset? LatestCompletedAt);
