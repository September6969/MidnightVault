package win.september.midnightvault.ui

import android.content.res.Configuration
import android.net.Uri
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.Image
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.GridItemSpan
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.Backup
import androidx.compose.material.icons.rounded.Devices
import androidx.compose.material.icons.rounded.Folder
import androidx.compose.material.icons.rounded.Home
import androidx.compose.material.icons.rounded.Info
import androidx.compose.material.icons.rounded.Notifications
import androidx.compose.material.icons.rounded.PhotoLibrary
import androidx.compose.material.icons.rounded.Refresh
import androidx.compose.material.icons.rounded.Schedule
import androidx.compose.material.icons.rounded.Security
import androidx.compose.material.icons.rounded.Storage
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.NavigationDrawerItem
import androidx.compose.material3.NavigationRail
import androidx.compose.material3.NavigationRailItem
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import win.september.midnightvault.R
import win.september.midnightvault.config.BackupSettings
import win.september.midnightvault.data.QueueSummary
import win.september.midnightvault.ui.adaptive.VaultWindowSize
import win.september.midnightvault.ui.adaptive.classifyWindowWidth
import win.september.midnightvault.ui.components.VaultEmptyState
import win.september.midnightvault.ui.components.VaultLoadingState
import win.september.midnightvault.ui.components.VaultMetric
import win.september.midnightvault.ui.components.VaultPageHeader
import win.september.midnightvault.ui.components.VaultSectionCard
import win.september.midnightvault.ui.components.VaultStatusBadge
import win.september.midnightvault.ui.components.vaultTouchTarget
import win.september.midnightvault.ui.model.BackupUiState
import win.september.midnightvault.ui.model.BackupOperationKind
import win.september.midnightvault.ui.model.MediaAccessState
import win.september.midnightvault.ui.theme.MidnightVaultTheme
import win.september.midnightvault.ui.theme.VaultSize
import win.september.midnightvault.ui.theme.VaultSpacing

enum class AppDestination(
    val route: String,
    val labelRes: Int,
    val icon: ImageVector
) {
    Overview("overview", R.string.navigation_overview, Icons.Rounded.Home),
    Connection("connection", R.string.navigation_connection, Icons.Rounded.Devices),
    Sources("sources", R.string.navigation_sources, Icons.Rounded.Folder);

    companion object {
        fun fromName(name: String?): AppDestination =
            entries.firstOrNull { it.route.equals(name, ignoreCase = true) } ?: Overview
    }
}

private sealed interface ConfirmAction {
    data object ClearUploadToken : ConfirmAction
    data object ClearControlToken : ConfirmAction
    data class RemoveDirectory(val uri: String) : ConfirmAction
}

