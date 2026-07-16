package win.september.midnightvault.network

import java.io.IOException
import java.net.HttpURLConnection
import java.net.URL
import java.security.SecureRandom
import java.time.Instant
import java.util.UUID
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONObject
import win.september.midnightvault.backup.BackupRunResult
import win.september.midnightvault.config.BackupSettings
import win.september.midnightvault.config.DeviceKeyStore

data class RemoteCommand(
    val id: String,
    val type: String
)

class ControlPlaneClient(
    private val deviceKeys: DeviceKeyStore = DeviceKeyStore()
) {
    suspend fun registerDevice(
        settings: BackupSettings,
        controlToken: String
    ) = withContext(Dispatchers.IO) {
        val body = JSONObject()
            .put("id", settings.deviceId)
            .put("displayName", settings.deviceName)
            .put("publicKey", deviceKeys.publicKeyBase64())
            .put("createdAt", Instant.now().toString())
            .toString()
            .toByteArray(Charsets.UTF_8)
        request(
            method = "POST",
            baseUrl = settings.controlPlaneUrl,
            path = "/v1/devices",
            body = body,
            bearerToken = controlToken,
            deviceId = null,
            expectedStatuses = setOf(HttpURLConnection.HTTP_CREATED)
        )
        Unit
    }

    suspend fun reportBackup(
        settings: BackupSettings,
        result: BackupRunResult,
        status: String = "completed"
    ) = withContext(Dispatchers.IO) {
        val body = JSONObject()
            .put("taskId", UUID.randomUUID().toString())
            .put("deviceId", settings.deviceId)
            .put("status", status)
            .put("successCount", result.completedFiles)
            .put("skippedCount", 0)
            .put("failedCount", result.failedFiles)
            .put("totalBytes", result.completedBytes)
            .put("completedAt", Instant.now().toString())
            .toString()
            .toByteArray(Charsets.UTF_8)
        request(
            method = "POST",
            baseUrl = settings.controlPlaneUrl,
            path = "/v1/tasks/report",
            body = body,
            bearerToken = null,
            deviceId = settings.deviceId,
            expectedStatuses = setOf(HttpURLConnection.HTTP_NO_CONTENT)
        )
        Unit
    }

    suspend fun nextCommand(settings: BackupSettings): RemoteCommand? = withContext(Dispatchers.IO) {
        val response = request(
            method = "GET",
            baseUrl = settings.controlPlaneUrl,
            path = "/v1/commands/next",
            body = ByteArray(0),
            bearerToken = null,
            deviceId = settings.deviceId,
            expectedStatuses = setOf(HttpURLConnection.HTTP_OK, HttpURLConnection.HTTP_NO_CONTENT)
        )
        if (response.status == HttpURLConnection.HTTP_NO_CONTENT) return@withContext null
        val json = JSONObject(response.body)
        RemoteCommand(json.getString("id"), json.getString("type"))
    }

    suspend fun acknowledgeCommand(settings: BackupSettings, commandId: String) =
        withContext(Dispatchers.IO) {
            request(
                method = "POST",
                baseUrl = settings.controlPlaneUrl,
                path = "/v1/commands/$commandId/ack",
                body = ByteArray(0),
                bearerToken = null,
                deviceId = settings.deviceId,
                expectedStatuses = setOf(HttpURLConnection.HTTP_NO_CONTENT)
            )
            Unit
        }

    private fun request(
        method: String,
        baseUrl: String,
        path: String,
        body: ByteArray,
        bearerToken: String?,
        deviceId: String?,
        expectedStatuses: Set<Int>
    ): HttpResponse {
        val normalized = baseUrl.trim().trimEnd('/')
        require(normalized.startsWith("http://") || normalized.startsWith("https://")) {
            "控制面地址必须以 http:// 或 https:// 开头。"
        }
        val connection = (URL(normalized + path).openConnection() as HttpURLConnection).apply {
            requestMethod = method
            connectTimeout = 15_000
            readTimeout = 30_000
            useCaches = false
            setRequestProperty("Accept", "application/json")
            if (bearerToken != null) setRequestProperty("Authorization", "Bearer $bearerToken")
            if (deviceId != null) addSignatureHeaders(this, method, path, body, deviceId)
            if (method != "GET") {
                doOutput = true
                setRequestProperty("Content-Type", "application/json; charset=utf-8")
                setFixedLengthStreamingMode(body.size)
            }
        }
        try {
            if (method != "GET") connection.outputStream.use { it.write(body) }
            val status = connection.responseCode
            val stream = if (status >= 400) connection.errorStream else connection.inputStream
            val responseBody = stream?.bufferedReader(Charsets.UTF_8)?.use { it.readText() }.orEmpty()
            if (status !in expectedStatuses) {
                throw IOException("控制面返回 HTTP $status：${responseBody.take(300)}")
            }
            return HttpResponse(status, responseBody)
        } finally {
            connection.disconnect()
        }
    }

    private fun addSignatureHeaders(
        connection: HttpURLConnection,
        method: String,
        path: String,
        body: ByteArray,
        deviceId: String
    ) {
        val timestamp = Instant.now().epochSecond.toString()
        val nonceBytes = ByteArray(16).also(SecureRandom()::nextBytes)
        val nonce = nonceBytes.toHex()
        val bodyHash = RequestSigning.bodyHash(body)
        val canonical = RequestSigning.canonicalBytes(
            method,
            path,
            timestamp,
            nonce,
            bodyHash
        )
        connection.setRequestProperty("X-Device-Id", deviceId)
        connection.setRequestProperty("X-Timestamp", timestamp)
        connection.setRequestProperty("X-Nonce", nonce)
        connection.setRequestProperty("X-Signature", deviceKeys.sign(canonical))
    }

    private fun ByteArray.toHex(): String = joinToString("") { "%02x".format(it) }

    private data class HttpResponse(val status: Int, val body: String)
}
