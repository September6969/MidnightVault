package win.september.midnightvault.scan

import android.content.ContentUris
import android.content.Context
import android.net.Uri
import android.provider.DocumentsContract
import android.provider.MediaStore
import java.time.LocalDate
import java.time.ZoneId
import java.util.ArrayDeque
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import win.september.midnightvault.config.AppSettings
import win.september.midnightvault.data.BackupCategory
import win.september.midnightvault.data.BackupQueueDatabase
import win.september.midnightvault.data.ScannedFile

data class ScanReport(
    val discovered: Int,
    val inaccessibleSources: List<String>
)

class BackupScanner(
    context: Context,
    private val database: BackupQueueDatabase,
    private val settings: AppSettings
) {
    private val appContext = context.applicationContext
    private val resolver = appContext.contentResolver

    suspend fun scan(): ScanReport = withContext(Dispatchers.IO) {
        val snapshot = settings.snapshot()
        val backupDate = LocalDate.now(BeijingZone).toString()
        val discovered = mutableListOf<ScannedFile>()
        val inaccessible = mutableListOf<String>()

        scanMediaCollection(
            MediaStore.Images.Media.EXTERNAL_CONTENT_URI,
            BackupCategory.PHOTO,
            snapshot.initialBackupCompleted,
            backupDate,
            discovered,
            inaccessible
        )
        scanMediaCollection(
            MediaStore.Video.Media.EXTERNAL_CONTENT_URI,
            BackupCategory.VIDEO,
            snapshot.initialBackupCompleted,
            backupDate,
            discovered,
            inaccessible
        )
        snapshot.treeUris.forEach { rawUri ->
            scanTree(
                Uri.parse(rawUri),
                snapshot.initialBackupCompleted,
                backupDate,
                discovered,
                inaccessible
            )
        }

        database.upsertScanned(discovered.distinctBy { it.sourceUri })
        ScanReport(discovered.distinctBy { it.sourceUri }.size, inaccessible)
    }

    private fun scanMediaCollection(
        collection: Uri,
        category: BackupCategory,
        initialBackupCompleted: Boolean,
        backupDate: String,
        destination: MutableList<ScannedFile>,
        inaccessible: MutableList<String>
    ) {
        val projection = arrayOf(
            MediaStore.MediaColumns._ID,
            MediaStore.MediaColumns.DISPLAY_NAME,
            MediaStore.MediaColumns.RELATIVE_PATH,
            MediaStore.MediaColumns.SIZE,
            MediaStore.MediaColumns.DATE_MODIFIED
        )
        try {
            resolver.query(collection, projection, null, null, null)?.use { cursor ->
                val idColumn = cursor.getColumnIndexOrThrow(MediaStore.MediaColumns._ID)
                val nameColumn = cursor.getColumnIndexOrThrow(MediaStore.MediaColumns.DISPLAY_NAME)
                val pathColumn = cursor.getColumnIndexOrThrow(MediaStore.MediaColumns.RELATIVE_PATH)
                val sizeColumn = cursor.getColumnIndexOrThrow(MediaStore.MediaColumns.SIZE)
                val modifiedColumn = cursor.getColumnIndexOrThrow(MediaStore.MediaColumns.DATE_MODIFIED)
                while (cursor.moveToNext()) {
                    val size = cursor.getLong(sizeColumn)
                    if (size < 0) continue
                    val sourceUri = ContentUris.withAppendedId(collection, cursor.getLong(idColumn))
                    val relativePath = safeRelativePath(
                        cursor.getString(pathColumn).orEmpty(),
                        cursor.getString(nameColumn).orEmpty()
                    )
                    if (relativePath.isBlank()) continue
                    destination += ScannedFile(
                        sourceUri = sourceUri.toString(),
                        category = category,
                        relativePath = relativePath,
                        size = size,
                        modifiedAtMillis = cursor.getLong(modifiedColumn) * 1_000L,
                        backupDate = backupDate,
                        isInitial = !initialBackupCompleted
                    )
                }
            }
        } catch (_: SecurityException) {
            inaccessible += category.wireName
        } catch (_: IllegalArgumentException) {
            inaccessible += category.wireName
        }
    }

    private fun scanTree(
        treeUri: Uri,
        initialBackupCompleted: Boolean,
        backupDate: String,
        destination: MutableList<ScannedFile>,
        inaccessible: MutableList<String>
    ) {
        try {
            val rootId = DocumentsContract.getTreeDocumentId(treeUri)
            val rootUri = DocumentsContract.buildDocumentUriUsingTree(treeUri, rootId)
            val rootName = queryDocumentName(rootUri).ifBlank { "已授权目录" }
            val category = categoryForRoot(rootName)
            val prefix = if (category == BackupCategory.CUSTOM) safeSegment(rootName) else ""
            val directories = ArrayDeque<DirectoryEntry>()
            directories.add(DirectoryEntry(rootUri, prefix, 0))

            while (directories.isNotEmpty()) {
                val directory = directories.removeFirst()
                if (directory.depth > MaximumTreeDepth) continue
                val parentId = DocumentsContract.getDocumentId(directory.uri)
                val childrenUri = DocumentsContract.buildChildDocumentsUriUsingTree(treeUri, parentId)
                resolver.query(childrenUri, DocumentProjection, null, null, null)?.use { cursor ->
                    val idColumn = cursor.getColumnIndexOrThrow(DocumentsContract.Document.COLUMN_DOCUMENT_ID)
                    val nameColumn = cursor.getColumnIndexOrThrow(DocumentsContract.Document.COLUMN_DISPLAY_NAME)
                    val mimeColumn = cursor.getColumnIndexOrThrow(DocumentsContract.Document.COLUMN_MIME_TYPE)
                    val sizeColumn = cursor.getColumnIndexOrThrow(DocumentsContract.Document.COLUMN_SIZE)
                    val modifiedColumn = cursor.getColumnIndexOrThrow(DocumentsContract.Document.COLUMN_LAST_MODIFIED)
                    while (cursor.moveToNext()) {
                        val childId = cursor.getString(idColumn)
                        val childUri = DocumentsContract.buildDocumentUriUsingTree(treeUri, childId)
                        val name = safeSegment(cursor.getString(nameColumn).orEmpty())
                        if (name.isBlank()) continue
                        val childPath = listOf(directory.relativePath, name)
                            .filter { it.isNotBlank() }
                            .joinToString("/")
                        if (cursor.getString(mimeColumn) == DocumentsContract.Document.MIME_TYPE_DIR) {
                            directories.add(DirectoryEntry(childUri, childPath, directory.depth + 1))
                            continue
                        }

                        val size = if (cursor.isNull(sizeColumn)) {
                            resolver.openAssetFileDescriptor(childUri, "r")?.use { it.length } ?: -1L
                        } else {
                            cursor.getLong(sizeColumn)
                        }
                        if (size < 0) continue
                        destination += ScannedFile(
                            sourceUri = childUri.toString(),
                            category = category,
                            relativePath = childPath,
                            size = size,
                            modifiedAtMillis = if (cursor.isNull(modifiedColumn)) 0L else cursor.getLong(modifiedColumn),
                            backupDate = backupDate,
                            isInitial = !initialBackupCompleted
                        )
                    }
                }
            }
        } catch (_: SecurityException) {
            inaccessible += treeUri.toString()
        } catch (_: IllegalArgumentException) {
            inaccessible += treeUri.toString()
        }
    }

    private fun queryDocumentName(uri: Uri): String {
        return resolver.query(
            uri,
            arrayOf(DocumentsContract.Document.COLUMN_DISPLAY_NAME),
            null,
            null,
            null
        )?.use { cursor ->
            if (cursor.moveToFirst()) cursor.getString(0).orEmpty() else ""
        }.orEmpty()
    }

    private fun categoryForRoot(name: String): BackupCategory {
        val normalized = name.lowercase()
        return when {
            normalized.contains("download") || normalized.contains("下载") -> BackupCategory.DOWNLOAD
            normalized.contains("document") || normalized.contains("文档") -> BackupCategory.DOCUMENT
            else -> BackupCategory.CUSTOM
        }
    }

    private fun safeRelativePath(directory: String, fileName: String): String {
        val segments = (directory.replace('\\', '/') + "/" + fileName)
            .split('/')
            .map(::safeSegment)
            .filter { it.isNotBlank() }
        return segments.joinToString("/")
    }

    private fun safeSegment(value: String): String {
        val cleaned = value
            .replace(InvalidWindowsCharacters, "_")
            .filterNot { it.isISOControl() }
            .trim()
            .trimEnd('.', ' ')
        return when (cleaned) {
            "", ".", ".." -> "_"
            else -> cleaned.take(MaximumSegmentLength)
        }
    }

    private data class DirectoryEntry(
        val uri: Uri,
        val relativePath: String,
        val depth: Int
    )

    private companion object {
        val BeijingZone: ZoneId = ZoneId.of("Asia/Shanghai")
        val InvalidWindowsCharacters = Regex("[<>:\"/\\\\|?*]")
        const val MaximumTreeDepth = 64
        const val MaximumSegmentLength = 180
        val DocumentProjection = arrayOf(
            DocumentsContract.Document.COLUMN_DOCUMENT_ID,
            DocumentsContract.Document.COLUMN_DISPLAY_NAME,
            DocumentsContract.Document.COLUMN_MIME_TYPE,
            DocumentsContract.Document.COLUMN_SIZE,
            DocumentsContract.Document.COLUMN_LAST_MODIFIED
        )
    }
}
