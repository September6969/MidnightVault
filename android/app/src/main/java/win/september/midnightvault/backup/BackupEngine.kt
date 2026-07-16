package win.september.midnightvault.backup

import android.content.Context
import android.net.Uri
import java.io.FileNotFoundException
import java.io.IOException
import java.io.InputStream
import java.security.MessageDigest
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import win.september.midnightvault.config.AppSettings
import win.september.midnightvault.config.CredentialStore
import win.september.midnightvault.data.BackupItem
import win.september.midnightvault.data.BackupQueueDatabase
import win.september.midnightvault.network.ReceiverClient
import win.september.midnightvault.network.ReceiverHttpException
import win.september.midnightvault.scan.BackupScanner

data class BackupProgress(
    val completedFiles: Int,
    val currentFile: String,
    val currentChunk: Int,
    val chunkCount: Int
)

data class BackupRunResult(
    val discoveredFiles: Int,
    val completedFiles: Int,
    val completedBytes: Long,
    val failedFiles: Int,
    val inaccessibleSources: List<String>
)

class BackupConfigurationException(message: String) : IllegalStateException(message)
class BackupRetryableException(message: String, cause: Throwable) : IOException(message, cause)

class BackupEngine(
    context: Context,
    private val database: BackupQueueDatabase = BackupQueueDatabase(context),
    private val settings: AppSettings = AppSettings(context),
    private val credentials: CredentialStore = CredentialStore(context),
    private val scanner: BackupScanner = BackupScanner(context, database, settings),
    private val client: ReceiverClient = ReceiverClient()
) {
    private val resolver = context.applicationContext.contentResolver

    suspend fun run(onProgress: suspend (BackupProgress) -> Unit = {}): BackupRunResult {
        val connection = settings.snapshot()
        val token = credentials.readUploadToken()
        if (!connection.isConnectionConfigured || connection.deviceName.isBlank() || token.isNullOrBlank()) {
            throw BackupConfigurationException("请先填写 PC 接收端地址、设备名称和上传令牌。")
        }

        database.retryFailures()
        val scanReport = scanner.scan()
        var completed = 0
        var completedBytes = 0L
        var failed = 0

        while (true) {
            val item = database.nextWorkItem() ?: break
            onProgress(BackupProgress(completed, item.relativePath, item.nextChunk, 0))
            try {
                processItem(connection, token, item) { chunk, count ->
                    onProgress(BackupProgress(completed, item.relativePath, chunk, count))
                }
                completed++
                completedBytes += item.size
            } catch (exception: ReceiverHttpException) {
                when {
                    exception.statusCode == 401 || exception.statusCode == 403 -> {
                        database.recordRetryableError(item.id, "上传令牌无效。")
                        throw BackupConfigurationException("PC 拒绝了上传令牌，请重新配置。")
                    }
                    exception.isRetryable -> {
                        database.recordRetryableError(item.id, exception.message.orEmpty())
                        throw BackupRetryableException("接收端暂时不可用。", exception)
                    }
                    else -> {
                        database.markFailed(item.id, exception.message.orEmpty())
                        failed++
                    }
                }
            } catch (exception: SecurityException) {
                database.markFailed(item.id, "文件授权已失效：${exception.message.orEmpty()}")
                failed++
            } catch (exception: FileNotFoundException) {
                database.markFailed(item.id, "源文件已不存在。")
                failed++
            } catch (exception: IOException) {
                database.recordRetryableError(item.id, exception.message.orEmpty())
                throw BackupRetryableException("网络或文件读取暂时失败。", exception)
            } catch (exception: IllegalArgumentException) {
                database.markFailed(item.id, exception.message.orEmpty())
                failed++
            }
        }

        if (!database.hasIncompleteInitialItems()) settings.markInitialBackupCompleted()
        return BackupRunResult(
            discoveredFiles = scanReport.discovered,
            completedFiles = completed,
            completedBytes = completedBytes,
            failedFiles = failed,
            inaccessibleSources = scanReport.inaccessibleSources
        )
    }

    private suspend fun processItem(
        connection: win.september.midnightvault.config.BackupSettings,
        token: String,
        originalItem: BackupItem,
        onChunk: suspend (Int, Int) -> Unit
    ) {
        var item = originalItem
        var restartedMissingSession = false
        while (true) {
            try {
                val sha256 = item.sha256 ?: hashSource(item).also { database.markHash(item.id, it) }
                var uploadItem = item
                val session = if (item.uploadId.isNullOrBlank() || item.chunkSize == null) {
                    client.startUpload(connection, token, item, sha256).also {
                        require(it.chunkSizeBytes in 1..MaximumChunkSizeBytes) {
                            "接收端分块大小不受支持：${it.chunkSizeBytes}。"
                        }
                        database.markUploadStarted(item.id, it.uploadId, it.chunkSizeBytes)
                    }
                } else {
                    val status = client.getUploadStatus(
                        connection.receiverUrl,
                        token,
                        connection.deviceId,
                        item.uploadId
                    )
                    if (status.completed) {
                        database.markCompleted(item.id)
                        return
                    }
                    require(status.chunkSizeBytes == item.chunkSize) {
                        "接收端分块大小与本地断点不一致。"
                    }
                    var confirmedNext = 0
                    while (status.receivedChunks.contains(confirmedNext)) confirmedNext++
                    uploadItem = item.copy(nextChunk = confirmedNext)
                    database.markChunkUploaded(item.id, confirmedNext)
                    win.september.midnightvault.network.UploadSession(
                        status.uploadId,
                        status.chunkSizeBytes,
                        status.chunkCount
                    )
                }

                uploadRemainingChunks(
                    connection.receiverUrl,
                    token,
                    connection.deviceId,
                    uploadItem,
                    session,
                    onChunk
                )
                client.completeUpload(
                    connection.receiverUrl,
                    token,
                    connection.deviceId,
                    session.uploadId
                )
                database.markCompleted(item.id)
                return
            } catch (exception: ReceiverHttpException) {
                if (exception.statusCode == 404 && !restartedMissingSession) {
                    database.resetUpload(item.id)
                    item = item.copy(uploadId = null, chunkSize = null, nextChunk = 0)
                    restartedMissingSession = true
                    continue
                }
                throw exception
            }
        }
    }

    private suspend fun uploadRemainingChunks(
        receiverUrl: String,
        token: String,
        deviceId: String,
        item: BackupItem,
        session: win.september.midnightvault.network.UploadSession,
        onChunk: suspend (Int, Int) -> Unit
    ) = withContext(Dispatchers.IO) {
        resolver.openInputStream(Uri.parse(item.sourceUri))?.use { input ->
            val offset = item.nextChunk.toLong() * session.chunkSizeBytes
            skipFully(input, offset)
            for (index in item.nextChunk until session.chunkCount) {
                val expectedLength = minOf(
                    session.chunkSizeBytes.toLong(),
                    item.size - index.toLong() * session.chunkSizeBytes
                ).toInt()
                val bytes = readExactly(input, expectedLength)
                client.putChunk(
                    receiverUrl = receiverUrl,
                    token = token,
                    deviceId = deviceId,
                    uploadId = session.uploadId,
                    index = index,
                    bytes = bytes,
                    sha256 = bytes.sha256()
                )
                database.markChunkUploaded(item.id, index + 1)
                onChunk(index + 1, session.chunkCount)
            }
        } ?: throw FileNotFoundException("无法打开源文件。")
    }

    private suspend fun hashSource(item: BackupItem): String = withContext(Dispatchers.IO) {
        val digest = MessageDigest.getInstance("SHA-256")
        var total = 0L
        resolver.openInputStream(Uri.parse(item.sourceUri))?.use { input ->
            val buffer = ByteArray(HashBufferSize)
            while (true) {
                val read = input.read(buffer)
                if (read < 0) break
                if (read == 0) continue
                digest.update(buffer, 0, read)
                total += read
            }
        } ?: throw FileNotFoundException("无法打开源文件。")
        if (total != item.size) {
            throw IllegalArgumentException("源文件大小在扫描后发生变化，请在下次扫描重试。")
        }
        digest.digest().toHex()
    }

    private fun skipFully(input: InputStream, bytes: Long) {
        var remaining = bytes
        while (remaining > 0) {
            val skipped = input.skip(remaining)
            if (skipped > 0) {
                remaining -= skipped
            } else if (input.read() >= 0) {
                remaining--
            } else {
                throw IOException("源文件长度短于断点位置。")
            }
        }
    }

    private fun readExactly(input: InputStream, expectedLength: Int): ByteArray {
        val bytes = ByteArray(expectedLength)
        var offset = 0
        while (offset < expectedLength) {
            val read = input.read(bytes, offset, expectedLength - offset)
            if (read < 0) throw IOException("源文件在读取分块时提前结束。")
            if (read > 0) offset += read
        }
        return bytes
    }

    private fun ByteArray.sha256(): String = MessageDigest.getInstance("SHA-256")
        .digest(this)
        .toHex()

    private fun ByteArray.toHex(): String = joinToString("") { "%02x".format(it) }

    private companion object {
        const val HashBufferSize = 1024 * 1024
        const val MaximumChunkSizeBytes = 64 * 1024 * 1024
    }
}