@Composable
@OptIn(ExperimentalMaterial3Api::class)
fun MidnightVaultApp(
    uiState: BackupUiState,
    initialDestination: AppDestination = AppDestination.Overview,
    onRequestNotificationPermission: () -> Unit,
    onRequestMediaPermission: () -> Unit,
    onChooseDirectory: () -> Unit,
    onRemoveDirectory: (String) -> Unit,
    onBackupNow: () -> Unit,
    onExactAlarmSettings: () -> Unit,
    onRefresh: () -> Unit,
    onSaveConnection: (String, String, String) -> Unit,
    onClearUploadToken: () -> Unit,
    onSaveControlPlane: (String, String) -> Unit,
    onClearControlToken: () -> Unit,
    onDeviceNameChanged: (String) -> Unit = {},
    onReceiverUrlChanged: (String) -> Unit = {},
    onUploadTokenChanged: (String) -> Unit = {},
    onControlPlaneUrlChanged: (String) -> Unit = {},
    onControlTokenChanged: (String) -> Unit = {},
    onConsumeOperationEvent: (Long) -> Unit = {},
    windowSizeOverride: VaultWindowSize? = null
) {
    var destinationRoute by rememberSaveable { mutableStateOf(initialDestination.route) }
    val destination = AppDestination.fromName(destinationRoute)
    val snackbarHostState = remember { SnackbarHostState() }
    var statusMessage by remember { mutableStateOf<String?>(null) }
    var showInfoSheet by rememberSaveable { mutableStateOf(false) }
    var confirmAction by remember { mutableStateOf<ConfirmAction?>(null) }

    val deviceName = uiState.connectionDraft.deviceName
        ?: uiState.settings?.deviceName.orEmpty()
    val receiverUrl = uiState.connectionDraft.receiverUrl
        ?: uiState.settings?.receiverUrl.orEmpty()
    val controlPlaneUrl = uiState.connectionDraft.controlPlaneUrl
        ?: uiState.settings?.controlPlaneUrl.orEmpty()
    val uploadToken = uiState.connectionDraft.uploadToken
    val controlToken = uiState.connectionDraft.controlToken

    val backupStarted = stringResource(R.string.backup_started)
    val connectionOnline = stringResource(R.string.connection_saved_online)
    val connectionOffline = stringResource(R.string.connection_saved_offline)
    val saveFailed = stringResource(R.string.save_failed)
    val tokenCleared = stringResource(R.string.token_cleared)
    val controlRegistered = stringResource(R.string.control_plane_registered)
    val controlTokenCleared = stringResource(R.string.control_token_cleared)
    val pairingSucceeded = stringResource(R.string.pairing_succeeded)
    val pairingInProgress = stringResource(R.string.pairing_in_progress)
    val operationEvent = uiState.operation.event
    val operationMessage = when (operationEvent?.kind) {
        BackupOperationKind.ConnectionOnline -> connectionOnline
        BackupOperationKind.ConnectionOffline -> connectionOffline
        BackupOperationKind.ConnectionFailed,
        BackupOperationKind.ControlFailed -> operationEvent.detail ?: saveFailed
        BackupOperationKind.ControlRegistered -> controlRegistered
        BackupOperationKind.UploadTokenCleared -> tokenCleared
        BackupOperationKind.ControlTokenCleared -> controlTokenCleared
        BackupOperationKind.PairingSucceeded -> pairingSucceeded
        BackupOperationKind.PairingFailed -> stringResource(
            R.string.pairing_failed,
            operationEvent.detail.orEmpty()
        )
        null -> null
    }
    LaunchedEffect(operationEvent?.id, operationMessage) {
        val event = operationEvent ?: return@LaunchedEffect
        val message = operationMessage ?: return@LaunchedEffect
        snackbarHostState.showSnackbar(message)
        onConsumeOperationEvent(event.id)
    }
    LaunchedEffect(statusMessage) {
        val message = statusMessage ?: return@LaunchedEffect
        snackbarHostState.showSnackbar(message)
        statusMessage = null
    }

    BackHandler(enabled = destination != AppDestination.Overview) {
        destinationRoute = AppDestination.Overview.route
    }

    BoxWithConstraints(modifier = Modifier.fillMaxSize()) {
        val windowSize = windowSizeOverride ?: run {
            val classified = classifyWindowWidth(maxWidth.value.toInt())
            if (maxHeight < 480.dp && classified == VaultWindowSize.Expanded) {
                VaultWindowSize.Medium
            } else {
                classified
            }
        }

        Scaffold(
            modifier = Modifier.testTag("vault_app_shell"),
            contentWindowInsets = WindowInsets.safeDrawing,
            topBar = {
                Column {
                    TopAppBar(
                        title = {
                            Text(stringResource(R.string.app_name))
                        },
                        actions = {
                            IconButton(onClick = onRefresh) {
                                Icon(
                                    imageVector = Icons.Rounded.Refresh,
                                    contentDescription = stringResource(R.string.refresh_status)
                                )
                            }
                            IconButton(onClick = { showInfoSheet = true }) {
                                Icon(
                                    imageVector = Icons.Rounded.Info,
                                    contentDescription = stringResource(R.string.more_information)
                                )
                            }
                        }
                    )
                    if (uiState.operation.pairing) {
                        LinearProgressIndicator(
                            modifier = Modifier
                                .fillMaxWidth()
                                .semantics {
                                    liveRegion = LiveRegionMode.Polite
                                    contentDescription = pairingInProgress
                                    stateDescription = pairingInProgress
                                }
                        )
                    }
                }
            },
            bottomBar = {
                if (windowSize == VaultWindowSize.Compact) {
                    CompactNavigation(
                        selected = destination,
                        onSelected = { destinationRoute = it.route }
                    )
                }
            },
            snackbarHost = { SnackbarHost(snackbarHostState) }
        ) { scaffoldPadding ->
            Row(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(scaffoldPadding)
                    .imePadding()
            ) {
                if (windowSize == VaultWindowSize.Medium) {
                    MediumNavigation(
                        selected = destination,
                        onSelected = { destinationRoute = it.route }
                    )
                } else if (windowSize == VaultWindowSize.Expanded) {
                    ExpandedNavigation(
                        selected = destination,
                        onSelected = { destinationRoute = it.route }
                    )
                }

                Box(
                    modifier = Modifier
                        .weight(1f)
                        .fillMaxHeight()
                ) {
                    Box(
                        modifier = Modifier
                            .fillMaxWidth()
                            .widthIn(max = VaultSize.ContentMaxWidth)
                            .align(Alignment.TopCenter)
                    ) {
                        when (destination) {
                            AppDestination.Overview -> OverviewScreen(
                                uiState = uiState,
                                windowSize = windowSize,
                                onBackupNow = {
                                    onBackupNow()
                                    statusMessage = backupStarted
                                },
                                onShowInfo = { showInfoSheet = true },
                                onOpenConnection = {
                                    destinationRoute = AppDestination.Connection.route
                                }
                            )

                            AppDestination.Connection -> ConnectionScreen(
                                windowSize = windowSize,
                                deviceName = deviceName,
                                receiverUrl = receiverUrl,
                                uploadToken = uploadToken,
                                uploadTokenConfigured = uiState.uploadTokenConfigured,
                                controlPlaneUrl = controlPlaneUrl,
                                controlToken = controlToken,
                                controlTokenConfigured = uiState.controlTokenConfigured,
                                savingConnection = uiState.operation.savingConnection,
                                savingControlPlane = uiState.operation.savingControlPlane,
                                onDeviceNameChanged = onDeviceNameChanged,
                                onReceiverUrlChanged = onReceiverUrlChanged,
                                onUploadTokenChanged = onUploadTokenChanged,
                                onControlPlaneUrlChanged = onControlPlaneUrlChanged,
                                onControlTokenChanged = onControlTokenChanged,
                                onSaveConnection = {
                                    onSaveConnection(deviceName, receiverUrl, uploadToken)
                                },
                                onSaveControlPlane = {
                                    onSaveControlPlane(controlPlaneUrl, controlToken)
                                },
                                onClearUploadToken = {
                                    confirmAction = ConfirmAction.ClearUploadToken
                                },
                                onClearControlToken = {
                                    confirmAction = ConfirmAction.ClearControlToken
                                }
                            )

                            AppDestination.Sources -> SourcesScreen(
                                uiState = uiState,
                                windowSize = windowSize,
                                onRequestNotificationPermission = onRequestNotificationPermission,
                                onRequestMediaPermission = onRequestMediaPermission,
                                onChooseDirectory = onChooseDirectory,
                                onExactAlarmSettings = onExactAlarmSettings,
                                onRemoveDirectory = {
                                    confirmAction = ConfirmAction.RemoveDirectory(it)
                                }
                            )
                        }
                    }
                }
            }
        }
    }

    if (showInfoSheet) {
        ModalBottomSheet(onDismissRequest = { showInfoSheet = false }) {
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = VaultSpacing.ExtraLarge)
                    .padding(bottom = VaultSpacing.ExtraExtraLarge),
                verticalArrangement = Arrangement.spacedBy(VaultSpacing.Medium)
            ) {
                Text(
                    text = stringResource(R.string.backup_principles),
                    style = MaterialTheme.typography.titleLarge,
                    modifier = Modifier.semantics { heading() }
                )
                Text(
                    text = stringResource(R.string.backup_principles_detail),
                    style = MaterialTheme.typography.bodyLarge
                )
                Button(
                    onClick = { showInfoSheet = false },
                    modifier = Modifier
                        .fillMaxWidth()
                        .vaultTouchTarget()
                ) {
                    Text(stringResource(R.string.close))
                }
            }
        }
    }

    confirmAction?.let { action ->
        val title = when (action) {
            ConfirmAction.ClearUploadToken -> R.string.confirm_clear_token_title
            ConfirmAction.ClearControlToken -> R.string.confirm_clear_control_token_title
            is ConfirmAction.RemoveDirectory -> R.string.confirm_remove_directory_title
        }
        val message = when (action) {
            ConfirmAction.ClearUploadToken,
            ConfirmAction.ClearControlToken -> R.string.confirm_clear_token_message
            is ConfirmAction.RemoveDirectory -> R.string.confirm_remove_directory_message
        }
        AlertDialog(
            onDismissRequest = { confirmAction = null },
            title = { Text(stringResource(title)) },
            text = { Text(stringResource(message)) },
            confirmButton = {
                TextButton(onClick = {
                    confirmAction = null
                    when (action) {
                        ConfirmAction.ClearUploadToken -> onClearUploadToken()
                        ConfirmAction.ClearControlToken -> onClearControlToken()
                        is ConfirmAction.RemoveDirectory -> {
                            onRemoveDirectory(action.uri)
                        }
                    }
                }) {
                    Text(
                        stringResource(
                            if (action is ConfirmAction.RemoveDirectory) {
                                R.string.confirm_remove
                            } else {
                                R.string.confirm_clear
                            }
                        )
                    )
                }
            },
            dismissButton = {
                TextButton(onClick = { confirmAction = null }) {
                    Text(stringResource(R.string.cancel))
                }
            }
        )
    }
}

