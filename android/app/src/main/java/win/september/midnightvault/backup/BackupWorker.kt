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

class BackupWorker(
    appContext: Context,
    parameters: WorkerParameters
) : CoroutineWorker(appContext, parameters) {
    override suspend fun doWork(): Result {
        setForeground(createForegroundInfo())

        // 下一里程碑在这里接入 MediaStore、SAF 扫描、分块上传与断点状态。
        BackupScheduler.scheduleNext(applicationContext)
        return Result.failure(workDataOf("message" to "尚未完成设备配对与目录授权"))
    }

    private fun createForegroundInfo(): ForegroundInfo {
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
            .setContentText(applicationContext.getString(R.string.backup_notification_text))
            .setOngoing(true)
            .build()

        return ForegroundInfo(2001, notification)
    }
}
