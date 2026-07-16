using System.Text.Json;
using Microsoft.Extensions.Options;

namespace MidnightVault.Receiver.Services;

public sealed record StorageVolumeSnapshot(
    string RootPath,
    string VolumeId,
    string DriveName,
    long AvailableFreeSpace,
    long TotalSize,
    long MinimumFreeSpaceBytes);

public sealed class StorageVolumeService(IOptions<ReceiverOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ReceiverOptions _options = options.Value;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private StorageVolumeSnapshot? _snapshot;

    public async Task<StorageVolumeSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        var root = Path.GetFullPath(_options.RootPath);
        var driveRoot = Path.GetPathRoot(root)
            ?? throw new DirectoryNotFoundException("无法确定备份根目录所在卷。");
        var drive = new DriveInfo(driveRoot);
        if (!drive.IsReady)
        {
            throw new IOException("备份硬盘当前未连接或尚未就绪。");
        }

        var current = _snapshot! with
        {
            AvailableFreeSpace = drive.AvailableFreeSpace,
            TotalSize = drive.TotalSize
        };
        _snapshot = current;
        return current;
    }

    public async Task EnsureCapacityAsync(long incomingBytes, CancellationToken cancellationToken)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken);
        var required = checked(snapshot.MinimumFreeSpaceBytes + Math.Max(0, incomingBytes));
        if (snapshot.AvailableFreeSpace < required)
        {
            throw new IOException(
                $"备份硬盘空间不足：可用 {snapshot.AvailableFreeSpace} 字节，需要至少 {required} 字节。");
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_snapshot is not null) return;
        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_snapshot is not null) return;
            var root = Path.GetFullPath(_options.RootPath);
            var driveRoot = Path.GetPathRoot(root)
                ?? throw new DirectoryNotFoundException("无法确定备份根目录所在卷。");
            var drive = new DriveInfo(driveRoot);
            if (!drive.IsReady)
            {
                throw new IOException("备份硬盘当前未连接或尚未就绪。");
            }

            var systemRoot = Path.Combine(root, ".midnight-vault");
            var markerPath = Path.Combine(systemRoot, "volume.json");
            VolumeMarker marker;
            if (File.Exists(markerPath))
            {
                await using var input = File.OpenRead(markerPath);
                marker = await JsonSerializer.DeserializeAsync<VolumeMarker>(input, JsonOptions, cancellationToken)
                    ?? throw new InvalidDataException("硬盘身份文件已损坏。");
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(_options.ExpectedVolumeId))
                {
                    throw new InvalidDataException(
                        "指定备份盘上缺少硬盘身份文件，已拒绝创建目录或写入数据。");
                }
                Directory.CreateDirectory(systemRoot);
                marker = new VolumeMarker(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow);
                var temporary = markerPath + ".tmp";
                await using (var output = new FileStream(
                    temporary,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(output, marker, JsonOptions, cancellationToken);
                    await output.FlushAsync(cancellationToken);
                    output.Flush(flushToDisk: true);
                }
                File.Move(temporary, markerPath, overwrite: false);
            }

            if (!string.IsNullOrWhiteSpace(_options.ExpectedVolumeId) &&
                !string.Equals(_options.ExpectedVolumeId, marker.VolumeId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("当前硬盘身份与 Receiver:ExpectedVolumeId 不一致，已拒绝写入。");
            }

            var minimumFreeSpaceBytes = Math.Max(
                _options.MinimumFreeSpaceBytes,
                checked((long)Math.Ceiling(drive.TotalSize * _options.MinimumFreeSpaceRatio)));
            _snapshot = new StorageVolumeSnapshot(
                root,
                marker.VolumeId,
                drive.Name,
                drive.AvailableFreeSpace,
                drive.TotalSize,
                minimumFreeSpaceBytes);
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    private sealed record VolumeMarker(string VolumeId, DateTimeOffset CreatedAt);
}