@Composable
private fun CompactNavigation(
    selected: AppDestination,
    onSelected: (AppDestination) -> Unit
) {
    val description = stringResource(R.string.navigation_compact_description)
    NavigationBar(
        modifier = Modifier
            .testTag("compact_navigation")
            .semantics { contentDescription = description }
    ) {
        AppDestination.entries.forEach { destination ->
            NavigationBarItem(
                selected = destination == selected,
                onClick = { onSelected(destination) },
                icon = {
                    Icon(destination.icon, contentDescription = null)
                },
                label = { Text(stringResource(destination.labelRes)) }
            )
        }
    }
}

@Composable
private fun MediumNavigation(
    selected: AppDestination,
    onSelected: (AppDestination) -> Unit
) {
    val description = stringResource(R.string.navigation_medium_description)
    NavigationRail(
        modifier = Modifier
            .fillMaxHeight()
            .testTag("medium_navigation")
            .semantics { contentDescription = description }
    ) {
        AppDestination.entries.forEach { destination ->
            NavigationRailItem(
                selected = destination == selected,
                onClick = { onSelected(destination) },
                icon = { Icon(destination.icon, contentDescription = null) },
                label = { Text(stringResource(destination.labelRes)) }
            )
        }
    }
}

@Composable
private fun ExpandedNavigation(
    selected: AppDestination,
    onSelected: (AppDestination) -> Unit
) {
    Surface(
        modifier = Modifier
            .width(VaultSize.ExpandedSidebarWidth)
            .fillMaxHeight()
            .testTag("expanded_navigation"),
        color = MaterialTheme.colorScheme.surface
    ) {
        Column(
            modifier = Modifier.padding(VaultSpacing.Medium),
            verticalArrangement = Arrangement.spacedBy(VaultSpacing.Small)
        ) {
            Text(
                text = stringResource(R.string.app_name),
                style = MaterialTheme.typography.titleMedium,
                modifier = Modifier.padding(VaultSpacing.Medium)
            )
            AppDestination.entries.forEach { destination ->
                NavigationDrawerItem(
                    selected = destination == selected,
                    onClick = { onSelected(destination) },
                    icon = { Icon(destination.icon, contentDescription = null) },
                    label = { Text(stringResource(destination.labelRes)) }
                )
            }
        }
    }
}

