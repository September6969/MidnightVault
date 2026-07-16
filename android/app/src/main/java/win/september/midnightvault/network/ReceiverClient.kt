package win.september.midnightvault.network

import java.io.IOException
import java.net.HttpURLConnection
import java.net.URL
import java.time.Instant
import java.security.SecureRandom
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONObject
import win.september.midnightvault.config.BackupSettings
import win.september.midnightvault.data.BackupItem
import win.september.midnightvault.config.DeviceKeyStore

data class UploadSession(
    val uploadId: String,
    val chunkSizeBytes: Int,
    val chunkCount: Int
)

data class UploadStatus(
    val uploadId: String,
    val chunkSizeBytes: Int,
    val chunkCount: Int,
    val receivedChunks: Set<Int>,
    val completed: Boolean
)

data class PairingClaim(
    val receiverUrl: String,
    val uploadToken: String
)

class ReceiverHttpException(
    val statusCode: Int,
    message: String
) : IOException(message) {
    val isRetryable: Boolean
        get() = statusCode == 408 || statusCode == 425 || statusCode == 429 || statusCode >= 500
}

class ReceiverClient(
    private val deviceKeys: DeviceKeyStore = DeviceKeyStore()
) {
    suspend fun startUpload(
        settings: BackupSettings,
        token: String,
        item: BackupItem,
        sha256: String
    ): UploadSession = withContext(Dispatchers.IO) {
        val file = JSONObject()
            .put("deviceId", settings.deviceId)
            .put("deviceName", settings.deviceName)
            .put("backupDate", item.backupDate)
            .put("category", item.category.wireName)
            .put("relativePath", item.relativePath)
            .put("size", item.size)
            .put("sha256", sha256)
            .put("originalModifiedAt", Instant.ofEpochMilli(item.modifiedAtMillis).toString())
            .put("isInitialBackup", item.isInitial)
        val response = request(
            method = "POST",
            url = endpoint(settings.receiverUrl, "/api/v1/uploads"),
            token = token,
            contentType = "application/json; charset=utf-8",
            body = JSONObject().put("file", file).toString().toByteArray(Charsets.UTF_8),
            deviceId = settings.deviceId,
            expectedStatus = HttpURLConnection.HTTP_CREATED
        )
        val json = JSONObject(response)
        UploadSession(
            uploadId = json.getString("uploadId"),
            chunkSizeBytes = json.getInt("chunkSizeBytes"),
            chunkCount = json.getInt("chunkCount")
        )
    }

    suspend fun putChunk(
        receiverUrl: String,
        token: String,
        deviceId: String,
        uploadId: String,
        index: Int,
        bytes: ByteArray,
        sha256: String
    ) = withContext(Dispatchers.IO) {
        request(
            method = "PUT",
            url = endpoint(receiverUrl, "/api/v1/uploads/$uploadId/chunks/$index"),
            token = token,
            contentType = "application/octet-stream",
            body = bytes,
            deviceId = deviceId,
            expectedStatus = HttpURLConnection.HTTP_NO_CONTENT,
            headers = mapOf("X-Chunk-Sha256" to sha256)
        )
        Unit
    }

    suspend fun getUploadStatus(
        receiverUrl: String,
        token: String,
        deviceId: String,
        uploadId: String
    ): UploadStatus = withContext(Dispatchers.IO) {
        val response = request(
            method = "GET",
            url = endpoint(receiverUrl, "/api/v1/uploads/$uploadId"),
            token = token,
            contentType = null,
            body = null,
            deviceId = deviceId,
            expectedStatus = HttpURLConnection.HTTP_OK
        )
        val json = JSONObject(response)
        val received = json.getJSONArray("receivedChunks")
        UploadStatus(
            uploadId = json.getString("uploadId"),
            chunkSizeBytes = json.getInt("chunkSizeBytes"),
            chunkCount = json.getInt("chunkCount"),
            receivedChunks = buildSet {
                for (index in 0 until received.length()) add(received.getInt(index))
            },
            completed = json.getBoolean("completed")
        )
    }

    suspend fun completeUpload(
        receiverUrl: String,
        token: String,
        deviceId: String,
        uploadId: String
    ) =
        withContext(Dispatchers.IO) {
            request(
                method = "POST",
                url = endpoint(receiverUrl, "/api/v1/uploads/$uploadId/complete"),
                token = token,
                contentType = "application/json; charset=utf-8",
                body = ByteArray(0),
                deviceId = deviceId,
                expectedStatus = HttpURLConnection.HTTP_OK
            )
            Unit
        }

    suspend fun checkHealth(receiverUrl: String): Boolean = withContext(Dispatchers.IO) {
        runCatching {
            request(
                method = "GET",
                url = endpoint(receiverUrl, "/healthz"),
                token = null,
                contentType = null,
                body = null,
                expectedStatus = HttpURLConnection.HTTP_OK
            )
        }.isSuccess
    }

    suspend fun claimPairing(
        receiverUrl: String,
        code: String,
        deviceId: String,
        deviceName: String
    ): PairingClaim = withContext(Dispatchers.IO) {
        val payload = JSONObject()
            .put("code", code)
            .put("deviceId", deviceId)
            .put("deviceName", deviceName)
            .put("publicKey", deviceKeys.publicKeyBase64())
        val response = request(
            method = "POST",
            url = endpoint(receiverUrl, "/api/v1/pairing/claim"),
            token = null,
            contentType = "application/json; charset=utf-8",
            body = payload.toString().toByteArray(Charsets.UTF_8),
            expectedStatus = HttpURLConnection.HTTP_OK
        )
        PairingClaim(receiverUrl.trim().trimEnd('/'), JSONObject(response).getString("uploadToken"))
    }

    suspend fun registerUploadDevice(
        settings: BackupSettings,
        token: String
    ) = withContext(Dispatchers.IO) {
        val body = JSONObject()
            .put("deviceId", settings.deviceId)
            .put("deviceName", settings.deviceName)
            .put("publicKey", deviceKeys.publicKeyBase64())
            .toString()
            .toByteArray(Charsets.UTF_8)
        request(
            method = "POST",
            url = endpoint(settings.receiverUrl, "/api/v1/devices/register"),
            token = token,
            contentType = "application/json; charset=utf-8",
            body = body,
            expectedStatus = HttpURLConnection.HTTP_NO_CONTENT
        )
        Unit
    }

    private fun request(
        method: String,
        url: URL,
        token: String?,
        contentType: String?,
        body: ByteArray?,
        expectedStatus: Int,
        deviceId: String? = null,
        headers: Map<String, String> = emptyMap()
    ): String {
        val connection = (url.openConnection() as HttpURLConnection).apply {
            requestMethod = method
            connectTimeout = ConnectTimeoutMillis
            readTimeout = ReadTimeoutMillis
            useCaches = false
            setRequestProperty("Accept", "application/json")
            if (!token.isNullOrBlank()) setRequestProperty("X-MidnightVault-Token", token)
            if (!deviceId.isNullOrBlank()) {
                addSignatureHeaders(this, method, url.file, body ?: ByteArray(0), deviceId)
            }
            if (contentType != null) setRequestProperty("Content-Type", contentType)
            headers.forEach { (name, value) -> setRequestProperty(name, value) }
            if (body != null) {
                doOutput = true
                setFixedLengthStreamingMode(body.size)
            }
        }

        try {
            if (body != null) connection.outputStream.use { it.write(body) }
            val status = connection.responseCode
            val stream = if (status >= 400) connection.errorStream else connection.inputStream
            val response = stream?.bufferedReader(Charsets.UTF_8)?.use { it.readText() }.orEmpty()
            if (status != expectedStatus) {
                val detail = response.replace(Regex("\\s+"), " ").take(MaximumErrorLength)
                throw ReceiverHttpException(status, "接收端返回 HTTP $status${if (detail.isBlank()) "" else "：$detail"}")
            }
            return response
        } finally {
            connection.disconnect()
        }
    }

    private fun endpoint(baseUrl: String, path: String): URL {
        val normalized = baseUrl.trim().trimEnd('/')
        require(normalized.startsWith("http://") || normalized.startsWith("https://")) {
            "接收端地址必须以 http:// 或 https:// 开头。"
        }
        return URL(normalized + path)
    }

    private fun addSignatureHeaders(
        connection: HttpURLConnection,
        method: String,
        pathAndQuery: String,
        body: ByteArray,
        deviceId: String
    ) {
        val timestamp = Instant.now().epochSecond.toString()
        val nonce = ByteArray(16).also(SecureRandom()::nextBytes).toHex()
        val contentHash = RequestSigning.bodyHash(body)
        val canonical = RequestSigning.canonicalBytes(
            method,
            pathAndQuery,
            timestamp,
            nonce,
            contentHash
        )
        connection.setRequestProperty("X-Device-Id", deviceId)
        connection.setRequestProperty("X-Timestamp", timestamp)
        connection.setRequestProperty("X-Nonce", nonce)
        connection.setRequestProperty("X-Content-Sha256", contentHash)
        connection.setRequestProperty("X-Signature", deviceKeys.sign(canonical))
    }

    private fun ByteArray.toHex(): String = joinToString("") { "%02x".format(it) }

    private companion object {
        const val ConnectTimeoutMillis = 15_000
        const val ReadTimeoutMillis = 180_000
        const val MaximumErrorLength = 500
    }
}
