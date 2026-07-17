using MidnightVault.Core.Storage;

namespace MidnightVault.Core.Tests;

public sealed class StoragePathGuardTests
{
    [Fact]
    public void IsContainedBy_允许盘符根目录中的文件()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        var candidate = Path.Combine(root, "手机A", "2026-07-18", "照片.jpg");

        Assert.True(StoragePathGuard.IsContainedBy(root, candidate));
    }

    [Fact]
    public void IsContainedBy_允许普通根目录中的文件()
    {
        Assert.True(StoragePathGuard.IsContainedBy(
            "E:\\Backup",
            "E:\\Backup\\手机A\\照片.jpg"));
    }

    [Fact]
    public void IsContainedBy_拒绝旁系目录和路径穿越()
    {
        Assert.False(StoragePathGuard.IsContainedBy(
            "E:\\Backup",
            "E:\\Backup-old\\照片.jpg"));
        Assert.False(StoragePathGuard.IsContainedBy(
            "E:\\Backup",
            "E:\\Windows\\system.ini"));
    }
}
