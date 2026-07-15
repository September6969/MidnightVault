namespace MidnightVault.Receiver;

public sealed class ReceiverOptions
{
    public const string SectionName = "Receiver";

    public string RootPath { get; init; } = "E:\\MidnightVaultData";

    public string? UploadToken { get; init; }

    public int ChunkSizeBytes { get; init; } = 8 * 1024 * 1024;
}
