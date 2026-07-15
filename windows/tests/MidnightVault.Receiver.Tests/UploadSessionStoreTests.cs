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
            ChunkSizeBytes = 4
        });
        var store = new UploadSessionStore(options, new BackupPathPolicy(), NullLogger<UploadSessionStore>.Instance);

        var session = await store.StartAsync(descriptor, CancellationToken.None);
        await PutChunkAsync(store, session.UploadId, 0, content[..4]);
        await PutChunkAsync(store, session.UploadId, 1, content[4..]);

        var completed = await store.CompleteAsync(session.UploadId, CancellationToken.None);
        var destination = Path.Combine(root, completed.RelativeStoredPath.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(File.Exists(destination));
        Assert.Equal("hello", await File.ReadAllTextAsync(destination, CancellationToken.None));
        Assert.Equal(fullHash, completed.Sha256);
    }

    private static async Task PutChunkAsync(UploadSessionStore store, string uploadId, int index, byte[] bytes)
    {
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        await using var stream = new MemoryStream(bytes);
        await store.PutChunkAsync(uploadId, index, stream, hash, CancellationToken.None);
    }
}
