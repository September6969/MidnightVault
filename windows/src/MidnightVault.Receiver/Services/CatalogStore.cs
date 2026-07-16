using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using MidnightVault.Core.Models;
using MidnightVault.Receiver.Contracts;

namespace MidnightVault.Receiver.Services;

public sealed class CatalogStore(IOptions<ReceiverOptions> options)
{
    private readonly ReceiverOptions _options = options.Value;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _initialized;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;
        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;
            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await ExecuteNonQueryAsync(connection, "PRAGMA journal_mode=WAL;", cancellationToken);
            await ExecuteNonQueryAsync(connection, "PRAGMA synchronous=FULL;", cancellationToken);
            await ExecuteNonQueryAsync(
                connection,
                """
                CREATE TABLE IF NOT EXISTS library_items (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    device_id TEXT NOT NULL,
                    device_name TEXT NOT NULL,
                    backup_date TEXT NOT NULL,
                    category TEXT NOT NULL,
                    relative_path TEXT NOT NULL,
                    relative_stored_path TEXT NOT NULL UNIQUE,
                    size INTEGER NOT NULL,
                    sha256 TEXT NOT NULL,
                    original_modified_at TEXT NOT NULL,
                    is_initial INTEGER NOT NULL,
                    completed_at TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_library_completed
                    ON library_items(completed_at DESC);
                CREATE INDEX IF NOT EXISTS idx_library_device_date
                    ON library_items(device_id, backup_date DESC);
                CREATE INDEX IF NOT EXISTS idx_library_category
                    ON library_items(category, backup_date DESC);
                CREATE TABLE IF NOT EXISTS paired_devices (
                    device_id TEXT PRIMARY KEY,
                    device_name TEXT NOT NULL,
                    public_key TEXT NOT NULL,
                    paired_at TEXT NOT NULL,
                    last_seen_at TEXT NOT NULL,
                    revoked_at TEXT NULL
                );
                CREATE TABLE IF NOT EXISTS request_nonces (
                    device_id TEXT NOT NULL,
                    nonce TEXT NOT NULL,
                    expires_at TEXT NOT NULL,
                    PRIMARY KEY(device_id, nonce),
                    FOREIGN KEY(device_id) REFERENCES paired_devices(device_id)
                );
                """,
                cancellationToken);
            await EnsureColumnAsync(
                connection,
                "paired_devices",
                "public_key",
                "TEXT NOT NULL DEFAULT ''",
                cancellationToken);
            await EnsureColumnAsync(
                connection,
                "paired_devices",
                "revoked_at",
                "TEXT NULL",
                cancellationToken);
            _initialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async Task<LibraryItemResponse> RecordCompletedAsync(
        UploadDescriptor descriptor,
        string relativeStoredPath,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        var completedAt = DateTimeOffset.UtcNow;
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO library_items (
                device_id, device_name, backup_date, category, relative_path,
                relative_stored_path, size, sha256, original_modified_at,
                is_initial, completed_at)
            VALUES (
                $deviceId, $deviceName, $backupDate, $category, $relativePath,
                $storedPath, $size, $sha256, $modifiedAt, $isInitial, $completedAt)
            ON CONFLICT(relative_stored_path) DO UPDATE SET
                device_name = excluded.device_name,
                size = excluded.size,
                sha256 = excluded.sha256,
                original_modified_at = excluded.original_modified_at,
                completed_at = excluded.completed_at
            RETURNING id;
            """;
        command.Parameters.AddWithValue("$deviceId", descriptor.DeviceId);
        command.Parameters.AddWithValue("$deviceName", descriptor.DeviceName);
        command.Parameters.AddWithValue("$backupDate", descriptor.BackupDate.ToString("O"));
        command.Parameters.AddWithValue("$category", descriptor.Category.ToString());
        command.Parameters.AddWithValue("$relativePath", descriptor.RelativePath);
        command.Parameters.AddWithValue("$storedPath", relativeStoredPath.Replace('\\', '/'));
        command.Parameters.AddWithValue("$size", descriptor.Size);
        command.Parameters.AddWithValue("$sha256", descriptor.Sha256.ToLowerInvariant());
        command.Parameters.AddWithValue("$modifiedAt", descriptor.OriginalModifiedAt.ToString("O"));
        command.Parameters.AddWithValue("$isInitial", descriptor.IsInitialBackup ? 1 : 0);
        command.Parameters.AddWithValue("$completedAt", completedAt.ToString("O"));
        var id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        return ToResponse(id, descriptor, relativeStoredPath, completedAt);
    }

    public async Task<LibraryPageResponse> QueryAsync(
        string? query,
        BackupCategory? category,
        string? deviceId,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        offset = Math.Max(0, offset);
        limit = Math.Clamp(limit, 1, 500);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var where = new List<string>();
        await using var command = connection.CreateCommand();
        if (!string.IsNullOrWhiteSpace(query))
        {
            where.Add("(relative_path LIKE $query OR device_name LIKE $query)");
            command.Parameters.AddWithValue("$query", $"%{query.Trim()}%");
        }
        if (category is not null)
        {
            where.Add("category = $category");
            command.Parameters.AddWithValue("$category", category.Value.ToString());
        }
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            where.Add("device_id = $deviceId");
            command.Parameters.AddWithValue("$deviceId", deviceId.Trim());
        }

        var whereSql = where.Count == 0 ? "" : "WHERE " + string.Join(" AND ", where);
        command.CommandText =
            $"""
            SELECT id, device_id, device_name, backup_date, category, relative_path,
                   relative_stored_path, size, sha256, original_modified_at,
                   is_initial, completed_at,
                   COUNT(*) OVER() AS total_count
            FROM library_items
            {whereSql}
            ORDER BY completed_at DESC, id DESC
            LIMIT $limit OFFSET $offset;
            """;
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);

        var items = new List<LibraryItemResponse>();
        long total = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            total = reader.GetInt64(12);
            items.Add(ReadItem(reader));
        }
        return new LibraryPageResponse(items, offset, limit, total);
    }

    public async Task<LibraryItemResponse?> GetAsync(long id, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, device_id, device_name, backup_date, category, relative_path,
                   relative_stored_path, size, sha256, original_modified_at,
                   is_initial, completed_at
            FROM library_items WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadItem(reader) : null;
    }

    public async Task<LibrarySummaryResponse> GetSummaryAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*), COALESCE(SUM(size), 0), COUNT(DISTINCT device_id), MAX(completed_at)
            FROM library_items;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new LibrarySummaryResponse(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt32(2),
            reader.IsDBNull(3) ? null : DateTimeOffset.Parse(reader.GetString(3)));
    }

    public async Task RegisterDeviceAsync(
        string deviceId,
        string deviceName,
        string publicKey,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow.ToString("O");
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO paired_devices (device_id, device_name, public_key, paired_at, last_seen_at)
            VALUES ($deviceId, $deviceName, $publicKey, $now, $now)
            ON CONFLICT(device_id) DO UPDATE SET
                device_name = excluded.device_name,
                public_key = excluded.public_key,
                last_seen_at = excluded.last_seen_at,
                revoked_at = NULL;
            """;
        command.Parameters.AddWithValue("$deviceId", deviceId);
        command.Parameters.AddWithValue("$deviceName", deviceName);
        command.Parameters.AddWithValue("$publicKey", publicKey);
        command.Parameters.AddWithValue("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<string?> GetDevicePublicKeyAsync(
        string deviceId,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT public_key FROM paired_devices WHERE device_id = $deviceId AND revoked_at IS NULL;";
        command.Parameters.AddWithValue("$deviceId", deviceId);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    public async Task<bool> TryUseNonceAsync(
        string deviceId,
        string nonce,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var cleanup = connection.CreateCommand())
        {
            cleanup.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
            cleanup.CommandText = "DELETE FROM request_nonces WHERE expires_at <= $now;";
            cleanup.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            await cleanup.ExecuteNonQueryAsync(cancellationToken);
        }
        try
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
            insert.CommandText =
                "INSERT INTO request_nonces (device_id, nonce, expires_at) VALUES ($deviceId, $nonce, $expiresAt);";
            insert.Parameters.AddWithValue("$deviceId", deviceId);
            insert.Parameters.AddWithValue("$nonce", nonce);
            insert.Parameters.AddWithValue("$expiresAt", expiresAt.ToString("O"));
            await insert.ExecuteNonQueryAsync(cancellationToken);
            await using var touch = connection.CreateCommand();
            touch.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
            touch.CommandText =
                "UPDATE paired_devices SET last_seen_at = $now WHERE device_id = $deviceId;";
            touch.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            touch.Parameters.AddWithValue("$deviceId", deviceId);
            await touch.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (Microsoft.Data.Sqlite.SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<PairedDeviceResponse>> GetDevicesAsync(
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT device_id, device_name, paired_at, last_seen_at
            FROM paired_devices
            WHERE revoked_at IS NULL
            ORDER BY paired_at;
            """;
        var devices = new List<PairedDeviceResponse>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            devices.Add(new PairedDeviceResponse(
                reader.GetString(0),
                reader.GetString(1),
                DateTimeOffset.Parse(reader.GetString(2)),
                DateTimeOffset.Parse(reader.GetString(3))));
        }
        return devices;
    }

    public async Task RevokeDeviceAsync(string deviceId, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var revoke = connection.CreateCommand())
        {
            revoke.Transaction = (SqliteTransaction)transaction;
            revoke.CommandText =
                "UPDATE paired_devices SET revoked_at = $now WHERE device_id = $deviceId;";
            revoke.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            revoke.Parameters.AddWithValue("$deviceId", deviceId);
            await revoke.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var clearNonces = connection.CreateCommand())
        {
            clearNonces.Transaction = (SqliteTransaction)transaction;
            clearNonces.CommandText = "DELETE FROM request_nonces WHERE device_id = $deviceId;";
            clearNonces.Parameters.AddWithValue("$deviceId", deviceId);
            await clearNonces.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public string ResolveStoredPath(LibraryItemResponse item)
    {
        var root = Path.GetFullPath(_options.RootPath);
        var candidate = Path.GetFullPath(Path.Combine(root, item.RelativeStoredPath));
        if (!candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("索引中的文件路径越过了备份根目录。");
        }
        return candidate;
    }

    private string DatabasePath => Path.Combine(
        Path.GetFullPath(_options.RootPath),
        ".midnight-vault",
        "catalog.db");

    private SqliteConnection CreateConnection() => new(new SqliteConnectionStringBuilder
    {
        DataSource = DatabasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Shared,
        Pooling = true
    }.ToString());

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureColumnAsync(
        SqliteConnection connection,
        string table,
        string column,
        string declaration,
        CancellationToken cancellationToken)
    {
        await using var query = connection.CreateCommand();
        query.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return;
        }
        await reader.DisposeAsync();
        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {declaration};";
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }

    private static LibraryItemResponse ReadItem(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetString(1),
        reader.GetString(2),
        DateOnly.Parse(reader.GetString(3)),
        Enum.Parse<BackupCategory>(reader.GetString(4)),
        reader.GetString(5),
        reader.GetString(6),
        reader.GetInt64(7),
        reader.GetString(8),
        DateTimeOffset.Parse(reader.GetString(9)),
        reader.GetInt32(10) == 1,
        DateTimeOffset.Parse(reader.GetString(11)));

    private static LibraryItemResponse ToResponse(
        long id,
        UploadDescriptor descriptor,
        string relativeStoredPath,
        DateTimeOffset completedAt) => new(
            id,
            descriptor.DeviceId,
            descriptor.DeviceName,
            descriptor.BackupDate,
            descriptor.Category,
            descriptor.RelativePath,
            relativeStoredPath.Replace('\\', '/'),
            descriptor.Size,
            descriptor.Sha256.ToLowerInvariant(),
            descriptor.OriginalModifiedAt,
            descriptor.IsInitialBackup,
            completedAt);
}
