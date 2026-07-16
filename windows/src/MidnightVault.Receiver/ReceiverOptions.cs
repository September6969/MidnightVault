namespace MidnightVault.Receiver;

public sealed class ReceiverOptions
{
    public const string SectionName = "Receiver";

    public string RootPath { get; init; } = "E:\\MidnightVaultData";

    public string? UploadToken { get; init; }

    public int ChunkSizeBytes { get; init; } = 8 * 1024 * 1024;

    public long MinimumFreeSpaceBytes { get; init; } = 1024L * 1024 * 1024;

    public double MinimumFreeSpaceRatio { get; init; } = 0.10;

    public string? ExpectedVolumeId { get; init; }

    public int CompletedSessionRetentionDays { get; init; } = 7;

    public int IncompleteSessionRetentionDays { get; init; } = 30;
}
