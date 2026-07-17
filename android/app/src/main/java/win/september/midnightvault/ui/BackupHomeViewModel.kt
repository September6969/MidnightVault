package win.september.midnightvault.ui

import android.Manifest
import android.app.AlarmManager
import android.app.Application
import android.content.pm.PackageManager
import android.os.Build
import androidx.core.content.ContextCompat
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.SavedStateHandle
import androidx.lifecycle.viewModelScope
import java.util.concurrent.atomic.AtomicLong
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.flow
import kotlinx.coroutines.flow.onStart
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import win.september.midnightvault.backup.BackupScheduler
import win.september.midnightvault.config.AppSettings
import win.september.midnightvault.config.CredentialStore
import win.september.midnightvault.data.BackupQueueDatabase
import win.september.midnightvault.network.ControlPlaneClient
import win.september.midnightvault.network.ReceiverClient
import win.september.midnightvault.ui.model.BackupOperationEvent
import win.september.midnightvault.ui.model.BackupOperationKind
import win.september.midnightvault.ui.model.BackupOperationState
import win.september.midnightvault.ui.model.BackupUiState
import win.september.midnightvault.ui.model.ConnectionDraftState
import win.september.midnightvault.ui.model.MediaAccessState

class BackupHomeViewModel(
    application: Application,
    private val savedStateHandle: SavedStateHandle
) : AndroidViewModel(application) {
    private val appSettings = AppSettings(application)
    private val credentialStore = CredentialStore(application)
    private val queueDatabase = BackupQueueDatabase(application)
    private val refreshVersion = MutableStateFlow(0L)
    private val operationState = MutableStateFlow(BackupOperationState())
    private val connectionDraftState = MutableStateFlow(
        ConnectionDraftState(
            deviceName = savedStateHandle[DRAFT_DEVICE_NAME],
            receiverUrl = savedStateHandle[DRAFT_RECEIVER_URL],
            controlPlaneUrl = savedStateHandle[DRAFT_CONTROL_PLANE_URL]
        )
    )
    private val versionCounter = AtomicLong()
    private val eventCounter = AtomicLong()

    private val ticker = flow {
        while (currentCoroutineContext().isActive) {
            emit(Unit)
            delay(1_500)
        }
    }

    val uiState = combine(
        appSettings.observe().onStart { emit(appSettings.snapshot()) },
        ticker,
        refreshVersion,
        operationState,
        connectionDraftState
    ) { settings, _, _, operation, connectionDraft ->
        try {
            withContext(Dispatchers.IO) {
                val context = getApplication<Application>()
                BackupUiState(
                    settings = settings,
                    queueSummary = queueDatabase.summary(),
                    uploadTokenConfigured = credentialStore.hasUploadToken(),
                    controlTokenConfigured = credentialStore.hasControlToken(),
                    mediaAccess = mediaAccessState(context),
                    notificationPermissionGranted = notificationPermissionGranted(context),
                    exactAlarmGranted = exactAlarmPermissionGranted(context),
                    operation = operation,
                    connectionDraft = connectionDraft
                )
            }
        } catch (exception: Exception) {
            // 单次读取失败不终止状态流，后续刷新和轮询仍可恢复。
            BackupUiState(
                settings = settings,
                operation = operation,
                connectionDraft = connectionDraft,
                errorMessage = exception.message ?: exception.javaClass.simpleName
            )
        }
    }.stateIn(
        scope = viewModelScope,
        started = SharingStarted.WhileSubscribed(5_000),
        initialValue = BackupUiState()
    )

    fun refresh() {
        refreshVersion.value = versionCounter.incrementAndGet()
    }

    fun saveConnection(
        deviceName: String,
        receiverUrl: String,
        uploadToken: String
    ) {
        if (operationState.value.savingConnection) return
        operationState.update { it.copy(savingConnection = true, event = null) }
        viewModelScope.launch {
            val result = runCatching {
                appSettings.saveConnection(deviceName, receiverUrl)
                if (uploadToken.isNotBlank()) {
                    withContext(Dispatchers.IO) { credentialStore.saveUploadToken(uploadToken) }
                }
                val snapshot = appSettings.snapshot()
                val savedToken = withContext(Dispatchers.IO) { credentialStore.readUploadToken() }
                    ?: error("上传令牌尚未配置")
                ReceiverClient().registerUploadDevice(snapshot, savedToken)
                ReceiverClient().checkHealth(receiverUrl)
            }
            operationState.update { current ->
                current.copy(
                    savingConnection = false,
                    event = result.fold(
                        onSuccess = { online ->
                            operationEvent(
                                if (online) BackupOperationKind.ConnectionOnline
                                else BackupOperationKind.ConnectionOffline
                            )
                        },
                        onFailure = { error ->
                            operationEvent(BackupOperationKind.ConnectionFailed, error.message)
                        }
                    )
                )
            }
            if (result.isSuccess) {
                connectionDraftState.update {
                    it.copy(deviceName = null, receiverUrl = null, uploadToken = "")
                }
                savedStateHandle.remove<String>(DRAFT_DEVICE_NAME)
                savedStateHandle.remove<String>(DRAFT_RECEIVER_URL)
            }
            refresh()
        }
    }

    fun saveControlPlane(
        controlPlaneUrl: String,
        controlToken: String
    ) {
        if (operationState.value.savingControlPlane) return
        operationState.update { it.copy(savingControlPlane = true, event = null) }
        viewModelScope.launch {
            val result = runCatching {
                appSettings.saveControlPlane(controlPlaneUrl)
                if (controlToken.isNotBlank()) {
                    withContext(Dispatchers.IO) { credentialStore.saveControlToken(controlToken) }
                }
                val snapshot = appSettings.snapshot()
                val token = withContext(Dispatchers.IO) { credentialStore.readControlToken() }
                    ?: error("控制面令牌尚未配置")
                ControlPlaneClient().registerDevice(snapshot, token)
            }
            operationState.update { current ->
                current.copy(
                    savingControlPlane = false,
                    event = result.fold(
                        onSuccess = { operationEvent(BackupOperationKind.ControlRegistered) },
                        onFailure = { error ->
                            operationEvent(BackupOperationKind.ControlFailed, error.message)
                        }
                    )
                )
            }
            if (result.isSuccess) {
                connectionDraftState.update {
                    it.copy(controlPlaneUrl = null, controlToken = "")
                }
                savedStateHandle.remove<String>(DRAFT_CONTROL_PLANE_URL)
                BackupScheduler.scheduleControlPolling(getApplication())
            }
            refresh()
        }
    }

    fun clearUploadToken() {
        viewModelScope.launch {
            withContext(Dispatchers.IO) { credentialStore.clearUploadToken() }
            connectionDraftState.update { it.copy(uploadToken = "") }
            publishEvent(BackupOperationKind.UploadTokenCleared)
            refresh()
        }
    }

    fun clearControlToken() {
        viewModelScope.launch {
            withContext(Dispatchers.IO) { credentialStore.clearControlToken() }
            connectionDraftState.update { it.copy(controlToken = "") }
            publishEvent(BackupOperationKind.ControlTokenCleared)
            refresh()
        }
    }

    fun claimPairing(receiverUrl: String, code: String, defaultDeviceName: String) {
        if (operationState.value.pairing) return
        operationState.update { it.copy(pairing = true, event = null) }
        viewModelScope.launch {
            val result = runCatching {
                val current = appSettings.snapshot()
                val deviceName = current.deviceName.ifBlank { defaultDeviceName }
                val claim = ReceiverClient().claimPairing(
                    receiverUrl,
                    code,
                    current.deviceId,
                    deviceName
                )
                appSettings.saveConnection(deviceName, claim.receiverUrl)
                withContext(Dispatchers.IO) {
                    credentialStore.saveUploadToken(claim.uploadToken)
                }
                connectionDraftState.update { draft ->
                    draft.copy(deviceName = null, receiverUrl = null, uploadToken = "")
                }
                savedStateHandle.remove<String>(DRAFT_DEVICE_NAME)
                savedStateHandle.remove<String>(DRAFT_RECEIVER_URL)
                BackupScheduler.enqueueNow(getApplication())
            }
            operationState.update { current ->
                current.copy(
                    pairing = false,
                    event = result.fold(
                        onSuccess = { operationEvent(BackupOperationKind.PairingSucceeded) },
                        onFailure = { error ->
                            operationEvent(BackupOperationKind.PairingFailed, error.message)
                        }
                    )
                )
            }
            refresh()
        }
    }

    fun consumeOperationEvent(eventId: Long) {
        operationState.update { current ->
            if (current.event?.id == eventId) current.copy(event = null) else current
        }
    }

    fun updateDeviceName(value: String) {
        savedStateHandle[DRAFT_DEVICE_NAME] = value
        connectionDraftState.update { it.copy(deviceName = value) }
    }

    fun updateReceiverUrl(value: String) {
        savedStateHandle[DRAFT_RECEIVER_URL] = value
        connectionDraftState.update { it.copy(receiverUrl = value) }
    }

    fun updateUploadToken(value: String) {
        connectionDraftState.update { it.copy(uploadToken = value) }
    }

    fun updateControlPlaneUrl(value: String) {
        savedStateHandle[DRAFT_CONTROL_PLANE_URL] = value
        connectionDraftState.update { it.copy(controlPlaneUrl = value) }
    }

    fun updateControlToken(value: String) {
        connectionDraftState.update { it.copy(controlToken = value) }
    }

    private fun publishEvent(kind: BackupOperationKind, detail: String? = null) {
        operationState.update { it.copy(event = operationEvent(kind, detail)) }
    }

    private fun operationEvent(
        kind: BackupOperationKind,
        detail: String? = null
    ): BackupOperationEvent = BackupOperationEvent(
        id = eventCounter.incrementAndGet(),
        kind = kind,
        detail = detail
    )

    private companion object {
        const val DRAFT_DEVICE_NAME = "connection_draft_device_name"
        const val DRAFT_RECEIVER_URL = "connection_draft_receiver_url"
        const val DRAFT_CONTROL_PLANE_URL = "connection_draft_control_plane_url"
    }
}

