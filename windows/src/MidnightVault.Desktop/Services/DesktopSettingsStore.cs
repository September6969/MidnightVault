using System.Text.Json;

namespace MidnightVault.Desktop.Services;

public sealed record DesktopSettings(
    string? BackupRootPath = null,
    string? ExpectedVolumeId = null,
    int ReceiverPort = 5053,
    string? ControlPlaneUrl = null)
{
    public string ReceiverBaseUrl => $"http://127.0.0.1:{ReceiverPort}";

    public string? CachePath => string.IsNullOrWhiteSpace(BackupRootPath)
        ? null
        : Path.Combine(BackupRootPath, ".midnight-vault", "cache");
}

public sealed class DesktopSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public async Task<DesktopSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SettingsPath)) return new DesktopSettings();
        await using var stream = File.OpenRead(SettingsPath);
        return await JsonSerializer.DeserializeAsync<DesktopSettings>(stream, JsonOptions, cancellationToken)
            ?? new DesktopSettings();
    }

    public async Task SaveAsync(DesktopSettings settings, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            var temporary = SettingsPath + ".tmp";
            await using (var stream = new FileStream(
                temporary,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, SettingsPath, overwrite: true);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static string SettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MidnightVault");

    private static string SettingsPath => Path.Combine(SettingsDirectory, "desktop-settings.json");
}
