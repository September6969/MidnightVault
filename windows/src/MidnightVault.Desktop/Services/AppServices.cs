namespace MidnightVault.Desktop.Services;

public static class AppServices
{
    public static DesktopSettingsStore Settings { get; } = new();
    public static DesktopCredentialStore Credentials { get; } = new();
    public static ReceiverApiClient ReceiverApi { get; } = new(Settings, Credentials);
    public static ReceiverProcessManager ReceiverProcess { get; } = new(
        Settings,
        Credentials,
        ReceiverApi);
    public static ControlPlaneClient ControlPlane { get; } = new(Settings, Credentials);

    public static async Task<ReceiverHealth?> ConfigureStorageAsync(
        string backupRootPath,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(backupRootPath);
        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory);
        if (string.Equals(
            Path.GetPathRoot(root),
            systemRoot,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("为防止误写系统盘，请选择其他硬盘上的目录。");
        }

        Directory.CreateDirectory(root);
        var current = await Settings.LoadAsync(cancellationToken);
        var changedRoot = !string.Equals(current.BackupRootPath, root, StringComparison.OrdinalIgnoreCase);
        var updated = current with
        {
            BackupRootPath = root,
            ExpectedVolumeId = changedRoot ? null : current.ExpectedVolumeId
        };
        await Settings.SaveAsync(updated, cancellationToken);
        await Credentials.GetOrCreateUploadTokenAsync(cancellationToken);
        var health = await ReceiverProcess.RestartAsync(cancellationToken);
        if (health is not null && string.IsNullOrWhiteSpace(updated.ExpectedVolumeId))
        {
            await Settings.SaveAsync(updated with
            {
                ExpectedVolumeId = health.Storage.VolumeId
            }, cancellationToken);
        }
        return health;
    }
}
