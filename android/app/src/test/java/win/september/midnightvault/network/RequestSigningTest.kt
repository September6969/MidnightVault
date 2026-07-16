package win.september.midnightvault.network

import org.junit.Assert.assertEquals
import org.junit.Test

class RequestSigningTest {
    @Test
    fun canonicalBytes_与服务端签名格式一致() {
        val hash = RequestSigning.bodyHash("hello".toByteArray())
        val canonical = RequestSigning.canonicalBytes(
            "POST",
            "/api/v1/uploads?mode=test",
            "1776297600",
            "0011223344556677",
            hash
        ).toString(Charsets.UTF_8)

        assertEquals(
            "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824",
            hash
        )
        assertEquals(
            "POST\n/api/v1/uploads?mode=test\n1776297600\n0011223344556677\n$hash",
            canonical
        )
    }
}
