package com.whj.screenkit

import android.app.admin.DevicePolicyManager
import android.content.BroadcastReceiver
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.os.PowerManager
import android.os.SystemClock
import android.util.Log

object ScreenOff {
    @Volatile var active = false
    private var lock: PowerManager.WakeLock? = null
    private var since = 0L
    private var rec: BroadcastReceiver? = null

    fun adminOn(ctx: Context): Boolean {
        val dpm = ctx.applicationContext.getSystemService(Context.DEVICE_POLICY_SERVICE) as DevicePolicyManager
        return dpm.isAdminActive(ComponentName(ctx.applicationContext, AdminRecv::class.java))
    }

    fun enter(ctx: Context): Boolean {
        val app = ctx.applicationContext
        val dpm = app.getSystemService(Context.DEVICE_POLICY_SERVICE) as DevicePolicyManager
        val cn = ComponentName(app, AdminRecv::class.java)
        if (!dpm.isAdminActive(cn)) return false
        if (active) return true
        val pm = app.getSystemService(Context.POWER_SERVICE) as PowerManager
        val wl = pm.newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "screenkit:castoff")
        wl.setReferenceCounted(false)
        wl.acquire(2 * 60 * 60 * 1000L)
        lock = wl
        active = true
        since = SystemClock.uptimeMillis()
        val r = object : BroadcastReceiver() {
            override fun onReceive(c: Context?, i: Intent?) {
                if (i?.action != Intent.ACTION_SCREEN_ON) return
                if (SystemClock.uptimeMillis() - since < 1200) return
                Log.i("scst", "screen off exit power")
                leave(app)
            }
        }
        rec = r
        app.registerReceiver(r, IntentFilter(Intent.ACTION_SCREEN_ON))
        Log.i("scst", "screen off lock")
        dpm.lockNow()
        return true
    }

    fun leave(ctx: Context) {
        active = false
        val app = ctx.applicationContext
        rec?.let {
            try { app.unregisterReceiver(it) } catch (_: Exception) { }
        }
        rec = null
        try {
            if (lock?.isHeld == true) lock?.release()
        } catch (_: Exception) { }
        lock = null
        Log.i("scst", "screen off left")
    }
}
