package win.september.midnightvault.backup

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent

class MidnightBackupReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        BackupScheduler.enqueueNow(context)
        BackupScheduler.scheduleNext(context)
    }
}
