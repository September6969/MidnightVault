package win.september.midnightvault.data

enum class BackupCategory(val wireName: String) {
    PHOTO("Photo"),
    VIDEO("Video"),
    DOWNLOAD("Download"),
    DOCUMENT("Document"),
    CUSTOM("Custom")
}
enum class BackupState {
    PENDING,
    UPLOADING,
    COMPLETED,
    FAILED
}

data class ScannedFile(
    val sourceUri: String,
    val category: BackupCategory,
    val relativePath: String,
    val size: Long,
    val modifiedAtMillis: Long,
    val backupDate: String,
    val isInitial: Boolean
)

data class BackupItem(
    val id: Long,
    val sourceUri: String,
    val category: BackupCategory,
    val relativePath: String,
    val size: Long,
    val modifiedAtMillis: Long,
    val sha256: String?,
    val backupDate: String,
    val isInitial: Boolean,
    val state: BackupState,
    val uploadId: String?,
    val chunkSize: Int?,
    val nextChunk: Int,
    val error: String?
)

data class QueueSummary(
    val pending: Int,
    val uploading: Int,
    val completed: Int,
    val failed: Int
) {
    val waiting: Int = pending + uploading
}