@Composable
private fun OverviewScreen(
    uiState: BackupUiState,
    windowSize: VaultWindowSize,
    onBackupNow: () -> Unit,
    onShowInfo: () -> Unit,
    onOpenConnection: () -> Unit
) {
    val pagePadding = if (windowSize == VaultWindowSize.Expanded) {
        VaultSize.ExpandedPagePadding
    } else {
        VaultSize.CompactPagePadding
    }
    LazyColumn(
        modifier = Modifier
            .fillMaxSize()
            .testTag("overview_screen"),
        contentPadding = PaddingValues(pagePadding),
        verticalArrangement = Arrangement.spacedBy(VaultSpacing.Large)
    ) {
        item {
            VaultPageHeader(
                title = stringResource(R.string.overview_title),
                subtitle = stringResource(R.string.overview_summary)
            )
        }
        if (uiState.isLoading) {
            item { VaultLoadingState() }
        } else {
            uiState.errorMessage?.let { error ->
                item {
                    VaultSectionCard {
                        Text(
                            text = error,
                            color = MaterialTheme.colorScheme.error,
                            style = MaterialTheme.typography.bodyLarge
                        )
                    }
                }
            }
            item {
                BackupHero(
                    canBackup = uiState.canBackupNow,
                    compact = windowSize == VaultWindowSize.Compact,
                    onBackupNow = onBackupNow,
                    onOpenConnection = onOpenConnection
                )
            }
            item { QueueSummaryCard(uiState.queueSummary) }
            item { ReadinessCard(uiState) }
            item {
                TextButton(
                    onClick = onShowInfo,
                    modifier = Modifier.vaultTouchTarget()
                ) {
                    Icon(Icons.Rounded.Info, contentDescription = null)
                    Text(
                        text = stringResource(R.string.more_information),
                        modifier = Modifier.padding(start = VaultSpacing.Small)
                    )
                }
            }
        }
    }
}

@Composable
private fun BackupHero(
    canBackup: Boolean,
    compact: Boolean,
    onBackupNow: () -> Unit,
    onOpenConnection: () -> Unit
) {
    VaultSectionCard(accent = true) {
        if (compact) {
            Column(
                modifier = Modifier.fillMaxWidth(),
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.spacedBy(VaultSpacing.Medium)
            ) {
                BackupHeroIllustration(124.dp)
                BackupHeroText(canBackup, onBackupNow, onOpenConnection)
            }
        } else {
            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(VaultSpacing.ExtraLarge)
            ) {
                BackupHeroIllustration(VaultSize.HeroIllustration)
                Box(modifier = Modifier.weight(1f)) {
                    BackupHeroText(canBackup, onBackupNow, onOpenConnection)
                }
            }
        }
    }
}

