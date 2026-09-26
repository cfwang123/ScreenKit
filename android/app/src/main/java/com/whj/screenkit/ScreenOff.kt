package com.whj.screenkit

import android.annotation.SuppressLint
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.os.Build
import android.os.IBinder
import android.os.PowerManager
import android.os.SystemClock
import android.provider.Settings
import android.util.Log

/**
 * 熄屏后继续采集。不用设备管理 lockNow：那会让系统休眠，投屏镜像变成黑屏。
 * 优先关掉面板电源（合成仍在）；不行就把背光调到最暗并拉长息屏时间。
 */
object ScreenOff {
    const val OK = 0
    const val NEED_WRITE = 1
    const val FAIL = 2

    @Volatile var active = false
    var listener: ((Boolean) -> Unit)? = null

    private const val MODE_PANEL = 1
    private const val MODE_DIM = 2
    private const val POWER_OFF = 0
    private const val POWER_ON = 2

    private var mode = 0
    private var lock: PowerManager.WakeLock? = null
    private var since = 0L
    private var rec: BroadcastReceiver? = null
    private var oldBright = -1
    private var oldMode = -1
    private var oldTimeout = -1

    fun enter(ctx: Context): Int {
        val app = ctx.applicationContext
        if (active) return OK
        if (panel(POWER_OFF)) {
            arm(app, MODE_PANEL)
            return OK
        }
        if (!Settings.System.canWrite(app)) return NEED_WRITE
        if (!dim(app)) return FAIL
        arm(app, MODE_DIM)
        return OK
    }

    fun leave(ctx: Context, wake: Boolean = false, notify: Boolean = true) {
        if (!active && mode == 0 && rec == null) return
        val app = ctx.applicationContext
        active = false
        if (mode == MODE_PANEL) panel(POWER_ON)
        mode = 0
        rec?.let {
            try { app.unregisterReceiver(it) } catch (_: Exception) { }
        }
        rec = null
        restoreBright(app)
        try {
            if (lock?.isHeld == true) lock?.release()
        } catch (_: Exception) { }
        lock = null
        if (wake) wake(app)
        Log.i("scst", "screen off left")
        if (notify) listener?.invoke(false)
    }

    fun wake(ctx: Context) {
        try {
            val pm = ctx.applicationContext.getSystemService(Context.POWER_SERVICE) as PowerManager
            @Suppress("DEPRECATION")
            val wl = pm.newWakeLock(
                PowerManager.SCREEN_BRIGHT_WAKE_LOCK or
                    PowerManager.ACQUIRE_CAUSES_WAKEUP or
                    PowerManager.ON_AFTER_RELEASE,
                "screenkit:castwake",
            )
            wl.setReferenceCounted(false)
            wl.acquire(3000)
        } catch (ex: Exception) {
            Log.w("scst", "wake ${ex.message}")
        }
    }

