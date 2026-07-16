package win.september.midnightvault

import android.Manifest
import android.app.AlarmManager
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.provider.Settings
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import androidx.core.content.ContextCompat
import androidx.lifecycle.lifecycleScope
import java.util.Locale
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import win.september.midnightvault.backup.BackupScheduler
import win.september.midnightvault.config.AppSettings
import win.september.midnightvault.config.BackupSettings
import win.september.midnightvault.config.CredentialStore
import win.september.midnightvault.data.BackupQueueDatabase
import win.september.midnightvault.data.QueueSummary
import win.september.midnightvault.network.ReceiverClient
import win.september.midnightvault.network.ControlPlaneClient

class MainActivity : ComponentActivity() {
    private val appSettings by lazy { AppSettings(this) }
    private val credentialStore by lazy { CredentialStore(this) }
    private val queueDatabase by lazy { BackupQueueDatabase(this) }

    private val notificationPermission = registerForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { }

    private val mediaPermissions = registerForActivityResult(
        ActivityResultContracts.RequestMultiplePermissions()
    ) { }

    private val directoryPicker = registerForActivityResult(
        ActivityResultContracts.OpenDocumentTree()
    ) { uri ->
        if (uri == null) return@registerForActivityResult
        runCatching {
            contentResolver.takePersistableUriPermission(uri, Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }
        lifecycleScope.launch { appSettings.addTreeUri(uri.toString()) }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        requestNotificationPermission()
        BackupScheduler.scheduleNext(this)
        BackupScheduler.scheduleControlPolling(this)
        processPairingIntent(intent)

        setContent {
            MaterialTheme {
                BackupHome(
                    appSettings = appSettings,
                    credentialStore = credentialStore,
                    queueDatabase = queueDatabase,
                    onRequestMediaPermission = ::requestMediaPermissions,
                    onChooseDirectory = { directoryPicker.launch(null) },
                    onRemoveDirectory = ::removeDirectory,
                    onBackupNow = { BackupScheduler.enqueueNow(this) },
                    onExactAlarmSettings = ::openExactAlarmSettings
                )
            }
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        processPairingIntent(intent)
    }

    private fun requestNotificationPermission() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            notificationPermission.launch(Manifest.permission.POST_NOTIFICATIONS)
        }
    }

    private fun requestMediaPermissions() {
        val permissions = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) {
            arrayOf(
                Manifest.permission.READ_MEDIA_IMAGES,
                Manifest.permission.READ_MEDIA_VIDEO,
                Manifest.permission.READ_MEDIA_VISUAL_USER_SELECTED
            )
        } else if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            arrayOf(Manifest.permission.READ_MEDIA_IMAGES, Manifest.permission.READ_MEDIA_VIDEO)
        } else {
            arrayOf(Manifest.permission.READ_EXTERNAL_STORAGE)
        }
        mediaPermissions.launch(permissions)
    }

    private fun removeDirectory(rawUri: String) {
        runCatching {
            contentResolver.releasePersistableUriPermission(
                Uri.parse(rawUri),
                Intent.FLAG_GRANT_READ_URI_PERMISSION
            )
        }
        lifecycleScope.launch { appSettings.removeTreeUri(rawUri) }
    }

    private fun openExactAlarmSettings() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            startActivity(
                Intent(
                    Settings.ACTION_REQUEST_SCHEDULE_EXACT_ALARM,
                    Uri.parse("package:$packageName")
                )
            )
        }
    }

    private fun processPairingIntent(intent: Intent?) {
        val uri = intent?.data ?: return
        if (uri.scheme != "midnightvault" || uri.host != "pair") return
        val receiverUrl = uri.getQueryParameter("receiver").orEmpty()
        val code = uri.getQueryParameter("code").orEmpty()
        if (receiverUrl.isBlank() || code.isBlank()) return

        lifecycleScope.launch {
            runCatching {
                val current = appSettings.snapshot()
                val deviceName = current.deviceName.ifBlank { defaultDeviceName() }
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
                BackupScheduler.enqueueNow(this@MainActivity)
            }.onSuccess {
                Toast.makeText(
                    this@MainActivity,
                    getString(R.string.pairing_succeeded),
                    Toast.LENGTH_LONG
                ).show()
            }.onFailure {
                Toast.makeText(
                    this@MainActivity,
                    getString(R.string.pairing_failed, it.message.orEmpty()),
                    Toast.LENGTH_LONG
                ).show()
            }
        }
    }
}

