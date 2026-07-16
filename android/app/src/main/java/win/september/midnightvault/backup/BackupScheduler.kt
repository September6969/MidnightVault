package win.september.midnightvault.backup

import android.app.AlarmManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.os.Build
import androidx.work.ExistingWorkPolicy
import androidx.work.BackoffPolicy
import androidx.work.Constraints
import androidx.work.NetworkType
import androidx.work.OneTimeWorkRequestBuilder
import androidx.work.ExistingPeriodicWorkPolicy
import androidx.work.PeriodicWorkRequestBuilder
import androidx.work.WorkManager
import java.time.Duration
import java.time.ZoneId
import java.time.ZonedDateTime
import java.util.concurrent.TimeUnit

object BackupScheduler {
    private const val AlarmRequestCode = 1001
    private const val ImmediateWorkName = "midnight-vault-backup"
    private const val CatchUpWorkName = "midnight-vault-catch-up"
    private const val ControlPollingWorkName = "midnight-vault-control-polling"
    private val BeijingZone = ZoneId.of("Asia/Shanghai")

    fun scheduleNext(context: Context) {
        val nextMidnight = ZonedDateTime.now(BeijingZone)
            .toLocalDate()
            .plusDays(1)
            .atStartOfDay(BeijingZone)
        val triggerAtMillis = nextMidnight.toInstant().toEpochMilli()

        val alarmManager = context.getSystemService(AlarmManager::class.java)
        val pendingIntent = PendingIntent.getBroadcast(
            context,
            AlarmRequestCode,
            Intent(context, MidnightBackupReceiver::class.java),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )

        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.S || alarmManager.canScheduleExactAlarms()) {
            alarmManager.setExactAndAllowWhileIdle(
                AlarmManager.RTC_WAKEUP,
                triggerAtMillis,
                pendingIntent
            )
        } else {
            alarmManager.setAndAllowWhileIdle(
                AlarmManager.RTC_WAKEUP,
                triggerAtMillis,
                pendingIntent
            )
        }

        val catchUpDelay = Duration.between(
            ZonedDateTime.now(BeijingZone),
            nextMidnight.plusMinutes(30)
        ).coerceAtLeast(Duration.ZERO)
        val catchUp = OneTimeWorkRequestBuilder<BackupWorker>()
            .setInitialDelay(catchUpDelay.toMillis(), TimeUnit.MILLISECONDS)
            .setConstraints(networkConstraints())
            .setBackoffCriteria(BackoffPolicy.EXPONENTIAL, 15, TimeUnit.MINUTES)
            .build()
        WorkManager.getInstance(context).enqueueUniqueWork(
            CatchUpWorkName,
            ExistingWorkPolicy.REPLACE,
            catchUp
        )
    }

    fun enqueueNow(context: Context) {
        val request = OneTimeWorkRequestBuilder<BackupWorker>()
            .setConstraints(networkConstraints())
            .setBackoffCriteria(BackoffPolicy.EXPONENTIAL, 15, TimeUnit.MINUTES)
            .build()
        WorkManager.getInstance(context).enqueueUniqueWork(
            ImmediateWorkName,
            ExistingWorkPolicy.APPEND_OR_REPLACE,
            request
        )
    }

    fun scheduleControlPolling(context: Context) {
        val request = PeriodicWorkRequestBuilder<ControlCommandWorker>(15, TimeUnit.MINUTES)
            .setConstraints(networkConstraints())
            .setBackoffCriteria(BackoffPolicy.EXPONENTIAL, 15, TimeUnit.MINUTES)
            .build()
        WorkManager.getInstance(context).enqueueUniquePeriodicWork(
            ControlPollingWorkName,
            ExistingPeriodicWorkPolicy.KEEP,
            request
        )
    }

    private fun networkConstraints() = Constraints.Builder()
        .setRequiredNetworkType(NetworkType.CONNECTED)
        .build()
}
