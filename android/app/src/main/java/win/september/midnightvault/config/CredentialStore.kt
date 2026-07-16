package win.september.midnightvault.config

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

class CredentialStore(context: Context) {
    private val preferences = context.applicationContext.getSharedPreferences(
        "midnightvault_credentials",
        Context.MODE_PRIVATE
    )

    fun saveUploadToken(token: String) {
        saveSecret(token, UploadTokenCiphertext, UploadTokenIv, UploadKeyAlias)
    }

    fun readUploadToken(): String? = readSecret(UploadTokenCiphertext, UploadTokenIv, UploadKeyAlias)

    fun hasUploadToken(): Boolean = !readUploadToken().isNullOrBlank()

    fun clearUploadToken() = clearSecret(UploadTokenCiphertext, UploadTokenIv)

    fun saveControlToken(token: String) {
        saveSecret(token, ControlTokenCiphertext, ControlTokenIv, ControlKeyAlias)
    }

    fun readControlToken(): String? = readSecret(ControlTokenCiphertext, ControlTokenIv, ControlKeyAlias)

    fun hasControlToken(): Boolean = !readControlToken().isNullOrBlank()

    fun clearControlToken() = clearSecret(ControlTokenCiphertext, ControlTokenIv)

    private fun saveSecret(token: String, ciphertextKey: String, ivKey: String, keyAlias: String) {
        if (token.isBlank()) {
            clearSecret(ciphertextKey, ivKey)
            return
        }

        val cipher = Cipher.getInstance(Transformation)
        cipher.init(Cipher.ENCRYPT_MODE, getOrCreateKey(keyAlias))
        val ciphertext = cipher.doFinal(token.trim().toByteArray(Charsets.UTF_8))
        preferences.edit()
            .putString(ciphertextKey, Base64.encodeToString(ciphertext, Base64.NO_WRAP))
            .putString(ivKey, Base64.encodeToString(cipher.iv, Base64.NO_WRAP))
            .apply()
    }

    private fun readSecret(ciphertextKey: String, ivKey: String, keyAlias: String): String? {
        val encodedCiphertext = preferences.getString(ciphertextKey, null) ?: return null
        val encodedIv = preferences.getString(ivKey, null) ?: return null
        return runCatching {
            val cipher = Cipher.getInstance(Transformation)
            cipher.init(
                Cipher.DECRYPT_MODE,
                getOrCreateKey(keyAlias),
                GCMParameterSpec(128, Base64.decode(encodedIv, Base64.NO_WRAP))
            )
            val plaintext = cipher.doFinal(Base64.decode(encodedCiphertext, Base64.NO_WRAP))
            plaintext.toString(Charsets.UTF_8)
        }.getOrElse {
            clearSecret(ciphertextKey, ivKey)
            null
        }
    }

    private fun clearSecret(ciphertextKey: String, ivKey: String) {
        preferences.edit().remove(ciphertextKey).remove(ivKey).apply()
    }

    private fun getOrCreateKey(keyAlias: String): SecretKey {
        val keyStore = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (keyStore.getKey(keyAlias, null) as? SecretKey)?.let { return it }

        val generator = KeyGenerator.getInstance(
            KeyProperties.KEY_ALGORITHM_AES,
            "AndroidKeyStore"
        )
        generator.init(
            KeyGenParameterSpec.Builder(
                keyAlias,
                KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT
            )
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .build()
        )
        return generator.generateKey()
    }

    private companion object {
        const val UploadKeyAlias = "midnightvault-upload-token"
        const val ControlKeyAlias = "midnightvault-control-token"
        const val Transformation = "AES/GCM/NoPadding"
        const val UploadTokenCiphertext = "upload_token_ciphertext"
        const val UploadTokenIv = "upload_token_iv"
        const val ControlTokenCiphertext = "control_token_ciphertext"
        const val ControlTokenIv = "control_token_iv"
    }
}