@Composable
@OptIn(ExperimentalMaterial3Api::class)
private fun BackupHome(
    appSettings: AppSettings,
    credentialStore: CredentialStore,
    queueDatabase: BackupQueueDatabase,
    onRequestMediaPermission: () -> Unit,
    onChooseDirectory: () -> Unit,
    onRemoveDirectory: (String) -> Unit,
    onBackupNow: () -> Unit,
    onExactAlarmSettings: () -> Unit
) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    var settings by remember { mutableStateOf<BackupSettings?>(null) }
    var summary by remember { mutableStateOf(QueueSummary(0, 0, 0, 0)) }
    var mediaPermissionGranted by remember { mutableStateOf(hasMediaPermission(context)) }
    var exactAlarmGranted by remember { mutableStateOf(hasExactAlarmPermission(context)) }
    var tokenConfigured by remember { mutableStateOf(false) }
    var deviceName by remember { mutableStateOf("") }
    var receiverUrl by remember { mutableStateOf("") }
    var uploadToken by remember { mutableStateOf("") }
    var controlPlaneUrl by remember { mutableStateOf("") }
    var controlToken by remember { mutableStateOf("") }
    var controlTokenConfigured by remember { mutableStateOf(false) }
    var formInitialized by remember { mutableStateOf(false) }
    var statusMessage by remember { mutableStateOf("") }
    var saving by remember { mutableStateOf(false) }
    val checkingConnectionMessage = stringResource(R.string.checking_connection)
    val connectionOnlineMessage = stringResource(R.string.connection_saved_online)
    val connectionOfflineMessage = stringResource(R.string.connection_saved_offline)
    val saveFailedMessage = stringResource(R.string.save_failed)
    val tokenClearedMessage = stringResource(R.string.token_cleared)
    val registeringControlMessage = stringResource(R.string.registering_control_plane)
    val controlTokenRequiredMessage = stringResource(R.string.control_token_required)
    val controlRegisteredMessage = stringResource(R.string.control_plane_registered)
    val controlTokenClearedMessage = stringResource(R.string.control_token_cleared)
    val uploadTokenRequiredMessage = stringResource(R.string.token_required_hint)

    LaunchedEffect(Unit) {
        while (isActive) {
            val currentSettings = appSettings.snapshot()
            val previousSettings = settings
            settings = currentSettings
            if (!formInitialized || previousSettings?.receiverUrl != currentSettings.receiverUrl) {
                deviceName = currentSettings.deviceName.ifBlank { defaultDeviceName() }
                receiverUrl = currentSettings.receiverUrl
                controlPlaneUrl = currentSettings.controlPlaneUrl
                formInitialized = true
            }
            summary = withContext(Dispatchers.IO) { queueDatabase.summary() }
            tokenConfigured = withContext(Dispatchers.IO) { credentialStore.hasUploadToken() }
            controlTokenConfigured = withContext(Dispatchers.IO) { credentialStore.hasControlToken() }
            mediaPermissionGranted = hasMediaPermission(context)
            exactAlarmGranted = hasExactAlarmPermission(context)
            delay(1_500)
        }
    }

    Scaffold(
        topBar = { TopAppBar(title = { Text(stringResource(R.string.app_name)) }) }
    ) { padding ->
        LazyColumn(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding),
            contentPadding = PaddingValues(20.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp)
        ) {
            item {
                Text(
                    stringResource(R.string.phone_backup_title),
                    style = MaterialTheme.typography.headlineMedium
                )
            }
            item {
                Text(
                    stringResource(R.string.backup_schedule_summary),
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }
            item {
                QueueCard(summary)
            }
            item {
                ConfigurationCard(
                    deviceName = deviceName,
                    receiverUrl = receiverUrl,
                    uploadToken = uploadToken,
                    tokenConfigured = tokenConfigured,
                    saving = saving,
                    onDeviceNameChanged = { deviceName = it },
                    onReceiverUrlChanged = { receiverUrl = it },
                    onUploadTokenChanged = { uploadToken = it },
                    onSave = {
                        saving = true
                        statusMessage = checkingConnectionMessage
                        scope.launch {
                            runCatching {
                                appSettings.saveConnection(deviceName, receiverUrl)
                                if (uploadToken.isNotBlank()) {
                                    withContext(Dispatchers.IO) {
                                        credentialStore.saveUploadToken(uploadToken)
                                    }
                                    uploadToken = ""
                                }
                                val snapshot = appSettings.snapshot()
                                val savedToken = withContext(Dispatchers.IO) {
                                    credentialStore.readUploadToken()
                                } ?: error(uploadTokenRequiredMessage)
                                ReceiverClient().registerUploadDevice(snapshot, savedToken)
                                val healthy = ReceiverClient().checkHealth(receiverUrl)
                                statusMessage = if (healthy) connectionOnlineMessage
                                else connectionOfflineMessage
                            }.onFailure {
                                statusMessage = it.message ?: saveFailedMessage
                            }
                            saving = false
                        }
                    },
                    onClearToken = {
                        scope.launch(Dispatchers.IO) { credentialStore.clearUploadToken() }
                        statusMessage = tokenClearedMessage
                    }
                )
            }
            item {
                ControlPlaneCard(
                    controlPlaneUrl = controlPlaneUrl,
                    controlToken = controlToken,
                    tokenConfigured = controlTokenConfigured,
                    saving = saving,
                    onUrlChanged = { controlPlaneUrl = it },
                    onTokenChanged = { controlToken = it },
                    onSave = {
                        saving = true
                        statusMessage = registeringControlMessage
                        scope.launch {
                            runCatching {
                                appSettings.saveControlPlane(controlPlaneUrl)
                                if (controlToken.isNotBlank()) {
                                    withContext(Dispatchers.IO) {
                                        credentialStore.saveControlToken(controlToken)
                                    }
                                    controlToken = ""
                                }
                                val snapshot = appSettings.snapshot()
                                val token = withContext(Dispatchers.IO) {
                                    credentialStore.readControlToken()
                                } ?: error(controlTokenRequiredMessage)
                                ControlPlaneClient().registerDevice(snapshot, token)
                                BackupScheduler.scheduleControlPolling(context)
                                statusMessage = controlRegisteredMessage
                            }.onFailure {
                                statusMessage = it.message ?: saveFailedMessage
                            }
                            saving = false
                        }
                    },
                    onClearToken = {
                        scope.launch(Dispatchers.IO) { credentialStore.clearControlToken() }
                        statusMessage = controlTokenClearedMessage
                    }
                )
            }
            if (statusMessage.isNotBlank()) {
                item {
                    Text(statusMessage, color = MaterialTheme.colorScheme.primary)
                }
            }
            item {
                SourcePermissionsCard(
                    mediaPermissionGranted = mediaPermissionGranted,
                    exactAlarmGranted = exactAlarmGranted,
                    onRequestMediaPermission = onRequestMediaPermission,
                    onChooseDirectory = onChooseDirectory,
                    onExactAlarmSettings = onExactAlarmSettings
                )
            }
            settings?.treeUris?.takeIf { it.isNotEmpty() }?.let { treeUris ->
                item {
                    Text(
                        stringResource(R.string.authorized_directories),
                        style = MaterialTheme.typography.titleMedium
                    )
                }
                items(treeUris.sorted()) { rawUri ->
                    AuthorizedDirectory(rawUri, onRemoveDirectory)
                }
            }
            item {
                Button(
                    onClick = onBackupNow,
                    modifier = Modifier.fillMaxWidth(),
                    enabled = settings?.isConnectionConfigured == true && tokenConfigured
                ) {
                    Text(stringResource(R.string.backup_now))
                }
            }
        }
    }
}

