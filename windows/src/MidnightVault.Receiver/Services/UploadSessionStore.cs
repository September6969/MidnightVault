using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MidnightVault.Core.Models;
using MidnightVault.Core.Storage;
using MidnightVault.Receiver.Contracts;

namespace MidnightVault.Receiver.Services;

public sealed class UploadSessionStore(
    IOptions<ReceiverOptions> options,
    BackupPathPolicy pathPolicy,
    ILogger<UploadSessionStore> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ReceiverOptions _options = options.Value;

    public async Task<StartUploadResponse> StartAsync(UploadDescriptor descriptor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        _ = pathPolicy.ResolveDestination(_options.RootPath, descriptor);

        var uploadId = Guid.NewGuid().ToString("N");
        var sessionDirectory = GetSessionDirectory(uploadId);
        Directory.CreateDirectory(sessionDirectory);

        var manifest = new UploadManifest(descriptor, _options.ChunkSizeBytes);
        await using var stream = File.Create(Path.Combine(sessionDirectory, "session.json"));
        await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);

        var chunkCount = descriptor.Size == 0
            ? 0
            : checked((int)Math.Ceiling((double)descriptor.Size / _options.ChunkSizeBytes));
        return new StartUploadResponse(uploadId, _options.ChunkSizeBytes, chunkCount);
    }

    public async Task PutChunkAsync(
        string uploadId,
        int index,
        Stream content,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        var manifest = await ReadManifestAsync(uploadId, cancellationToken);
        var expectedLength = GetExpectedChunkLength(manifest, index);
        var sessionDirectory = GetSessionDirectory(uploadId);
        var destination = Path.Combine(sessionDirectory, $"{index:D8}.chunk");
        var temporary = $"{destination}.uploading";

        try
        {
            await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true))
            {
                await content.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
                if (output.Length != expectedLength)
                {
                    throw new InvalidDataException($"分块长度应为 {expectedLength} 字节，实际为 {output.Length} 字节。 ");
                }
            }

            var actualSha256 = await FileIntegrity.ComputeSha256Async(temporary, cancellationToken);
            if (!actualSha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("分块 SHA-256 校验失败。 ");
            }

            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public async Task<CompleteUploadResponse> CompleteAsync(string uploadId, CancellationToken cancellationToken)
    {
        var manifest = await ReadManifestAsync(uploadId, cancellationToken);
        var destination = pathPolicy.ResolveDestination(_options.RootPath, manifest.File);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        if (File.Exists(destination))
        {
            var existingHash = await FileIntegrity.ComputeSha256Async(destination, cancellationToken);
            if (existingHash.Equals(manifest.File.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.SetLastWriteTimeUtc(destination, manifest.File.OriginalModifiedAt.UtcDateTime);
                return CreateCompleteResponse(destination, manifest.File);
            }

            destination = AddHashSuffix(destination, manifest.File.Sha256[..8]);
        }

        var temporary = $"{destination}.assembling";
        try
        {
            await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true))
            {
                var chunkCount = manifest.File.Size == 0
                    ? 0
                    : checked((int)Math.Ceiling((double)manifest.File.Size / manifest.ChunkSizeBytes));

                for (var index = 0; index < chunkCount; index++)
                {
                    var chunkPath = Path.Combine(GetSessionDirectory(uploadId), $"{index:D8}.chunk");
                    if (!File.Exists(chunkPath))
                    {
                        throw new InvalidDataException($"缺少第 {index} 个分块。 ");
                    }

                    await using var input = File.OpenRead(chunkPath);
                    await input.CopyToAsync(output, cancellationToken);
                }

                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
                if (output.Length != manifest.File.Size)
                {
                    throw new InvalidDataException("合并后的文件大小与清单不一致。 ");
                }
            }

            var fullHash = await FileIntegrity.ComputeSha256Async(temporary, cancellationToken);
            if (!fullHash.Equals(manifest.File.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("整文件 SHA-256 校验失败。 ");
            }

            File.Move(temporary, destination);
        }
        finally
        {
            File.Delete(temporary);
        }

        File.SetLastWriteTimeUtc(destination, manifest.File.OriginalModifiedAt.UtcDateTime);
        logger.LogInformation(
            "上传 {UploadId} 已完成：设备 {DeviceId}，{Size} 字节，SHA-256 {HashPrefix}",
            uploadId,
            manifest.File.DeviceId,
            manifest.File.Size,
            manifest.File.Sha256[..8].ToLowerInvariant());
        return CreateCompleteResponse(destination, manifest.File);
    }

    private string GetSessionDirectory(string uploadId)
    {
        if (uploadId.Length != 32 || !uploadId.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("上传任务 ID 无效。", nameof(uploadId));
        }

        var stagingRoot = Path.GetFullPath(Path.Combine(_options.RootPath, ".midnight-vault", "staging"));
        var sessionDirectory = Path.GetFullPath(Path.Combine(stagingRoot, uploadId));
        if (!sessionDirectory.StartsWith(stagingRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("上传任务路径无效。 ");
        }

        return sessionDirectory;
    }

    private async Task<UploadManifest> ReadManifestAsync(string uploadId, CancellationToken cancellationToken)
    {
        var path = Path.Combine(GetSessionDirectory(uploadId), "session.json");
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<UploadManifest>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("上传任务清单损坏。 ");
    }

    private static long GetExpectedChunkLength(UploadManifest manifest, int index)
    {
        var chunkCount = manifest.File.Size == 0
            ? 0
            : checked((int)Math.Ceiling((double)manifest.File.Size / manifest.ChunkSizeBytes));
        if (index < 0 || index >= chunkCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "分块序号越界。 ");
        }

        var offset = (long)index * manifest.ChunkSizeBytes;
        return Math.Min(manifest.ChunkSizeBytes, manifest.File.Size - offset);
    }

    private CompleteUploadResponse CreateCompleteResponse(string destination, UploadDescriptor descriptor)
    {
        var relativePath = Path.GetRelativePath(_options.RootPath, destination).Replace('\\', '/');
        return new CompleteUploadResponse(relativePath, descriptor.Sha256.ToLowerInvariant(), descriptor.Size);
    }

    private static string AddHashSuffix(string path, string hashPrefix)
    {
        var directory = Path.GetDirectoryName(path)!;
        var extension = Path.GetExtension(path);
        var stem = Path.GetFileNameWithoutExtension(path);
        return Path.Combine(directory, $"{stem}~{hashPrefix.ToLowerInvariant()}{extension}");
    }

    private sealed record UploadManifest(UploadDescriptor File, int ChunkSizeBytes);
}
