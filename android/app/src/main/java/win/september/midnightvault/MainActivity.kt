package win.september.midnightvault

import android.Manifest
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.provider.Settings
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.contract.ActivityResultContracts
import androidx.activity.viewModels
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.lifecycleScope
import java.util.Locale
import kotlinx.coroutines.launch
import win.september.midnightvault.backup.BackupScheduler
import win.september.midnightvault.config.AppSettings
import win.september.midnightvault.ui.AppDestination
import win.september.midnightvault.ui.BackupHomeViewModel
import win.september.midnightvault.ui.MidnightVaultApp
import win.september.midnightvault.ui.theme.MidnightVaultTheme

class MainActivity : ComponentActivity() {
    private val appSettings by lazy { AppSettings(this) }
    private val homeViewModel by viewModels<BackupHomeViewModel>()

    private val notificationPermission = registerForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { homeViewModel.refresh() }

    private val mediaPermissions = registerForActivityResult(
        ActivityResultContracts.RequestMultiplePermissions()
    ) { homeViewModel.refresh() }

    private val directoryPicker = registerForActivityResult(
        ActivityResultContracts.OpenDocumentTree()
    ) { uri ->
        if (uri == null) return@registerForActivityResult
        val persisted = runCatching {
            contentResolver.takePersistableUriPermission(
                uri,
                Intent.FLAG_GRANT_READ_URI_PERMISSION
            )
        }.isSuccess
        if (!persisted) {
            Toast.makeText(this, R.string.directory_permission_failed, Toast.LENGTH_LONG).show()
            return@registerForActivityResult
        }
        lifecycleScope.launch {
            appSettings.addTreeUri(uri.toString())
            homeViewModel.refresh()
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        BackupScheduler.scheduleNext(this)
        BackupScheduler.scheduleControlPolling(this)
        processPairingIntent(intent)

        setContent {
            val uiState by homeViewModel.uiState.collectAsStateWithLifecycle()
            val initialDestination = remember {
                AppDestination.fromName(intent.getStringExtra("ui_destination"))
            }
            MidnightVaultTheme {
                MidnightVaultApp(
                    uiState = uiState,
                    initialDestination = initialDestination,
                    onRequestNotificationPermission = ::requestNotificationPermission,
                    onRequestMediaPermission = ::requestMediaPermissions,
                    onChooseDirectory = { directoryPicker.launch(null) },
                    onRemoveDirectory = ::removeDirectory,
                    onBackupNow = { BackupScheduler.enqueueNow(this) },
                    onExactAlarmSettings = ::openExactAlarmSettings,
                    onRefresh = homeViewModel::refresh,
                    onSaveConnection = homeViewModel::saveConnection,
                    onClearUploadToken = homeViewModel::clearUploadToken,
                    onSaveControlPlane = homeViewModel::saveControlPlane,
                    onClearControlToken = homeViewModel::clearControlToken,
                    onDeviceNameChanged = homeViewModel::updateDeviceName,
                    onReceiverUrlChanged = homeViewModel::updateReceiverUrl,
                    onUploadTokenChanged = homeViewModel::updateUploadToken,
                    onControlPlaneUrlChanged = homeViewModel::updateControlPlaneUrl,
                    onControlTokenChanged = homeViewModel::updateControlToken,
                    onConsumeOperationEvent = homeViewModel::consumeOperationEvent
                )
            }
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        processPairingIntent(intent)
    }

    private fun requestNotificationPermission() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            notificationPermission.launch(Manifest.permission.POST_NOTIFICATIONS)
        } else {
            homeViewModel.refresh()
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
        lifecycleScope.launch {
            appSettings.removeTreeUri(rawUri)
            homeViewModel.refresh()
        }
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
        // 先把事务交给跨配置存活的 ViewModel，再消费 Activity 的一次性深链。
        homeViewModel.claimPairing(receiverUrl, code, defaultDeviceName())
        intent.data = null
    }
}

private fun defaultDeviceName(): String {
    val manufacturer = Build.MANUFACTURER.replaceFirstChar {
        if (it.isLowerCase()) it.titlecase(Locale.getDefault()) else it.toString()
    }
    return "$manufacturer ${Build.MODEL}".trim()
}
