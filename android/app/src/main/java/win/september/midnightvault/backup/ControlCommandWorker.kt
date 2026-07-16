package win.september.midnightvault.backup

import android.content.Context
import androidx.work.CoroutineWorker
import androidx.work.WorkerParameters
import win.september.midnightvault.config.AppSettings
import win.september.midnightvault.network.ControlPlaneClient

class ControlCommandWorker(
    appContext: Context,
    parameters: WorkerParameters
) : CoroutineWorker(appContext, parameters) {
    override suspend fun doWork(): Result {
        val settings = AppSettings(applicationContext).snapshot()
        if (!settings.isControlPlaneConfigured) return Result.success()
        return runCatching {
            val client = ControlPlaneClient()
            val command = client.nextCommand(settings) ?: return Result.success()
            if (command.type == "backup-now") {
                BackupScheduler.enqueueNow(applicationContext)
            }
            client.acknowledgeCommand(settings, command.id)
            Result.success()
        }.getOrElse { Result.retry() }
    }
}
