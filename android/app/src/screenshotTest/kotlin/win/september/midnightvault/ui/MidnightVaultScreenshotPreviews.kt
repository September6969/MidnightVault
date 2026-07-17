package win.september.midnightvault.ui

import android.content.res.Configuration
import androidx.compose.runtime.Composable
import androidx.compose.ui.tooling.preview.Preview
import com.android.tools.screenshot.PreviewTest
import win.september.midnightvault.config.BackupSettings
import win.september.midnightvault.data.QueueSummary
import win.september.midnightvault.ui.model.BackupUiState
import win.september.midnightvault.ui.model.MediaAccessState
import win.september.midnightvault.ui.theme.MidnightVaultTheme

@PreviewTest
@Preview(name = "360x800 Light", widthDp = 360, heightDp = 800, showBackground = true)
@Composable
fun CompactLightScreenshot() {
    ScreenshotApp()
}

@PreviewTest
@Preview(
    name = "412x915 Dark",
    widthDp = 412,
    heightDp = 915,
    uiMode = Configuration.UI_MODE_NIGHT_YES,
    showBackground = true
)
@Composable
fun CompactDarkScreenshot() {
    ScreenshotApp(darkTheme = true, destination = AppDestination.Connection)
}

@PreviewTest
@Preview(name = "600x960 Medium", widthDp = 600, heightDp = 960, showBackground = true)
@Composable
fun MediumScreenshot() {
    ScreenshotApp(destination = AppDestination.Sources)
}

@PreviewTest
@Preview(
    name = "800x1280 Large Text",
    widthDp = 800,
    heightDp = 1280,
    fontScale = 1.6f,
    showBackground = true
)
@Composable
fun TabletLargeTextScreenshot() {
    ScreenshotApp(destination = AppDestination.Connection)
}

@PreviewTest
@Preview(name = "1000x800 Expanded", widthDp = 1000, heightDp = 800, showBackground = true)
@Composable
fun ExpandedScreenshot() {
    ScreenshotApp(destination = AppDestination.Sources)
}

@PreviewTest
@Preview(name = "360x800 Loading", widthDp = 360, heightDp = 800, showBackground = true)
@Composable
fun CompactLoadingScreenshot() {
    ScreenshotApp(state = BackupUiState())
}

@PreviewTest
@Preview(name = "1000x800 Empty Sources", widthDp = 1000, heightDp = 800, showBackground = true)
@Composable
fun ExpandedEmptySourcesScreenshot() {
    ScreenshotApp(
        destination = AppDestination.Sources,
        state = ScreenshotState.copy(
            settings = ScreenshotState.settings?.copy(treeUris = emptySet()),
            mediaAccess = MediaAccessState.Partial,
            notificationPermissionGranted = false,
            exactAlarmGranted = false
        )
    )
}

@Composable
private fun ScreenshotApp(
    darkTheme: Boolean = false,
    destination: AppDestination = AppDestination.Overview,
    state: BackupUiState = ScreenshotState
) {
    MidnightVaultTheme(darkTheme = darkTheme) {
        MidnightVaultApp(
            uiState = state,
            initialDestination = destination,
            onRequestNotificationPermission = {},
            onRequestMediaPermission = {},
            onChooseDirectory = {},
            onRemoveDirectory = {},
            onBackupNow = {},
            onExactAlarmSettings = {},
            onRefresh = {},
            onSaveConnection = { _, _, _ -> },
            onClearUploadToken = {},
            onSaveControlPlane = { _, _ -> },
            onClearControlToken = {}
        )
    }
}

private val ScreenshotState = BackupUiState(
    settings = BackupSettings(
        deviceId = "visual-test-device",
        deviceName = "Pixel 视觉测试机",
        receiverUrl = "http://192.168.1.20:5210",
        controlPlaneUrl = "https://control.example.com",
        treeUris = setOf("content://com.android.externalstorage.documents/tree/primary%3ADocuments"),
        initialBackupCompleted = false
    ),
    queueSummary = QueueSummary(3, 1, 128, 0),
    uploadTokenConfigured = true,
    controlTokenConfigured = true,
    mediaAccess = MediaAccessState.Full,
    notificationPermissionGranted = true,
    exactAlarmGranted = true
)
