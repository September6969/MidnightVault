using Microsoft.Extensions.Options;
using MidnightVault.Receiver.Services;

namespace MidnightVault.Receiver.Tests;

public sealed class StorageVolumeServiceTests
{
    [Fact]
    public async Task GetSnapshotAsync_绑定卷缺少身份文件时不会创建目录()
    {
        var parent = Path.Combine(Path.GetTempPath(), $"midnight-vault-volume-{Guid.NewGuid():N}");
        var root = Path.Combine(parent, "不存在的备份目录");
        var service = new StorageVolumeService(Options.Create(new ReceiverOptions
        {
            RootPath = root,
            ExpectedVolumeId = Guid.NewGuid().ToString("N"),
            MinimumFreeSpaceBytes = 0,
            MinimumFreeSpaceRatio = 0
        }));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.GetSnapshotAsync(CancellationToken.None));

        Assert.Contains("缺少硬盘身份文件", exception.Message);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task GetSnapshotAsync_空间阈值取固定值与百分比的较大者()
    {
        var root = Path.Combine(Path.GetTempPath(), $"midnight-vault-volume-{Guid.NewGuid():N}");
        var service = new StorageVolumeService(Options.Create(new ReceiverOptions
        {
            RootPath = root,
            MinimumFreeSpaceBytes = 1,
            MinimumFreeSpaceRatio = 0.10
        }));

        var snapshot = await service.GetSnapshotAsync(CancellationToken.None);

        Assert.Equal((long)Math.Ceiling(snapshot.TotalSize * 0.10), snapshot.MinimumFreeSpaceBytes);
    }
}