@Composable
private fun BackupHeroIllustration(size: androidx.compose.ui.unit.Dp) {
    Image(
        painter = painterResource(R.drawable.vault_backup_hero),
        contentDescription = stringResource(R.string.illustration_backup_description),
        modifier = Modifier.size(size)
    )
}

@Composable
private fun BackupHeroText(
    canBackup: Boolean,
    onBackupNow: () -> Unit,
    onOpenConnection: () -> Unit
) {
    Column(
        modifier = Modifier.fillMaxWidth(),
        verticalArrangement = Arrangement.spacedBy(VaultSpacing.Medium)
    ) {
        Text(
            text = stringResource(R.string.next_backup),
            style = MaterialTheme.typography.titleMedium
        )
        Text(
            text = stringResource(R.string.next_backup_value),
            style = MaterialTheme.typography.headlineMedium
        )
        Text(
            text = stringResource(R.string.backup_action_hint),
            color = MaterialTheme.colorScheme.onPrimaryContainer
        )
        Button(
            onClick = if (canBackup) onBackupNow else onOpenConnection,
            modifier = Modifier
                .fillMaxWidth()
                .vaultTouchTarget()
        ) {
            Icon(
                imageVector = if (canBackup) Icons.Rounded.Backup else Icons.Rounded.Devices,
                contentDescription = null
            )
            Text(
                text = stringResource(
                    if (canBackup) R.string.backup_now else R.string.navigation_connection
                ),
                modifier = Modifier.padding(start = VaultSpacing.Small)
            )
        }
    }
}

@Composable
private fun QueueSummaryCard(summary: QueueSummary) {
    VaultSectionCard {
        Text(
            text = stringResource(R.string.backup_queue),
            style = MaterialTheme.typography.titleMedium,
            modifier = Modifier.semantics { heading() }
        )
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(VaultSpacing.Medium)
        ) {
            QueueMetricAccessible(
                label = stringResource(R.string.waiting),
                value = summary.waiting,
                modifier = Modifier.weight(1f)
            )
            QueueMetricAccessible(
                label = stringResource(R.string.completed),
                value = summary.completed,
                modifier = Modifier.weight(1f)
            )
            QueueMetricAccessible(
                label = stringResource(R.string.failed),
                value = summary.failed,
                modifier = Modifier.weight(1f)
            )
        }
    }
}

@Composable
private fun QueueMetricAccessible(label: String, value: Int, modifier: Modifier = Modifier) {
    val description = "$label $value"
    Box(modifier = modifier.semantics(mergeDescendants = true) {
        contentDescription = description
    }) {
        VaultMetric(label = label, value = value.toString())
    }
}

@Composable
private fun ReadinessCard(uiState: BackupUiState) {
    val ready = uiState.canBackupNow
    VaultSectionCard {
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.SpaceBetween
        ) {
            Text(
                text = if (ready) {
                    stringResource(R.string.receiver_configured)
                } else {
                    stringResource(R.string.receiver_not_configured)
                },
                style = MaterialTheme.typography.titleMedium,
                modifier = Modifier.weight(1f)
            )
            VaultStatusBadge(
                text = stringResource(
                    if (ready) R.string.backup_ready else R.string.backup_not_ready
                ),
                positive = ready
            )
        }
        HorizontalDivider()
        ReadinessLine(
            icon = Icons.Rounded.PhotoLibrary,
            label = stringResource(R.string.photos_and_videos),
            ready = uiState.mediaAccess != MediaAccessState.None
        )
        ReadinessLine(
            icon = Icons.Rounded.Notifications,
            label = stringResource(R.string.notification_permission),
            ready = uiState.notificationPermissionGranted
        )
        ReadinessLine(
            icon = Icons.Rounded.Schedule,
            label = stringResource(R.string.midnight_exact_alarm),
            ready = uiState.exactAlarmGranted
        )
    }
}

@Composable
private fun ReadinessLine(icon: ImageVector, label: String, ready: Boolean) {
    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(VaultSpacing.Medium)
    ) {
        Icon(icon, contentDescription = null, tint = MaterialTheme.colorScheme.primary)
        Text(text = label, modifier = Modifier.weight(1f))
        Text(
            text = stringResource(
                if (ready) R.string.permission_status_granted
                else R.string.permission_status_missing
            ),
            color = if (ready) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.error,
            style = MaterialTheme.typography.labelLarge
        )
    }
}

