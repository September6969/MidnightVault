using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MidnightVault.Desktop.Services;

public sealed class ReceiverProcessManager(
    DesktopSettingsStore settingsStore,
    DesktopCredentialStore credentialStore,
    ReceiverApiClient apiClient)
{
    private Process? _managedProcess;
    private readonly SemaphoreSlim _startLock = new(1, 1);

    public async Task<ReceiverHealth?> EnsureRunningAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsStore.LoadAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(settings.BackupRootPath)) return null;

        var current = await apiClient.GetHealthAsync(cancellationToken);
        if (current is not null) return ValidateRunningReceiver(current, settings);

        await _startLock.WaitAsync(cancellationToken);
        try
        {
            current = await apiClient.GetHealthAsync(cancellationToken);
            if (current is not null) return ValidateRunningReceiver(current, settings);

            var token = await credentialStore.GetOrCreateUploadTokenAsync(cancellationToken);
            var startInfo = CreateStartInfo(settings, token);
            _managedProcess = Process.Start(startInfo)
                ?? throw new InvalidOperationException("无法启动 MidnightVault 接收服务。");
            _managedProcess.BeginOutputReadLine();
            _managedProcess.BeginErrorReadLine();

            for (var attempt = 0; attempt < 30; attempt++)
            {
                await Task.Delay(500, cancellationToken);
                if (_managedProcess.HasExited)
                {
                    throw new InvalidOperationException(
                        $"接收服务启动后立即退出，退出码 {_managedProcess.ExitCode}。");
                }
                current = await apiClient.GetHealthAsync(cancellationToken);
                if (current is not null) return ValidateRunningReceiver(current, settings);
            }
            throw new TimeoutException("接收服务未能在 15 秒内就绪。");
        }
        finally
        {
            _startLock.Release();
        }
    }

    public async Task<ReceiverHealth?> RestartAsync(CancellationToken cancellationToken = default)
    {
        if (_managedProcess is { HasExited: false })
        {
            _managedProcess.Kill(entireProcessTree: true);
            await _managedProcess.WaitForExitAsync(cancellationToken);
        }
        _managedProcess?.Dispose();
        _managedProcess = null;
        return await EnsureRunningAsync(cancellationToken);
    }

    public static IReadOnlyList<string> GetPairingUrls(int port)
    {
        var addresses = NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up &&
                adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
            .Select(address => address.Address)
            .Where(address => address.AddressFamily == AddressFamily.InterNetwork &&
                !IPAddress.IsLoopback(address))
            .Distinct()
            .Select(address => $"http://{address}:{port}")
            .ToList();
        return addresses;
    }

    private static ProcessStartInfo CreateStartInfo(DesktopSettings settings, string token)
    {
        var repositoryRoot = FindRepositoryRoot();
        var receiverExecutable = repositoryRoot is null
            ? ResolvePackagedReceiver()
            : Path.Combine(
                repositoryRoot,
                "windows",
                "src",
                "MidnightVault.Receiver",
                "bin",
                "Debug",
                "net10.0",
                "MidnightVault.Receiver.exe");

        ProcessStartInfo startInfo;
        if (File.Exists(receiverExecutable))
        {
            startInfo = new ProcessStartInfo(receiverExecutable);
        }
        else if (repositoryRoot is not null)
        {
            var project = Path.Combine(
                repositoryRoot,
                "windows",
                "src",
                "MidnightVault.Receiver",
                "MidnightVault.Receiver.csproj");
            startInfo = new ProcessStartInfo(
                "dotnet",
                $"run --project \"{project}\" --no-build --no-launch-profile");
        }
        else
        {
            throw new FileNotFoundException("找不到 MidnightVault.Receiver 接收服务。");
        }

        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.Environment["ASPNETCORE_URLS"] = $"http://0.0.0.0:{settings.ReceiverPort}";
        startInfo.Environment["Receiver__RootPath"] = settings.BackupRootPath;
        startInfo.Environment["Receiver__UploadToken"] = token;
        startInfo.Environment["Receiver__MinimumFreeSpaceBytes"] = (50L * 1024 * 1024 * 1024).ToString();
        if (!string.IsNullOrWhiteSpace(settings.ExpectedVolumeId))
        {
            startInfo.Environment["Receiver__ExpectedVolumeId"] = settings.ExpectedVolumeId;
        }
        return startInfo;
    }

    private static ReceiverHealth ValidateRunningReceiver(
        ReceiverHealth health,
        DesktopSettings settings)
    {
        var configuredRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(settings.BackupRootPath!));
        var actualRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(health.Storage.RootPath));
        if (!string.Equals(configuredRoot, actualRoot, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(settings.ExpectedVolumeId) &&
             !string.Equals(
                 settings.ExpectedVolumeId,
                 health.Storage.VolumeId,
                 StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"端口 {settings.ReceiverPort} 正由另一个备份目录的 Receiver 使用；" +
                "请停止旧服务或恢复对应设置。");
        }
        return health;
    }

    private static string ResolvePackagedReceiver()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Receiver", "MidnightVault.Receiver.exe"),
            Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "Receiver",
                "MidnightVault.Receiver.exe"))
        };
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    private static string? FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MidnightVault.slnx")))
            {
                return directory.FullName;
            }
        }
        return null;
    }
}
