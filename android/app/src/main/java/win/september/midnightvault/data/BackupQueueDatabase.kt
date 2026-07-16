package win.september.midnightvault.data

import android.content.ContentValues
import android.content.Context
import android.database.Cursor
import android.database.sqlite.SQLiteDatabase
import android.database.sqlite.SQLiteOpenHelper

class BackupQueueDatabase(context: Context) : SQLiteOpenHelper(
    context.applicationContext,
    DatabaseName,
    null,
    DatabaseVersion
) {
    override fun onCreate(db: SQLiteDatabase) {
        db.execSQL(
            """
            CREATE TABLE backup_queue (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                source_uri TEXT NOT NULL UNIQUE,
                category TEXT NOT NULL,
                relative_path TEXT NOT NULL,
                size INTEGER NOT NULL,
                modified_at INTEGER NOT NULL,
                sha256 TEXT,
                backup_date TEXT NOT NULL,
                is_initial INTEGER NOT NULL,
                state TEXT NOT NULL,
                upload_id TEXT,
                chunk_size INTEGER,
                next_chunk INTEGER NOT NULL DEFAULT 0,
                error TEXT,
                updated_at INTEGER NOT NULL
            )
            """.trimIndent()
        )
        db.execSQL("CREATE INDEX idx_backup_queue_state ON backup_queue(state, modified_at)")
    }

    override fun onUpgrade(db: SQLiteDatabase, oldVersion: Int, newVersion: Int) {
        // 首个数据库版本暂无迁移路径；后续版本必须在此进行无损迁移。
    }

    @Synchronized
    fun upsertScanned(files: List<ScannedFile>) {
        val db = writableDatabase
        db.beginTransaction()
        try {
            files.forEach { file -> upsertScanned(db, file) }
            db.setTransactionSuccessful()
        } finally {
            db.endTransaction()
        }
    }

    private fun upsertScanned(db: SQLiteDatabase, file: ScannedFile) {
        val existing = db.query(
            Table,
            arrayOf("id", "category", "relative_path", "size", "modified_at"),
            "source_uri = ?",
            arrayOf(file.sourceUri),
            null,
            null,
            null,
            "1"
        ).use { cursor ->
            if (!cursor.moveToFirst()) null else ExistingFile(
                id = cursor.getLong(0),
                category = cursor.getString(1),
                relativePath = cursor.getString(2),
                size = cursor.getLong(3),
                modifiedAtMillis = cursor.getLong(4)
            )
        }

        val now = System.currentTimeMillis()
        if (existing == null) {
            db.insertOrThrow(Table, null, ContentValues().apply {
                put("source_uri", file.sourceUri)
                put("category", file.category.name)
                put("relative_path", file.relativePath)
                put("size", file.size)
                put("modified_at", file.modifiedAtMillis)
                put("backup_date", file.backupDate)
                put("is_initial", if (file.isInitial) 1 else 0)
                put("state", BackupState.PENDING.name)
                put("next_chunk", 0)
                put("updated_at", now)
            })
            return
        }

        val changed = existing.category != file.category.name ||
            existing.relativePath != file.relativePath ||
            existing.size != file.size ||
            existing.modifiedAtMillis != file.modifiedAtMillis
        if (!changed) return

        db.update(Table, ContentValues().apply {
            put("category", file.category.name)
            put("relative_path", file.relativePath)
            put("size", file.size)
            put("modified_at", file.modifiedAtMillis)
            putNull("sha256")
            put("backup_date", file.backupDate)
            put("is_initial", if (file.isInitial) 1 else 0)
            put("state", BackupState.PENDING.name)
            putNull("upload_id")
            putNull("chunk_size")
            put("next_chunk", 0)
            putNull("error")
            put("updated_at", now)
        }, "id = ?", arrayOf(existing.id.toString()))
    }

    @Synchronized
    fun nextWorkItem(): BackupItem? = readableDatabase.query(
        Table,
        AllColumns,
        "state IN (?, ?)",
        arrayOf(BackupState.PENDING.name, BackupState.UPLOADING.name),
        null,
        null,
        "modified_at ASC, id ASC",
        "1"
    ).use { cursor -> if (cursor.moveToFirst()) cursor.toBackupItem() else null }

    @Synchronized
    fun markHash(id: Long, sha256: String) {
        update(id, ContentValues().apply {
            put("sha256", sha256)
            put("updated_at", System.currentTimeMillis())
        })
    }

    @Synchronized
    fun markUploadStarted(id: Long, uploadId: String, chunkSize: Int) {
        update(id, ContentValues().apply {
            put("state", BackupState.UPLOADING.name)
            put("upload_id", uploadId)
            put("chunk_size", chunkSize)
            put("next_chunk", 0)
            putNull("error")
            put("updated_at", System.currentTimeMillis())
        })
    }

    @Synchronized
    fun markChunkUploaded(id: Long, nextChunk: Int) {
        update(id, ContentValues().apply {
            put("state", BackupState.UPLOADING.name)
            put("next_chunk", nextChunk)
            putNull("error")
            put("updated_at", System.currentTimeMillis())
        })
    }

    @Synchronized
    fun resetUpload(id: Long) {
        update(id, ContentValues().apply {
            put("state", BackupState.PENDING.name)
            putNull("upload_id")
            putNull("chunk_size")
            put("next_chunk", 0)
            putNull("error")
            put("updated_at", System.currentTimeMillis())
        })
    }

    @Synchronized
    fun markCompleted(id: Long) {
        update(id, ContentValues().apply {
            put("state", BackupState.COMPLETED.name)
            putNull("error")
            put("updated_at", System.currentTimeMillis())
        })
    }

    @Synchronized
    fun markFailed(id: Long, message: String) {
        update(id, ContentValues().apply {
            put("state", BackupState.FAILED.name)
            put("error", message.take(500))
            put("updated_at", System.currentTimeMillis())
        })
    }

    @Synchronized
    fun recordRetryableError(id: Long, message: String) {
        update(id, ContentValues().apply {
            put("error", message.take(500))
            put("updated_at", System.currentTimeMillis())
        })
    }

    @Synchronized
    fun retryFailures() {
        writableDatabase.update(Table, ContentValues().apply {
            put("state", BackupState.PENDING.name)
            putNull("error")
            put("updated_at", System.currentTimeMillis())
        }, "state = ?", arrayOf(BackupState.FAILED.name))
    }

    @Synchronized
    fun summary(): QueueSummary {
        val counts = mutableMapOf<BackupState, Int>()
        readableDatabase.rawQuery(
            "SELECT state, COUNT(*) FROM $Table GROUP BY state",
            null
        ).use { cursor ->
            while (cursor.moveToNext()) {
                counts[BackupState.valueOf(cursor.getString(0))] = cursor.getInt(1)
            }
        }
        return QueueSummary(
            pending = counts[BackupState.PENDING] ?: 0,
            uploading = counts[BackupState.UPLOADING] ?: 0,
            completed = counts[BackupState.COMPLETED] ?: 0,
            failed = counts[BackupState.FAILED] ?: 0
        )
    }

    @Synchronized
    fun hasIncompleteInitialItems(): Boolean = readableDatabase.rawQuery(
        "SELECT 1 FROM $Table WHERE is_initial = 1 AND state != ? LIMIT 1",
        arrayOf(BackupState.COMPLETED.name)
    ).use { it.moveToFirst() }

    private fun update(id: Long, values: ContentValues) {
        writableDatabase.update(Table, values, "id = ?", arrayOf(id.toString()))
    }

    private fun Cursor.toBackupItem() = BackupItem(
        id = getLong(getColumnIndexOrThrow("id")),
        sourceUri = getString(getColumnIndexOrThrow("source_uri")),
        category = BackupCategory.valueOf(getString(getColumnIndexOrThrow("category"))),
        relativePath = getString(getColumnIndexOrThrow("relative_path")),
        size = getLong(getColumnIndexOrThrow("size")),
        modifiedAtMillis = getLong(getColumnIndexOrThrow("modified_at")),
        sha256 = getStringOrNull("sha256"),
        backupDate = getString(getColumnIndexOrThrow("backup_date")),
        isInitial = getInt(getColumnIndexOrThrow("is_initial")) == 1,
        state = BackupState.valueOf(getString(getColumnIndexOrThrow("state"))),
        uploadId = getStringOrNull("upload_id"),
        chunkSize = getIntOrNull("chunk_size"),
        nextChunk = getInt(getColumnIndexOrThrow("next_chunk")),
        error = getStringOrNull("error")
    )

    private fun Cursor.getStringOrNull(column: String): String? {
        val index = getColumnIndexOrThrow(column)
        return if (isNull(index)) null else getString(index)
    }

    private fun Cursor.getIntOrNull(column: String): Int? {
        val index = getColumnIndexOrThrow(column)
        return if (isNull(index)) null else getInt(index)
    }

    private data class ExistingFile(
        val id: Long,
        val category: String,
        val relativePath: String,
        val size: Long,
        val modifiedAtMillis: Long
    )

    private companion object {
        const val DatabaseName = "midnightvault.db"
        const val DatabaseVersion = 1
        const val Table = "backup_queue"
        val AllColumns = arrayOf(
            "id",
            "source_uri",
            "category",
            "relative_path",
            "size",
            "modified_at",
            "sha256",
            "backup_date",
            "is_initial",
            "state",
            "upload_id",
            "chunk_size",
            "next_chunk",
            "error"
        )
    }
}
