package win.september.midnightvault.config

import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import java.security.KeyPairGenerator
import java.security.KeyStore
import java.security.PrivateKey
import java.security.Signature
import java.security.spec.ECGenParameterSpec

class DeviceKeyStore {
    fun publicKeyBase64(): String = Base64.encodeToString(
        getOrCreateKeyPair().certificate.publicKey.encoded,
        Base64.NO_WRAP
    )

    fun sign(canonical: ByteArray): String {
        val entry = getOrCreateKeyPair()
        val signature = Signature.getInstance("SHA256withECDSA")
        signature.initSign(entry.privateKey)
        signature.update(canonical)
        return Base64.encodeToString(signature.sign(), Base64.NO_WRAP)
    }

    private fun getOrCreateKeyPair(): KeyStore.PrivateKeyEntry {
        val keyStore = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (keyStore.getEntry(KeyAlias, null) as? KeyStore.PrivateKeyEntry)?.let { return it }

        val generator = KeyPairGenerator.getInstance(
            KeyProperties.KEY_ALGORITHM_EC,
            "AndroidKeyStore"
        )
        generator.initialize(
            KeyGenParameterSpec.Builder(
                KeyAlias,
                KeyProperties.PURPOSE_SIGN or KeyProperties.PURPOSE_VERIFY
            )
                .setAlgorithmParameterSpec(ECGenParameterSpec("secp256r1"))
                .setDigests(KeyProperties.DIGEST_SHA256)
                .build()
        )
        generator.generateKeyPair()
        return keyStore.getEntry(KeyAlias, null) as KeyStore.PrivateKeyEntry
    }

    private companion object {
        const val KeyAlias = "midnightvault-device-signing-key"
    }
}
