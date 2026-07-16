using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MidnightVault.Core.Models;
using MidnightVault.Core.Storage;
using MidnightVault.Receiver;
using MidnightVault.Receiver.Services;

namespace MidnightVault.Receiver.Tests;

public sealed class UploadSessionStoreTests
{
    [Fact]
    public async Task CompleteAsync_合并分块并校验后写入日期目录()
    {
        var root = Path.Combine(Path.GetTempPath(), $"midnight-vault-{Guid.NewGuid():N}");
        var content = Encoding.UTF8.GetBytes("hello");
        var fullHash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        var descriptor = new UploadDescriptor(
            DeviceId: "device-a",
            DeviceName: "手机A",
            BackupDate: new DateOnly(2026, 7, 16),
            Category: BackupCategory.Document,
            RelativePath: "Documents/hello.txt",
            Size: content.Length,
            Sha256: fullHash,
            OriginalModifiedAt: DateTimeOffset.Parse("2026-07-16T00:00:00+08:00"));
        var options = Options.Create(new ReceiverOptions
        {
            RootPath = root,
            ChunkSizeBytes = 4,
            MinimumFreeSpaceRatio = 0
        });
        var (store, catalog) = CreateStore(options);

        var session = await store.StartAsync(descriptor, CancellationToken.None);
        await PutChunkAsync(store, session.UploadId, 0, content[..4]);
        await PutChunkAsync(store, session.UploadId, 1, content[4..]);

        var completed = await store.CompleteAsync(session.UploadId, CancellationToken.None);
        var destination = Path.Combine(root, completed.RelativeStoredPath.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(File.Exists(destination));
        Assert.Equal("hello", await File.ReadAllTextAsync(destination, CancellationToken.None));
        Assert.Equal(fullHash, completed.Sha256);
        Assert.Equal(descriptor.OriginalModifiedAt.UtcDateTime, File.GetLastWriteTimeUtc(destination));

        var repeated = await store.CompleteAsync(session.UploadId, CancellationToken.None);
        Assert.Equal(completed, repeated);
        var status = await store.GetStatusAsync(session.UploadId, CancellationToken.None);
        Assert.True(status.Completed);
        Assert.Empty(status.ReceivedChunks);

        var summary = await catalog.GetSummaryAsync(CancellationToken.None);
        Assert.Equal(1, summary.FileCount);
        Assert.Equal(content.Length, summary.TotalBytes);
        var page = await catalog.QueryAsync("hello", null, "device-a", 0, 10, CancellationToken.None);
        Assert.Single(page.Items);

        var manifest = Directory.EnumerateFiles(root, ".midnight-vault-manifest.jsonl", SearchOption.AllDirectories).Single();
        Assert.Single(await File.ReadAllLinesAsync(manifest, CancellationToken.None));
    }

    [Fact]
    public async Task PutChunkAsync_分块长度错误时清理临时文件()
    {
        var root = Path.Combine(Path.GetTempPath(), $"midnight-vault-{Guid.NewGuid():N}");
        var descriptor = new UploadDescriptor(
            DeviceId: "device-a",
            DeviceName: "手机A",
            BackupDate: new DateOnly(2026, 7, 16),
            Category: BackupCategory.Document,
            RelativePath: "Documents/hello.txt",
            Size: 5,
            Sha256: new string('a', 64),
            OriginalModifiedAt: DateTimeOffset.Parse("2026-07-16T00:00:00+08:00"));
        var (store, _) = CreateStore(Options.Create(new ReceiverOptions
        {
            RootPath = root,
            ChunkSizeBytes = 4,
            MinimumFreeSpaceBytes = 0,
            MinimumFreeSpaceRatio = 0
        }));
        var session = await store.StartAsync(descriptor, CancellationToken.None);
        var bytes = Encoding.UTF8.GetBytes("bad");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            PutChunkAsync(store, session.UploadId, 0, bytes));

        var temporary = Path.Combine(
            root,
            ".midnight-vault",
            "staging",
            session.UploadId,
            "00000000.chunk.uploading");
        Assert.False(File.Exists(temporary));
    }

