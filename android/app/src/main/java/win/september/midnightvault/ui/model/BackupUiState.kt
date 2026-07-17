package win.september.midnightvault.ui.model

import win.september.midnightvault.config.BackupSettings
import win.september.midnightvault.data.QueueSummary

enum class MediaAccessState {
    None,
    Partial,
    Full
}

enum class BackupOperationKind {
    ConnectionOnline,
    ConnectionOffline,
    ConnectionFailed,
    ControlRegistered,
    ControlFailed,
    UploadTokenCleared,
    ControlTokenCleared,
    PairingSucceeded,
    PairingFailed
}

data class BackupOperationEvent(
    val id: Long,
    val kind: BackupOperationKind,
    val detail: String? = null
)

data class BackupOperationState(
    val savingConnection: Boolean = false,
    val savingControlPlane: Boolean = false,
    val pairing: Boolean = false,
    val event: BackupOperationEvent? = null
)

data class ConnectionDraftState(
    val deviceName: String? = null,
    val receiverUrl: String? = null,
    val uploadToken: String = "",
    val controlPlaneUrl: String? = null,
    val controlToken: String = ""
)

data class BackupUiState(
    val settings: BackupSettings? = null,
    val queueSummary: QueueSummary = QueueSummary(0, 0, 0, 0),
    val uploadTokenConfigured: Boolean = false,
    val controlTokenConfigured: Boolean = false,
    val mediaAccess: MediaAccessState = MediaAccessState.None,
    val notificationPermissionGranted: Boolean = false,
    val exactAlarmGranted: Boolean = false,
    val operation: BackupOperationState = BackupOperationState(),
    val connectionDraft: ConnectionDraftState = ConnectionDraftState(),
    val errorMessage: String? = null
) {
    val isLoading: Boolean
        get() = settings == null && errorMessage == null

    val canBackupNow: Boolean
        get() = settings?.isConnectionConfigured == true && uploadTokenConfigured
}
