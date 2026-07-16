package win.september.midnightvault.backup

import android.app.NotificationChannel
import android.app.NotificationManager
import android.content.Context
import androidx.core.app.NotificationCompat
import androidx.work.CoroutineWorker
import androidx.work.ForegroundInfo
import androidx.work.WorkerParameters
import androidx.work.workDataOf
import win.september.midnightvault.R
import win.september.midnightvault.config.AppSettings
import win.september.midnightvault.network.ControlPlaneClient

class BackupWorker(
    appContext: Context,
    parameters: WorkerParameters
) : CoroutineWorker(appContext, parameters) {
    override suspend fun doWork(): Result {
        setForeground(createForegroundInfo(applicationContext.getString(R.string.backup_notification_text)))
        return try {
            val result = BackupEngine(applicationContext).run { progress ->
                val text = applicationContext.getString(
                    R.string.backup_notification_progress,
                    progress.currentFile,
                    progress.currentChunk,
                    progress.chunkCount
                )
                setForeground(createForegroundInfo(text))
                setProgress(
                    workDataOf(
                        "currentFile" to progress.currentFile,
                        "completedFiles" to progress.completedFiles,
                        "currentChunk" to progress.currentChunk,
                        "chunkCount" to progress.chunkCount
                    )
                )
            }
            val settings = AppSettings(applicationContext).snapshot()
            if (settings.isControlPlaneConfigured) {
                runCatching { ControlPlaneClient().reportBackup(settings, result) }
            }
            showResultNotification(
                applicationContext.getString(
                    R.string.backup_finished,
                    result.completedFiles,
                    result.failedFiles
                ),
                result.failedFiles == 0
            )
            Result.success(
                workDataOf(
                    "message" to applicationContext.getString(
                        R.string.backup_finished,
                        result.completedFiles,
                        result.failedFiles
                    ),
                    "completedFiles" to result.completedFiles,
                    "failedFiles" to result.failedFiles
                )
            )
        } catch (exception: BackupConfigurationException) {
            showResultNotification(exception.message.orEmpty(), false)
            Result.failure(workDataOf("message" to exception.message.orEmpty()))
        } catch (exception: BackupRetryableException) {
            showResultNotification(
                applicationContext.getString(R.string.backup_will_retry),
                false
            )
            Result.retry()
        } finally {
            BackupScheduler.scheduleNext(applicationContext)
        }
    }

    private fun createForegroundInfo(text: String): ForegroundInfo {
        val channelId = "midnight-vault-backup"
        val manager = applicationContext.getSystemService(NotificationManager::class.java)
        manager.createNotificationChannel(
            NotificationChannel(
                channelId,
                applicationContext.getString(R.string.backup_channel_name),
                NotificationManager.IMPORTANCE_LOW
            )
        )

        val notification = NotificationCompat.Builder(applicationContext, channelId)
            .setSmallIcon(android.R.drawable.stat_sys_upload)
            .setContentTitle(applicationContext.getString(R.string.backup_notification_title))
            .setContentText(text)
            .setOngoing(true)
            .setOnlyAlertOnce(true)
            .build()

        return ForegroundInfo(2001, notification)
    }

    private fun showResultNotification(text: String, successful: Boolean) {
        val channelId = "midnight-vault-result"
        val manager = applicationContext.getSystemService(NotificationManager::class.java)
        manager.createNotificationChannel(
            NotificationChannel(
                channelId,
                applicationContext.getString(R.string.backup_result_channel_name),
                NotificationManager.IMPORTANCE_DEFAULT
            )
        )
        val notification = NotificationCompat.Builder(applicationContext, channelId)
            .setSmallIcon(
                if (successful) android.R.drawable.stat_sys_upload_done
                else android.R.drawable.stat_notify_error
            )
            .setContentTitle(
                applicationContext.getString(
                    if (successful) R.string.backup_result_success
                    else R.string.backup_result_attention
                )
            )
            .setContentText(text)
            .setStyle(NotificationCompat.BigTextStyle().bigText(text))
            .setAutoCancel(true)
            .build()
        manager.notify(2002, notification)
    }
}
