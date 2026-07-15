package win.september.midnightvault

import android.Manifest
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.provider.Settings
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
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import win.september.midnightvault.backup.BackupScheduler

class MainActivity : ComponentActivity() {
    private val notificationPermission = registerForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        requestNotificationPermission()
        BackupScheduler.scheduleNext(this)

        setContent {
            MaterialTheme {
                BackupHome(
                    onBackupNow = { BackupScheduler.enqueueNow(this) },
                    onExactAlarmSettings = ::openExactAlarmSettings
                )
            }
        }
    }

    private fun requestNotificationPermission() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            notificationPermission.launch(Manifest.permission.POST_NOTIFICATIONS)
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
}

@Composable
@OptIn(ExperimentalMaterial3Api::class)
private fun BackupHome(
    onBackupNow: () -> Unit,
    onExactAlarmSettings: () -> Unit
) {
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
                StatusCard(
                    stringResource(R.string.pc_status),
                    stringResource(R.string.not_paired)
                )
            }
            item {
                StatusCard(
                    stringResource(R.string.pending_upload),
                    stringResource(R.string.after_first_scan)
                )
            }
            item {
                StatusCard(
                    stringResource(R.string.recent_task),
                    stringResource(R.string.no_records)
                )
            }
            item {
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(12.dp)
                ) {
                    Button(onClick = onBackupNow, modifier = Modifier.weight(1f)) {
                        Text(stringResource(R.string.backup_now))
                    }
                    OutlinedButton(onClick = onExactAlarmSettings, modifier = Modifier.weight(1f)) {
                        Text(stringResource(R.string.exact_alarm))
                    }
                }
            }
        }
    }
}

@Composable
private fun StatusCard(title: String, value: String) {
    Card(modifier = Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp)) {
            Text(title, style = MaterialTheme.typography.labelLarge)
            Text(value, style = MaterialTheme.typography.titleLarge)
        }
    }
}