@Composable
private fun ControlPlaneCard(
    controlPlaneUrl: String,
    controlToken: String,
    tokenConfigured: Boolean,
    saving: Boolean,
    onUrlChanged: (String) -> Unit,
    onTokenChanged: (String) -> Unit,
    onSave: () -> Unit,
    onClearToken: () -> Unit
) {
    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp)
        ) {
            Text(stringResource(R.string.phone_server), style = MaterialTheme.typography.titleMedium)
            Text(
                stringResource(R.string.phone_server_explanation),
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
            OutlinedTextField(
                value = controlPlaneUrl,
                onValueChange = onUrlChanged,
                modifier = Modifier.fillMaxWidth(),
                singleLine = true,
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri),
                label = { Text(stringResource(R.string.control_plane_url)) }
            )
            OutlinedTextField(
                value = controlToken,
                onValueChange = onTokenChanged,
                modifier = Modifier.fillMaxWidth(),
                singleLine = true,
                visualTransformation = PasswordVisualTransformation(),
                label = { Text(stringResource(R.string.control_token)) },
                supportingText = {
                    Text(
                        stringResource(
                            if (tokenConfigured) R.string.token_configured_hint
                            else R.string.control_token_required
                        )
                    )
                }
            )
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(12.dp)
            ) {
                Button(
                    onClick = onSave,
                    enabled = !saving && controlPlaneUrl.isNotBlank(),
                    modifier = Modifier.weight(1f)
                ) {
                    Text(stringResource(R.string.register_device))
                }
                if (tokenConfigured) {
                    OutlinedButton(onClick = onClearToken) {
                        Text(stringResource(R.string.clear_token))
                    }
                }
            }
        }
    }
}