    private fun arm(app: Context, m: Int) {
        mode = m
        active = true
        since = SystemClock.uptimeMillis()
        val partial = (app.getSystemService(Context.POWER_SERVICE) as PowerManager)
            .newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "screenkit:castoff")
        partial.setReferenceCounted(false)
        partial.acquire(2 * 60 * 60 * 1000L)
        lock = partial
        val r = object : BroadcastReceiver() {
            override fun onReceive(c: Context?, i: Intent?) {
                if (SystemClock.uptimeMillis() - since < 1500) return
                val act = i?.action ?: return
                if (mode == MODE_PANEL && act == Intent.ACTION_SCREEN_ON) {
                    Log.i("scst", "screen off exit power")
                    leave(app, wake = false)
                } else if (mode == MODE_DIM && act == Intent.ACTION_SCREEN_OFF) {
                    Log.i("scst", "screen off exit dim")
                    leave(app, wake = true)
                }
            }
        }
        rec = r
        val filter = IntentFilter()
        filter.addAction(Intent.ACTION_SCREEN_ON)
        filter.addAction(Intent.ACTION_SCREEN_OFF)
        app.registerReceiver(r, filter)
        Log.i("scst", "screen off on mode=$m")
        listener?.invoke(true)
    }

    @SuppressLint("PrivateApi", "BlockedPrivateApi")
    private fun panel(power: Int): Boolean {
        return try {
            val cls = Class.forName("android.view.SurfaceControl")
            val token = displayToken(cls) ?: return false
            val method = cls.getMethod("setDisplayPowerMode", IBinder::class.java, Int::class.javaPrimitiveType)
            method.invoke(null, token, power)
            Log.i("scst", "panel power=$power")
            true
        } catch (ex: Exception) {
            Log.w("scst", "panel power=$power ${ex.javaClass.simpleName} ${ex.message}")
            false
        }
    }

    private fun displayToken(cls: Class<*>): IBinder? {
        if (Build.VERSION.SDK_INT < 29) {
            try {
                val m = cls.getMethod("getBuiltInDisplay", Int::class.javaPrimitiveType)
                return m.invoke(null, 0) as? IBinder
            } catch (ex: Exception) {
                Log.w("scst", "builtin ${ex.message}")
            }
        } else {
            try {
                val m = cls.getMethod("getInternalDisplayToken")
                val t = m.invoke(null) as? IBinder
                if (t != null) return t
            } catch (ex: Exception) {
                Log.w("scst", "internal ${ex.message}")
            }
        }
        return try {
            val ids = cls.getMethod("getPhysicalDisplayIds").invoke(null) as? LongArray ?: return null
            val id = ids.firstOrNull() ?: return null
            cls.getMethod("getPhysicalDisplayToken", Long::class.javaPrimitiveType)
                .invoke(null, id) as? IBinder
        } catch (ex: Exception) {
            Log.w("scst", "physical ${ex.message}")
            null
        }
    }

    private fun dim(app: Context): Boolean {
        if (!Settings.System.canWrite(app)) return false
        val cr = app.contentResolver
        return try {
            oldMode = Settings.System.getInt(cr, Settings.System.SCREEN_BRIGHTNESS_MODE, Settings.System.SCREEN_BRIGHTNESS_MODE_AUTOMATIC)
            oldBright = Settings.System.getInt(cr, Settings.System.SCREEN_BRIGHTNESS, 128)
            oldTimeout = Settings.System.getInt(cr, Settings.System.SCREEN_OFF_TIMEOUT, 30000)
            Settings.System.putInt(cr, Settings.System.SCREEN_BRIGHTNESS_MODE, Settings.System.SCREEN_BRIGHTNESS_MODE_MANUAL)
            Settings.System.putInt(cr, Settings.System.SCREEN_BRIGHTNESS, 0)
            Settings.System.putInt(cr, Settings.System.SCREEN_OFF_TIMEOUT, 12 * 60 * 60 * 1000)
            Log.i("scst", "dim bright $oldBright -> 0 timeout $oldTimeout")
            true
        } catch (ex: Exception) {
            Log.w("scst", "dim ${ex.message}")
            restoreBright(app)
            false
        }
    }

    private fun restoreBright(app: Context) {
        if (oldBright < 0 && oldMode < 0 && oldTimeout < 0) return
        if (!Settings.System.canWrite(app)) {
            oldBright = -1
            oldMode = -1
            oldTimeout = -1
            return
        }
        val cr = app.contentResolver
        try {
            if (oldMode >= 0)
                Settings.System.putInt(cr, Settings.System.SCREEN_BRIGHTNESS_MODE, oldMode)
            if (oldBright >= 0)
                Settings.System.putInt(cr, Settings.System.SCREEN_BRIGHTNESS, oldBright)
            if (oldTimeout >= 0)
                Settings.System.putInt(cr, Settings.System.SCREEN_OFF_TIMEOUT, oldTimeout)
        } catch (ex: Exception) {
            Log.w("scst", "restore bright ${ex.message}")
        }
        oldBright = -1
        oldMode = -1
        oldTimeout = -1
    }
}