@Composable
private fun ConnectionScreen(
    windowSize: VaultWindowSize,
    deviceName: String,
    receiverUrl: String,
    uploadToken: String,
    uploadTokenConfigured: Boolean,
    controlPlaneUrl: String,
    controlToken: String,
    controlTokenConfigured: Boolean,
    savingConnection: Boolean,
    savingControlPlane: Boolean,
    onDeviceNameChanged: (String) -> Unit,
    onReceiverUrlChanged: (String) -> Unit,
    onUploadTokenChanged: (String) -> Unit,
    onControlPlaneUrlChanged: (String) -> Unit,
    onControlTokenChanged: (String) -> Unit,
    onSaveConnection: () -> Unit,
    onSaveControlPlane: () -> Unit,
    onClearUploadToken: () -> Unit,
    onClearControlToken: () -> Unit
) {
    val columns = if (windowSize == VaultWindowSize.Expanded) 2 else 1
    val pagePadding = if (windowSize == VaultWindowSize.Expanded) {
        VaultSize.ExpandedPagePadding
    } else {
        VaultSize.CompactPagePadding
    }
    LazyVerticalGrid(
        columns = GridCells.Fixed(columns),
        modifier = Modifier
            .fillMaxSize()
            .testTag("connection_screen"),
        contentPadding = PaddingValues(pagePadding),
        horizontalArrangement = Arrangement.spacedBy(VaultSpacing.Large),
        verticalArrangement = Arrangement.spacedBy(VaultSpacing.Large)
    ) {
        item(span = { GridItemSpan(maxLineSpan) }) {
            VaultPageHeader(
                title = stringResource(R.string.connection_title),
                subtitle = stringResource(R.string.connection_summary)
            )
        }
        item(span = { GridItemSpan(maxLineSpan) }) {
            VaultSectionCard(accent = true) {
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(VaultSpacing.Medium)
                ) {
                    Icon(Icons.Rounded.Security, contentDescription = null)
                    Text(
                        text = stringResource(R.string.connection_security_hint),
                        modifier = Modifier.weight(1f)
                    )
                }
            }
        }
        item {
            ReceiverConfigurationCard(
                deviceName = deviceName,
                receiverUrl = receiverUrl,
                uploadToken = uploadToken,
                tokenConfigured = uploadTokenConfigured,
                saving = savingConnection,
                onDeviceNameChanged = onDeviceNameChanged,
                onReceiverUrlChanged = onReceiverUrlChanged,
                onUploadTokenChanged = onUploadTokenChanged,
                onSave = onSaveConnection,
                onClearToken = onClearUploadToken
            )
        }
        item {
            ControlPlaneConfigurationCard(
                controlPlaneUrl = controlPlaneUrl,
                controlToken = controlToken,
                tokenConfigured = controlTokenConfigured,
                saving = savingControlPlane,
                onUrlChanged = onControlPlaneUrlChanged,
                onTokenChanged = onControlTokenChanged,
                onSave = onSaveControlPlane,
                onClearToken = onClearControlToken
            )
        }
    }
}

@Composable
private fun ReceiverConfigurationCard(
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
    VaultSectionCard {
        SectionTitle(Icons.Rounded.Storage, stringResource(R.string.connection_section_receiver))
        OutlinedTextField(
            value = deviceName,
            onValueChange = onDeviceNameChanged,
            modifier = Modifier.fillMaxWidth(),
            singleLine = true,
            label = { Text(stringResource(R.string.device_name)) },
            keyboardOptions = KeyboardOptions(imeAction = ImeAction.Next)
        )
        OutlinedTextField(
            value = receiverUrl,
            onValueChange = onReceiverUrlChanged,
            modifier = Modifier.fillMaxWidth(),
            singleLine = true,
            label = { Text(stringResource(R.string.receiver_url)) },
            supportingText = { Text(stringResource(R.string.receiver_url_hint)) },
            keyboardOptions = KeyboardOptions(
                keyboardType = KeyboardType.Uri,
                imeAction = ImeAction.Next
            )
        )
        OutlinedTextField(
            value = uploadToken,
            onValueChange = onUploadTokenChanged,
            modifier = Modifier.fillMaxWidth(),
            singleLine = true,
            label = { Text(stringResource(R.string.upload_token)) },
            supportingText = {
                Text(
                    stringResource(
                        if (tokenConfigured) R.string.token_configured_hint
                        else R.string.token_required_hint
                    )
                )
            },
            visualTransformation = PasswordVisualTransformation(),
            keyboardOptions = KeyboardOptions(
                keyboardType = KeyboardType.Password,
                imeAction = ImeAction.Done
            )
        )
        Button(
            onClick = onSave,
            enabled = !saving && deviceName.isNotBlank() && receiverUrl.isNotBlank(),
            modifier = Modifier
                .fillMaxWidth()
                .vaultTouchTarget()
        ) {
            Text(
                stringResource(
                    if (saving) R.string.checking_connection else R.string.save_and_test
                )
            )
        }
        if (tokenConfigured) {
            TextButton(
                onClick = onClearToken,
                enabled = !saving,
                modifier = Modifier
                    .fillMaxWidth()
                    .vaultTouchTarget()
            ) {
                Text(stringResource(R.string.clear_token))
            }
        }
    }
}