    [Fact]
    public async Task GetStatusAsync_服务重建后仍能发现已接收分块()
    {
        var root = Path.Combine(Path.GetTempPath(), $"midnight-vault-{Guid.NewGuid():N}");
        var content = Encoding.UTF8.GetBytes("hello");
        var descriptor = new UploadDescriptor(
            "device-a",
            "手机A",
            new DateOnly(2026, 7, 16),
            BackupCategory.Document,
            "Documents/hello.txt",
            content.Length,
            Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
            DateTimeOffset.Parse("2026-07-16T00:00:00+08:00"));
        var options = Options.Create(new ReceiverOptions
        {
            RootPath = root,
            ChunkSizeBytes = 4,
            MinimumFreeSpaceBytes = 0,
            MinimumFreeSpaceRatio = 0
        });
        var (firstStore, _) = CreateStore(options);
        var session = await firstStore.StartAsync(descriptor, CancellationToken.None);
        await PutChunkAsync(firstStore, session.UploadId, 0, content[..4]);

        var (restartedStore, _) = CreateStore(options);
        var status = await restartedStore.GetStatusAsync(session.UploadId, CancellationToken.None);

        Assert.False(status.Completed);
        Assert.Equal([0], status.ReceivedChunks);
        await PutChunkAsync(restartedStore, session.UploadId, 1, content[4..]);
        var completed = await restartedStore.CompleteAsync(session.UploadId, CancellationToken.None);
        Assert.Equal(content.Length, completed.Size);
    }

    [Fact]
    public async Task CompleteAsync_两台设备可并行写入独立日期目录()
    {
        var root = Path.Combine(Path.GetTempPath(), $"midnight-vault-parallel-{Guid.NewGuid():N}");
        var options = Options.Create(new ReceiverOptions
        {
            RootPath = root,
            ChunkSizeBytes = 4,
            MinimumFreeSpaceBytes = 0,
            MinimumFreeSpaceRatio = 0
        });
        var (store, catalog) = CreateStore(options);

        async Task UploadAsync(string deviceId, string deviceName, byte[] content)
        {
            var descriptor = new UploadDescriptor(
                deviceId,
                deviceName,
                new DateOnly(2026, 7, 16),
                BackupCategory.Photo,
                "DCIM/Camera/parallel.jpg",
                content.Length,
                Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
                DateTimeOffset.Parse("2026-07-16T00:00:00+08:00"));
            var session = await store.StartAsync(descriptor, CancellationToken.None);
            await PutChunkAsync(store, session.UploadId, 0, content);
            await store.CompleteAsync(session.UploadId, CancellationToken.None);
        }

        await Task.WhenAll(
            UploadAsync("device-a", "手机A", [1, 2, 3]),
            UploadAsync("device-b", "手机B", [4, 5, 6]));

        var summary = await catalog.GetSummaryAsync(CancellationToken.None);
        Assert.Equal(2, summary.FileCount);
        Assert.Equal(2, summary.DeviceCount);
        Assert.True(Directory.Exists(Path.Combine(root, "手机A", "2026-07-16")));
        Assert.True(Directory.Exists(Path.Combine(root, "手机B", "2026-07-16")));
    }

    private static async Task PutChunkAsync(UploadSessionStore store, string uploadId, int index, byte[] bytes)
    {
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        await using var stream = new MemoryStream(bytes);
        await store.PutChunkAsync(uploadId, index, stream, hash, CancellationToken.None);
    }

    private static (UploadSessionStore Store, CatalogStore Catalog) CreateStore(
        IOptions<ReceiverOptions> options)
    {
        var storage = new StorageVolumeService(options);
        var catalog = new CatalogStore(options);
        var manifest = new DailyManifestWriter(options);
        var store = new UploadSessionStore(
            options,
            new BackupPathPolicy(),
            storage,
            catalog,
            manifest,
            NullLogger<UploadSessionStore>.Instance);
        return (store, catalog);
    }
}
