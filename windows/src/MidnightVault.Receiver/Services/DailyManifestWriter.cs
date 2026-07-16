using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MidnightVault.Core.Models;
using MidnightVault.Receiver.Contracts;

namespace MidnightVault.Receiver.Services;

public sealed class DailyManifestWriter(IOptions<ReceiverOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ReceiverOptions _options = options.Value;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public async Task AppendAsync(
        string uploadId,
        UploadDescriptor descriptor,
        CompleteUploadResponse response,
        CancellationToken cancellationToken)
    {
        var destination = Path.GetFullPath(Path.Combine(_options.RootPath, response.RelativeStoredPath));
        var categoryDirectory = Path.GetDirectoryName(destination)
            ?? throw new InvalidDataException("完成文件路径无效。");
        var dayDirectory = FindDayDirectory(categoryDirectory, descriptor.BackupDate);
        var manifestPath = Path.Combine(dayDirectory, ".midnight-vault-manifest.jsonl");
        var line = JsonSerializer.Serialize(new
        {
            uploadId,
            descriptor.DeviceId,
            descriptor.DeviceName,
            descriptor.BackupDate,
            category = descriptor.Category.ToString(),
            descriptor.RelativePath,
            response.RelativeStoredPath,
            descriptor.Size,
            sha256 = descriptor.Sha256.ToLowerInvariant(),
            descriptor.OriginalModifiedAt,
            descriptor.IsInitialBackup,
            completedAt = DateTimeOffset.UtcNow
        }, JsonOptions) + Environment.NewLine;

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var stream = new FileStream(
                manifestPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
            var bytes = Encoding.UTF8.GetBytes(line);
            await stream.WriteAsync(bytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Flush(flushToDisk: true);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static string FindDayDirectory(string directory, DateOnly backupDate)
    {
        var dateText = backupDate.ToString("O");
        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
        {
            if (current.Name == dateText || current.Name.EndsWith(dateText, StringComparison.Ordinal))
            {
                return current.FullName;
            }
        }
        throw new InvalidDataException("无法定位每日备份目录。");
    }
}
