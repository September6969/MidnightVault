package win.september.midnightvault.network

import java.security.MessageDigest

internal object RequestSigning {
    fun bodyHash(body: ByteArray): String =
        MessageDigest.getInstance("SHA-256").digest(body).toHex()

    fun canonicalBytes(
        method: String,
        pathAndQuery: String,
        timestamp: String,
        nonce: String,
        bodyHash: String
    ): ByteArray = "$method\n$pathAndQuery\n$timestamp\n$nonce\n${bodyHash.lowercase()}"
        .toByteArray(Charsets.UTF_8)

    private fun ByteArray.toHex(): String = joinToString("") { "%02x".format(it) }
}
