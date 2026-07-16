package win.september.midnightvault.config

import android.content.Context
import androidx.datastore.preferences.core.booleanPreferencesKey
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.core.stringSetPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import java.util.UUID
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.map

private val Context.midnightVaultDataStore by preferencesDataStore(name = "midnightvault_settings")

data class BackupSettings(
    val deviceId: String,
    val deviceName: String,
    val receiverUrl: String,
    val controlPlaneUrl: String,
    val treeUris: Set<String>,
    val initialBackupCompleted: Boolean
) {
    val isConnectionConfigured: Boolean
        get() = receiverUrl.isNotBlank()

    val isControlPlaneConfigured: Boolean
        get() = controlPlaneUrl.isNotBlank()
}

class AppSettings(context: Context) {
    private val appContext = context.applicationContext

    suspend fun snapshot(): BackupSettings {
        var preferences = appContext.midnightVaultDataStore.data.first()
        if (preferences[Keys.DeviceId].isNullOrBlank()) {
            appContext.midnightVaultDataStore.edit {
                it[Keys.DeviceId] = UUID.randomUUID().toString()
            }
            preferences = appContext.midnightVaultDataStore.data.first()
        }
        return preferences.toSettings()
    }

    fun observe() = appContext.midnightVaultDataStore.data.map { it.toSettings() }

    suspend fun saveConnection(deviceName: String, receiverUrl: String) {
        val normalizedUrl = receiverUrl.trim().trimEnd('/')
        appContext.midnightVaultDataStore.edit {
            it[Keys.DeviceName] = deviceName.trim()
            it[Keys.ReceiverUrl] = normalizedUrl
        }
    }

    suspend fun saveControlPlane(controlPlaneUrl: String) {
        appContext.midnightVaultDataStore.edit {
            it[Keys.ControlPlaneUrl] = controlPlaneUrl.trim().trimEnd('/')
        }
    }

    suspend fun addTreeUri(uri: String) {
        appContext.midnightVaultDataStore.edit {
            val current = it[Keys.TreeUris].orEmpty()
            it[Keys.TreeUris] = current + uri
        }
    }

    suspend fun removeTreeUri(uri: String) {
        appContext.midnightVaultDataStore.edit {
            val current = it[Keys.TreeUris].orEmpty()
            it[Keys.TreeUris] = current - uri
        }
    }

    suspend fun markInitialBackupCompleted() {
        appContext.midnightVaultDataStore.edit {
            it[Keys.InitialBackupCompleted] = true
        }
    }

    private fun androidx.datastore.preferences.core.Preferences.toSettings() = BackupSettings(
        deviceId = this[Keys.DeviceId].orEmpty(),
        deviceName = this[Keys.DeviceName].orEmpty(),
        receiverUrl = this[Keys.ReceiverUrl].orEmpty(),
        controlPlaneUrl = this[Keys.ControlPlaneUrl].orEmpty(),
        treeUris = this[Keys.TreeUris].orEmpty(),
        initialBackupCompleted = this[Keys.InitialBackupCompleted] ?: false
    )

    private object Keys {
        val DeviceId = stringPreferencesKey("device_id")
        val DeviceName = stringPreferencesKey("device_name")
        val ReceiverUrl = stringPreferencesKey("receiver_url")
        val ControlPlaneUrl = stringPreferencesKey("control_plane_url")
        val TreeUris = stringSetPreferencesKey("tree_uris")
        val InitialBackupCompleted = booleanPreferencesKey("initial_backup_completed")
    }
}