@Composable
private fun QueueCard(summary: QueueSummary) {
    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp)
        ) {
            Text(stringResource(R.string.backup_queue), style = MaterialTheme.typography.titleMedium)
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween
            ) {
                QueueMetric(stringResource(R.string.waiting), summary.waiting)
                QueueMetric(stringResource(R.string.completed), summary.completed)
                QueueMetric(stringResource(R.string.failed), summary.failed)
            }
        }
    }
}

@Composable
private fun QueueMetric(label: String, value: Int) {
    Column {
        Text(value.toString(), style = MaterialTheme.typography.headlineSmall)
        Text(label, color = MaterialTheme.colorScheme.onSurfaceVariant)
    }
}

@Composable
private fun ConfigurationCard(
    deviceName: String,
    receiverUrl: String,
    uploadToken: String,
    tokenConfigured: Boolean,
    saving: Boolean,
    onDeviceNameChanged: (String) -> Unit,
    onReceiverUrlChanged: (String) -> Unit,
    onUploadTokenChanged: (String) -> Unit,
    onSave: () -> Unit,
    onClearToken: () -> Unit
) {
    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp)
        ) {
            Text(stringResource(R.string.pc_connection), style = MaterialTheme.typography.titleMedium)
            OutlinedTextField(
                value = deviceName,
                onValueChange = onDeviceNameChanged,
                modifier = Modifier.fillMaxWidth(),
                singleLine = true,
                label = { Text(stringResource(R.string.device_name)) }
            )
            OutlinedTextField(
                value = receiverUrl,
                onValueChange = onReceiverUrlChanged,
                modifier = Modifier.fillMaxWidth(),
                singleLine = true,
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri),
                label = { Text(stringResource(R.string.receiver_url)) },
                supportingText = { Text(stringResource(R.string.receiver_url_hint)) }
            )
            OutlinedTextField(
                value = uploadToken,
                onValueChange = onUploadTokenChanged,
                modifier = Modifier.fillMaxWidth(),
                singleLine = true,
                visualTransformation = PasswordVisualTransformation(),
                label = { Text(stringResource(R.string.upload_token)) },
                supportingText = {
                    Text(
                        stringResource(
                            if (tokenConfigured) R.string.token_configured_hint
                            else R.string.token_required_hint
                        )
                    )
                }
            )
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(12.dp)
            ) {
                Button(
                    onClick = onSave,
                    enabled = !saving && deviceName.isNotBlank() && receiverUrl.isNotBlank(),
                    modifier = Modifier.weight(1f)
                ) {
                    Text(stringResource(R.string.save_and_test))
                }
                if (tokenConfigured) {
                    OutlinedButton(onClick = onClearToken) {
                        Text(stringResource(R.string.clear_token))
                    }
                }
            }
        }
    }
}