@Composable
private fun ControlPlaneConfigurationCard(
    controlPlaneUrl: String,
    controlToken: String,
    tokenConfigured: Boolean,
    saving: Boolean,
    onUrlChanged: (String) -> Unit,
    onTokenChanged: (String) -> Unit,
    onSave: () -> Unit,
    onClearToken: () -> Unit
) {
    VaultSectionCard {
        SectionTitle(Icons.Rounded.Devices, stringResource(R.string.connection_section_control))
        Text(
            text = stringResource(R.string.phone_server_explanation),
            color = MaterialTheme.colorScheme.onSurfaceVariant
        )
        OutlinedTextField(
            value = controlPlaneUrl,
            onValueChange = onUrlChanged,
            modifier = Modifier.fillMaxWidth(),
            singleLine = true,
            label = { Text(stringResource(R.string.control_plane_url)) },
            keyboardOptions = KeyboardOptions(
                keyboardType = KeyboardType.Uri,
                imeAction = ImeAction.Next
            )
        )
        OutlinedTextField(
            value = controlToken,
            onValueChange = onTokenChanged,
            modifier = Modifier.fillMaxWidth(),
            singleLine = true,
            label = { Text(stringResource(R.string.control_token)) },
            supportingText = {
                Text(
                    stringResource(
                        if (tokenConfigured) R.string.token_configured_hint
                        else R.string.control_token_required
                    )
                )
            },
            visualTransformation = PasswordVisualTransformation(),
            keyboardOptions = KeyboardOptions(
                keyboardType = KeyboardType.Password,
                imeAction = ImeAction.Done
            )
        )
        Button(
            onClick = onSave,
            enabled = !saving && controlPlaneUrl.isNotBlank(),
            modifier = Modifier
                .fillMaxWidth()
                .vaultTouchTarget()
        ) {
            Text(
                stringResource(
                    if (saving) R.string.registering_control_plane else R.string.register_device
                )
            )
        }
        if (tokenConfigured) {
            TextButton(
                onClick = onClearToken,
                enabled = !saving,
                modifier = Modifier
                    .fillMaxWidth()
                    .vaultTouchTarget()
            ) {
                Text(stringResource(R.string.clear_token))
            }
        }
    }
}

@Composable
private fun SectionTitle(icon: ImageVector, title: String) {
    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(VaultSpacing.Medium)
    ) {
        Icon(icon, contentDescription = null, tint = MaterialTheme.colorScheme.primary)
        Text(
            text = title,
            style = MaterialTheme.typography.titleMedium,
            modifier = Modifier.semantics { heading() }
        )
    }
}

