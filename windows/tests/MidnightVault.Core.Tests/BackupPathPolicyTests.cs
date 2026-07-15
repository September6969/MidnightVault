using MidnightVault.Core.Models;
using MidnightVault.Core.Storage;

namespace MidnightVault.Core.Tests;

public sealed class BackupPathPolicyTests
{
    private readonly BackupPathPolicy _policy = new();

    [Fact]
    public void ResolveDestination_保留日期类别与原始层级()
    {
        var descriptor = CreateDescriptor("DCIM/Camera/IMG_001.jpg");

        var result = _policy.ResolveDestination("E:\\Backup", descriptor);

        Assert.Equal(
            Path.GetFullPath("E:\\Backup\\手机A\\2026-07-16\\照片\\DCIM\\Camera\\IMG_001.jpg"),
            result);
    }

    [Fact]
    public void ResolveDestination_拒绝路径穿越()
    {
        var descriptor = CreateDescriptor("../../Windows/system.ini");

        Assert.Throws<ArgumentException>(() => _policy.ResolveDestination("E:\\Backup", descriptor));
    }

    [Fact]
    public void ResolveDestination_安全转义Windows非法字符()
    {
        var descriptor = CreateDescriptor("Downloads/课程:第一讲?.pdf");

        var result = _policy.ResolveDestination("E:\\Backup", descriptor);

        Assert.EndsWith("课程_第一讲_.pdf", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveDestination_首次备份使用专用日期目录()
    {
        var descriptor = CreateDescriptor("Documents/a.txt") with { IsInitialBackup = true };

        var result = _policy.ResolveDestination("E:\\Backup", descriptor);

        Assert.Contains("首次备份_2026-07-16", result, StringComparison.Ordinal);
    }

    private static UploadDescriptor CreateDescriptor(string relativePath) => new(
        DeviceId: "device-a",
        DeviceName: "手机A",
        BackupDate: new DateOnly(2026, 7, 16),
        Category: BackupCategory.Photo,
        RelativePath: relativePath,
        Size: 12,
        Sha256: new string('a', 64),
        OriginalModifiedAt: DateTimeOffset.Parse("2026-07-16T00:00:00+08:00"));
}