@Composable
private fun SourcePermissionsCard(
    mediaPermissionGranted: Boolean,
    exactAlarmGranted: Boolean,
    onRequestMediaPermission: () -> Unit,
    onChooseDirectory: () -> Unit,
    onExactAlarmSettings: () -> Unit
) {
    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp)
        ) {
            Text(stringResource(R.string.sources_and_schedule), style = MaterialTheme.typography.titleMedium)
            PermissionRow(
                title = stringResource(R.string.photos_and_videos),
                granted = mediaPermissionGranted,
                buttonText = stringResource(R.string.grant_permission),
                onClick = onRequestMediaPermission
            )
            HorizontalDivider()
            Text(
                stringResource(R.string.directory_permission_explanation),
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
            OutlinedButton(onClick = onChooseDirectory, modifier = Modifier.fillMaxWidth()) {
                Text(stringResource(R.string.choose_directory))
            }
            HorizontalDivider()
            PermissionRow(
                title = stringResource(R.string.midnight_exact_alarm),
                granted = exactAlarmGranted,
                buttonText = stringResource(R.string.open_settings),
                onClick = onExactAlarmSettings
            )
        }
    }
}

@Composable
private fun PermissionRow(
    title: String,
    granted: Boolean,
    buttonText: String,
    onClick: () -> Unit
) {
    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.SpaceBetween
    ) {
        Column(modifier = Modifier.weight(1f)) {
            Text(title)
            Text(
                stringResource(if (granted) R.string.granted else R.string.not_granted),
                color = if (granted) MaterialTheme.colorScheme.primary
                else MaterialTheme.colorScheme.error
            )
        }
        if (!granted) TextButton(onClick = onClick) { Text(buttonText) }
    }
}

@Composable
private fun AuthorizedDirectory(rawUri: String, onRemove: (String) -> Unit) {
    Card(modifier = Modifier.fillMaxWidth()) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(12.dp),
            horizontalArrangement = Arrangement.SpaceBetween
        ) {
            Text(
                Uri.decode(rawUri).takeLast(80),
                modifier = Modifier.weight(1f),
                style = MaterialTheme.typography.bodySmall
            )
            TextButton(onClick = { onRemove(rawUri) }) {
                Text(stringResource(R.string.remove))
            }
        }
    }
}

private fun hasMediaPermission(context: Context): Boolean {
    return if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) {
        val fullAccess = ContextCompat.checkSelfPermission(context, Manifest.permission.READ_MEDIA_IMAGES) ==
            PackageManager.PERMISSION_GRANTED &&
            ContextCompat.checkSelfPermission(context, Manifest.permission.READ_MEDIA_VIDEO) ==
            PackageManager.PERMISSION_GRANTED
        val selectedAccess = ContextCompat.checkSelfPermission(
            context,
            Manifest.permission.READ_MEDIA_VISUAL_USER_SELECTED
        ) == PackageManager.PERMISSION_GRANTED
        fullAccess || selectedAccess
    } else if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
        ContextCompat.checkSelfPermission(context, Manifest.permission.READ_MEDIA_IMAGES) ==
            PackageManager.PERMISSION_GRANTED &&
            ContextCompat.checkSelfPermission(context, Manifest.permission.READ_MEDIA_VIDEO) ==
            PackageManager.PERMISSION_GRANTED
    } else {
        ContextCompat.checkSelfPermission(context, Manifest.permission.READ_EXTERNAL_STORAGE) ==
            PackageManager.PERMISSION_GRANTED
    }
}

private fun hasExactAlarmPermission(context: Context): Boolean {
    return Build.VERSION.SDK_INT < Build.VERSION_CODES.S ||
        context.getSystemService(AlarmManager::class.java).canScheduleExactAlarms()
}

private fun defaultDeviceName(): String {
    val manufacturer = Build.MANUFACTURER.replaceFirstChar {
        if (it.isLowerCase()) it.titlecase(Locale.getDefault()) else it.toString()
    }
    return "$manufacturer ${Build.MODEL}".trim()
}