private fun mediaAccessState(application: Application): MediaAccessState {
    if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) {
        val images = ContextCompat.checkSelfPermission(
            application,
            Manifest.permission.READ_MEDIA_IMAGES
        ) == PackageManager.PERMISSION_GRANTED
        val videos = ContextCompat.checkSelfPermission(
            application,
            Manifest.permission.READ_MEDIA_VIDEO
        ) == PackageManager.PERMISSION_GRANTED
        val selected = ContextCompat.checkSelfPermission(
            application,
            Manifest.permission.READ_MEDIA_VISUAL_USER_SELECTED
        ) == PackageManager.PERMISSION_GRANTED
        return when {
            images && videos -> MediaAccessState.Full
            selected || images || videos -> MediaAccessState.Partial
            else -> MediaAccessState.None
        }
    }
    if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
        val images = ContextCompat.checkSelfPermission(
            application,
            Manifest.permission.READ_MEDIA_IMAGES
        ) == PackageManager.PERMISSION_GRANTED
        val videos = ContextCompat.checkSelfPermission(
            application,
            Manifest.permission.READ_MEDIA_VIDEO
        ) == PackageManager.PERMISSION_GRANTED
        return if (images && videos) MediaAccessState.Full else MediaAccessState.None
    }
    return if (
        ContextCompat.checkSelfPermission(
            application,
            Manifest.permission.READ_EXTERNAL_STORAGE
        ) == PackageManager.PERMISSION_GRANTED
    ) {
        MediaAccessState.Full
    } else {
        MediaAccessState.None
    }
}

private fun notificationPermissionGranted(application: Application): Boolean =
    Build.VERSION.SDK_INT < Build.VERSION_CODES.TIRAMISU ||
        ContextCompat.checkSelfPermission(
            application,
            Manifest.permission.POST_NOTIFICATIONS
        ) == PackageManager.PERMISSION_GRANTED

private fun exactAlarmPermissionGranted(application: Application): Boolean =
    Build.VERSION.SDK_INT < Build.VERSION_CODES.S ||
        application.getSystemService(AlarmManager::class.java).canScheduleExactAlarms()
