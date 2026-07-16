using Microsoft.Extensions.Options;

namespace MidnightVault.Receiver.Services;

public sealed class StagingCleanupService(
    IOptions<ReceiverOptions> options,
    ILogger<StagingCleanupService> logger) : BackgroundService
{
    private readonly ReceiverOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        do
        {
            Cleanup();
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private void Cleanup()
    {
        var stagingRoot = Path.GetFullPath(Path.Combine(
            _options.RootPath,
            ".midnight-vault",
            "staging"));
        if (!Directory.Exists(stagingRoot)) return;

        foreach (var directory in Directory.EnumerateDirectories(stagingRoot))
        {
            try
            {
                var candidate = Path.GetFullPath(directory);
                if (!candidate.StartsWith(
                    stagingRoot + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                var name = Path.GetFileName(candidate);
                if (name.Length != 32 || !name.All(Uri.IsHexDigit)) continue;

                var completion = Path.Combine(candidate, "completion.json");
                var manifest = Path.Combine(candidate, "session.json");
                var timestampPath = File.Exists(completion) ? completion : manifest;
                if (!File.Exists(timestampPath)) continue;
                var retentionDays = File.Exists(completion)
                    ? _options.CompletedSessionRetentionDays
                    : _options.IncompleteSessionRetentionDays;
                if (File.GetLastWriteTimeUtc(timestampPath) > DateTime.UtcNow.AddDays(-retentionDays))
                {
                    continue;
                }

                Directory.Delete(candidate, recursive: true);
                logger.LogInformation("已清理过期上传会话 {UploadId}", name);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "清理上传暂存目录失败：{Directory}", directory);
            }
        }
    }
}