@Composable
private fun SourcesScreen(
    uiState: BackupUiState,
    windowSize: VaultWindowSize,
    onRequestNotificationPermission: () -> Unit,
    onRequestMediaPermission: () -> Unit,
    onChooseDirectory: () -> Unit,
    onExactAlarmSettings: () -> Unit,
    onRemoveDirectory: (String) -> Unit
) {
    val directories = uiState.settings?.treeUris.orEmpty().sorted()
    val expanded = windowSize == VaultWindowSize.Expanded
    LazyVerticalGrid(
        columns = GridCells.Fixed(if (expanded) 2 else 1),
        modifier = Modifier
            .fillMaxSize()
            .testTag("sources_screen"),
        contentPadding = PaddingValues(
            if (expanded) VaultSize.ExpandedPagePadding else VaultSize.CompactPagePadding
        ),
        verticalArrangement = Arrangement.spacedBy(VaultSpacing.Large),
        horizontalArrangement = Arrangement.spacedBy(VaultSpacing.Large)
    ) {
        item(span = { GridItemSpan(maxLineSpan) }) {
            VaultPageHeader(
                title = stringResource(R.string.sources_title),
                subtitle = stringResource(R.string.sources_summary)
            )
        }
        item(span = { GridItemSpan(maxLineSpan) }) {
            VaultSectionCard(accent = true) {
                Text(stringResource(R.string.permission_help))
            }
        }
        item {
            VaultSectionCard {
                SectionTitle(Icons.Rounded.Security, stringResource(R.string.sources_and_schedule))
                PermissionItem(
                    icon = Icons.Rounded.PhotoLibrary,
                    title = stringResource(R.string.photos_and_videos),
                    status = when (uiState.mediaAccess) {
                        MediaAccessState.None -> stringResource(R.string.not_granted)
                        MediaAccessState.Partial -> stringResource(R.string.permission_status_partial)
                        MediaAccessState.Full -> stringResource(R.string.granted)
                    },
                    granted = uiState.mediaAccess != MediaAccessState.None,
                    showAction = uiState.mediaAccess != MediaAccessState.Full,
                    buttonText = stringResource(
                        if (uiState.mediaAccess == MediaAccessState.Partial) {
                            R.string.manage_media_selection
                        } else {
                            R.string.grant_permission
                        }
                    ),
                    onClick = onRequestMediaPermission
                )
                HorizontalDivider()
                PermissionItem(
                    icon = Icons.Rounded.Notifications,
                    title = stringResource(R.string.notification_permission),
                    status = stringResource(
                        if (uiState.notificationPermissionGranted) R.string.granted
                        else R.string.not_granted
                    ),
                    granted = uiState.notificationPermissionGranted,
                    buttonText = stringResource(R.string.grant_permission),
                    onClick = onRequestNotificationPermission
                )
                HorizontalDivider()
                PermissionItem(
                    icon = Icons.Rounded.Schedule,
                    title = stringResource(R.string.midnight_exact_alarm),
                    status = stringResource(
                        if (uiState.exactAlarmGranted) R.string.granted
                        else R.string.not_granted
                    ),
                    granted = uiState.exactAlarmGranted,
                    buttonText = stringResource(R.string.open_settings),
                    onClick = onExactAlarmSettings
                )
            }
        }
        item {
            Column(verticalArrangement = Arrangement.spacedBy(VaultSpacing.Large)) {
                OutlinedButton(
                    onClick = onChooseDirectory,
                    modifier = Modifier
                        .fillMaxWidth()
                        .vaultTouchTarget()
                ) {
                    Icon(Icons.Rounded.Folder, contentDescription = null)
                    Text(
                        text = stringResource(R.string.choose_directory),
                        modifier = Modifier.padding(start = VaultSpacing.Small)
                    )
                }
                Text(
                    text = stringResource(R.string.authorized_directories),
                    style = MaterialTheme.typography.titleMedium,
                    modifier = Modifier.semantics { heading() }
                )
                if (directories.isEmpty()) {
                VaultEmptyState(
                    title = stringResource(R.string.directories_empty_title),
                    detail = stringResource(R.string.directories_empty_detail)
                )
                } else {
                    directories.forEach { rawUri ->
                        AuthorizedDirectoryItem(rawUri, onRemoveDirectory)
                    }
                }
            }
        }
    }
}

@Composable
private fun PermissionItem(
    icon: ImageVector,
    title: String,
    status: String,
    granted: Boolean,
    showAction: Boolean = !granted,
    buttonText: String,
    onClick: () -> Unit
) {
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .semantics(mergeDescendants = true) {
                stateDescription = status
            },
        verticalArrangement = Arrangement.spacedBy(VaultSpacing.Small)
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(VaultSpacing.Medium)
        ) {
            Icon(icon, contentDescription = null, tint = MaterialTheme.colorScheme.primary)
            Text(text = title, style = MaterialTheme.typography.titleMedium)
        }
        VaultStatusBadge(text = status, positive = granted)
        if (showAction) {
            TextButton(
                onClick = onClick,
                modifier = Modifier.vaultTouchTarget()
            ) {
                Text(buttonText)
            }
        }
    }
}

@Composable
private fun AuthorizedDirectoryItem(rawUri: String, onRemove: (String) -> Unit) {
    val readableName = remember(rawUri) {
        Uri.decode(rawUri).trimEnd('/').substringAfterLast('/').ifBlank {
            Uri.decode(rawUri).takeLast(64)
        }
    }
    VaultSectionCard {
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(VaultSpacing.Medium)
        ) {
            Icon(
                imageVector = Icons.Rounded.Folder,
                contentDescription = null,
                tint = MaterialTheme.colorScheme.primary
            )
            Column(modifier = Modifier.weight(1f)) {
                Text(readableName, style = MaterialTheme.typography.titleMedium)
                Text(
                    text = Uri.decode(rawUri).takeLast(96),
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }
        }
        TextButton(
            onClick = { onRemove(rawUri) },
            modifier = Modifier.vaultTouchTarget()
        ) {
            Text(stringResource(R.string.remove))
        }
    }
}

private val PreviewState = BackupUiState(
    settings = BackupSettings(
        deviceId = "preview-device",
        deviceName = "Pixel 预览机",
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

@Preview(name = "Compact Light", widthDp = 360, heightDp = 800, showBackground = true)
@Preview(
    name = "Compact Dark Large Text",
    widthDp = 412,
    heightDp = 915,
    fontScale = 1.6f,
    uiMode = Configuration.UI_MODE_NIGHT_YES,
    showBackground = true
)
@Preview(name = "Medium", widthDp = 600, heightDp = 960, showBackground = true)
@Preview(name = "Expanded", widthDp = 1000, heightDp = 800, showBackground = true)
@Composable
private fun MidnightVaultPreview() {
    MidnightVaultTheme {
        MidnightVaultApp(
            uiState = PreviewState,
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
