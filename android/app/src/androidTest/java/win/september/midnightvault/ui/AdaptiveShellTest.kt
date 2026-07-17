package win.september.midnightvault.ui

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.ui.Modifier
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.junit4.v2.createComposeRule
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import org.junit.Rule
import org.junit.Test
import win.september.midnightvault.config.BackupSettings
import win.september.midnightvault.data.QueueSummary
import win.september.midnightvault.ui.model.BackupUiState
import win.september.midnightvault.ui.model.MediaAccessState
import win.september.midnightvault.ui.adaptive.classifyWindowWidth
import win.september.midnightvault.ui.theme.MidnightVaultTheme

class AdaptiveShellTest {
    @get:Rule
    val composeRule = createComposeRule()

    @Test
    fun compactUsesBottomNavigationAndCanOpenSources() {
        setContent(width = 360)
        composeRule.onNodeWithTag("compact_navigation").assertIsDisplayed()
        composeRule.onNodeWithText("来源").performClick()
        composeRule.onNodeWithTag("sources_screen").assertIsDisplayed()
    }

    @Test
    fun mediumUsesNavigationRail() {
        setContent(width = 600)
        composeRule.onNodeWithTag("medium_navigation").assertIsDisplayed()
        composeRule.onNodeWithText("连接").performClick()
        composeRule.onNodeWithTag("connection_screen").assertIsDisplayed()
    }

    @Test
    fun expandedUsesPersistentSidebar() {
        setContent(width = 1000)
        composeRule.onNodeWithTag("expanded_navigation").assertIsDisplayed()
        composeRule.onNodeWithTag("overview_screen").assertIsDisplayed()
    }

    private fun setContent(width: Int) {
        composeRule.setContent {
            Box(modifier = Modifier.fillMaxSize()) {
                MidnightVaultTheme {
                    MidnightVaultApp(
                        uiState = TestState,
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
                        onClearControlToken = {},
                        windowSizeOverride = classifyWindowWidth(width)
                    )
                }
            }
        }
    }

    private companion object {
        val TestState = BackupUiState(
            settings = BackupSettings(
                deviceId = "test-device",
                deviceName = "测试手机",
                receiverUrl = "http://192.168.1.20:5210",
                controlPlaneUrl = "https://control.example.com",
                treeUris = emptySet(),
                initialBackupCompleted = false
            ),
            queueSummary = QueueSummary(1, 0, 3, 0),
            uploadTokenConfigured = true,
            mediaAccess = MediaAccessState.Full,
            notificationPermissionGranted = true,
            exactAlarmGranted = true
        )
    }
}
