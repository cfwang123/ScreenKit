package com.whj.screenkit

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.ComponentCallbacks
import android.content.Intent
import android.content.pm.ServiceInfo
import android.content.res.Configuration
import android.hardware.usb.UsbManager
import android.media.projection.MediaProjection
import android.media.projection.MediaProjectionManager
import android.os.Build
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import android.util.DisplayMetrics
import android.util.Log
import android.view.WindowManager
import org.json.JSONObject

class CastService : Service() {
    private var mp: MediaProjection? = null
    private var video: VideoPipe? = null
    private var audio: AudioPipe? = null
    private var sink: FrameSink? = null
    private var q: Quality? = null
    private var wantAudio = false
    private var cfgOn = false
    @Volatile private var replacing = false
    private val gate = Any()

    private val cfgCb = object : ComponentCallbacks {
        override fun onConfigurationChanged(newConfig: Configuration) {
            Handler(Looper.getMainLooper()).postDelayed({
                Thread({
                    try { sendOrient() }
                    catch (ex: Exception) { Log.w("scst", "orient ${ex.message}") }
                }, "scst-orient").start()
            }, 250)
        }
        override fun onLowMemory() {}
    }

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == ACTION_STOP) {
            stopCast()
            stopSelf()
            return START_NOT_STICKY
        }
        if (intent?.action == ACTION_QUALITY) {
            val qname = intent.getStringExtra(EXTRA_Q) ?: Quality.PRESETS[1].name
            applyQuality(Quality.byName(qname))
            return START_STICKY
        }
        startFg()
        val code = intent?.getIntExtra(EXTRA_CODE, 0) ?: 0
        val data = if (Build.VERSION.SDK_INT >= 33) {
            intent?.getParcelableExtra(EXTRA_DATA, Intent::class.java)
        } else {
            @Suppress("DEPRECATION")
            intent?.getParcelableExtra(EXTRA_DATA)
        }
        val qname = intent?.getStringExtra(EXTRA_Q) ?: Quality.PRESETS[1].name
        val wantAudio = intent?.getBooleanExtra(EXTRA_AUDIO, true) == true
        val mode = intent?.getStringExtra(EXTRA_MODE) ?: "tcp"
        val ip = intent?.getStringExtra(EXTRA_IP) ?: ""
        val port = intent?.getIntExtra(EXTRA_PORT, Proto.TCP_PORT) ?: Proto.TCP_PORT
        if (data == null) {
            stopSelf()
            return START_NOT_STICKY
        }
        Thread {
            try {
                val s: FrameSink = when (mode) {
                    "usb" -> openUsbSink()
                    "usb-lan" -> TcpSink.listen(UsbLan.lastNet)
                    "usb-adb" -> UsbLoop.open()
                    else -> TcpSink(ip, port)
                }
                sink = s
                val latch = java.util.concurrent.CountDownLatch(1)
                var fail: Exception? = null
                Handler(Looper.getMainLooper()).post {
                    try {
                        beginCapture(code, data, Quality.byName(qname), wantAudio, s)
                    } catch (ex: Exception) {
                        fail = ex
                    }
                    latch.countDown()
                }
                latch.await()
                val e = fail
                if (e != null) throw e
            } catch (ex: Exception) {
                sendBroadcast(Intent(ACTION_STAT).setPackage(packageName).putExtra("msg", "失败: ${ex.message}"))
                stopCast()
                stopSelf()
            }
        }.start()
        return START_STICKY
    }

    private fun beginCapture(
        code: Int,
        data: Intent,
        q: Quality,
        wantAudio: Boolean,
        s: FrameSink,
    ) {
        val mgr = getSystemService(MEDIA_PROJECTION_SERVICE) as MediaProjectionManager
        val projection = mgr.getMediaProjection(code, data)
            ?: throw IllegalStateException("MediaProjection 为空")
        projection.registerCallback(object : MediaProjection.Callback() {
            override fun onStop() {
                if (replacing) return
                Handler(Looper.getMainLooper()).post {
                    if (replacing || mp == null) return@post
                    stopCast()
                    stopSelf()
                }
            }
        }, Handler(Looper.getMainLooper()))
        mp = projection
        this.q = q
        this.wantAudio = wantAudio
        startCtrl(s)
        val dm = metrics()
        val v = VideoPipe(projection, dm.widthPixels, dm.heightPixels, dm.densityDpi, q, s, ::peerGone)
        synchronized(gate) {
            video = v
        }
        sendHello(v, wantAudio)
        var audioOk = false
        if (wantAudio) {
            try {
                audio = AudioPipe(projection, s)
                audioOk = true
            } catch (ex: Exception) {
                sendBroadcast(Intent(ACTION_STAT).setPackage(packageName).putExtra("msg", "仅画面: ${ex.message}"))
            }
        }
        sendBroadcast(
            Intent(ACTION_STAT).setPackage(packageName).putExtra(
                "msg",
                "投屏中 ${v.outW}x${v.outH}@${v.fps}" + if (audioOk) " 有声" else " 无声",
            ),
        )
        if (!cfgOn) {
            registerComponentCallbacks(cfgCb)
            cfgOn = true
        }
    }

    private fun metrics(): DisplayMetrics {
        val dm = DisplayMetrics()
        @Suppress("DEPRECATION")
        (getSystemService(WINDOW_SERVICE) as WindowManager).defaultDisplay.getRealMetrics(dm)
        return dm
    }

    private fun sendHello(v: VideoPipe, wantAudio: Boolean) {
        val s = sink ?: return
        val dm = metrics()
        val hello = JSONObject()
            .put("cmd", "hello")
            .put("name", Build.MODEL)
            .put("w", v.outW)
            .put("h", v.outH)
            .put("dw", dm.widthPixels)
            .put("dh", dm.heightPixels)
            .put("fps", v.fps)
            .put("br", this.q?.bitrate ?: 0)
            .put("audio", wantAudio)
            .put("pix", "h264")
            .put("aud", "aac")
        s.send(Proto.T_JSON, hello.toString().toByteArray(Charsets.UTF_8))
    }

    private fun sendOrient() {
        val s = sink ?: return
        val dm = metrics()
        val o = JSONObject()
            .put("cmd", "orient")
            .put("dw", dm.widthPixels)
            .put("dh", dm.heightPixels)
        s.send(Proto.T_JSON, o.toString().toByteArray(Charsets.UTF_8))
    }

    private fun peerGone() {
        Handler(Looper.getMainLooper()).post {
            if (mp == null) return@post
            sendBroadcast(Intent(ACTION_STAT).setPackage(packageName).putExtra("msg", "电脑已断开"))
            stopCast()
            stopSelf()
        }
    }

    private fun openUsbSink(): FrameSink {
        val usb = getSystemService(USB_SERVICE) as UsbManager
        val acc = usb.accessoryList?.firstOrNull()
            ?: throw IllegalStateException("没有 USB 配件。请插上数据线，电脑 ScreenKit 会切换配件（无需 USB 调试）")
        return UsbSink(usb, acc)
    }

    private fun startFg() {
        val ch = "cast"
        val nm = getSystemService(NOTIFICATION_SERVICE) as NotificationManager
        if (Build.VERSION.SDK_INT >= 26) {
            nm.createNotificationChannel(
                NotificationChannel(ch, "投屏", NotificationManager.IMPORTANCE_LOW),
            )
        }
        val pi = PendingIntent.getActivity(
            this,
            0,
            Intent(this, CastActivity::class.java)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_SINGLE_TOP),
            PendingIntent.FLAG_IMMUTABLE,
        )
        val n = if (Build.VERSION.SDK_INT >= 26) {
            Notification.Builder(this, ch)
        } else {
            @Suppress("DEPRECATION")
            Notification.Builder(this)
        }
            .setContentTitle(getString(R.string.label_cast))
            .setContentText("正在投屏")
            .setSmallIcon(android.R.drawable.ic_menu_share)
            .setContentIntent(pi)
            .build()
        if (Build.VERSION.SDK_INT >= 29) {
            startForeground(1, n, ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PROJECTION)
        } else {
            startForeground(1, n)
        }
    }

    private fun stopCast() {
        if (cfgOn) {
            try { unregisterComponentCallbacks(cfgCb) } catch (_: Exception) { }
            cfgOn = false
        }
        try { sink?.send(Proto.T_JSON, """{"cmd":"bye"}""".toByteArray(Charsets.UTF_8)) } catch (_: Exception) { }
        try { audio?.stop() } catch (_: Exception) { }
        try { video?.stop() } catch (_: Exception) { }
        try { sink?.close() } catch (_: Exception) { }
        try { mp?.stop() } catch (_: Exception) { }
        audio = null
        video = null
        sink = null
        mp = null
        q = null
        replacing = false
        sendBroadcast(Intent(ACTION_STAT).setPackage(packageName).putExtra("msg", "已停止"))
    }

    private fun startCtrl(s: FrameSink) {
        val ins = s.input() ?: return
        Thread({
            while (true) {
                val pair = Proto.read(ins) ?: break
                if (pair.first != Proto.T_JSON) continue
                val obj = try {
                    JSONObject(String(pair.second, Charsets.UTF_8))
                } catch (_: Exception) {
                    continue
                }
                when (obj.optString("cmd")) {
                    "ping" -> {
                        val pong = JSONObject().put("cmd", "pong").put("t", obj.optLong("t"))
                        s.send(Proto.T_JSON, pong.toString().toByteArray(Charsets.UTF_8))
                    }
                    "quality" -> {
                        val name = obj.optString("name")
                        if (name.isNotEmpty()) applyQuality(Quality.byName(name))
                    }
                }
            }
        }, "scst-ctrl").start()
    }

    private fun applyQuality(nq: Quality, force: Boolean = false) {
        Thread {
            val old: VideoPipe?
            val projection: MediaProjection
            val s: FrameSink
            synchronized(gate) {
                projection = mp ?: return@Thread
                s = sink ?: return@Thread
                if (!force && q?.name == nq.name && video != null) return@Thread
                q = nq
                old = video
                replacing = true
                old?.haltSend()
            }
            val dm = metrics()
            val latch = java.util.concurrent.CountDownLatch(1)
            var v: VideoPipe? = null
            var fail: Exception? = null
            Handler(Looper.getMainLooper()).post {
                try {
                    v = VideoPipe(projection, dm.widthPixels, dm.heightPixels, dm.densityDpi, nq, s, ::peerGone)
                } catch (ex: Exception) {
                    fail = ex
                }
                latch.countDown()
            }
            latch.await()
            val nv = v
            if (nv == null) {
                replacing = false
                sendBroadcast(Intent(ACTION_STAT).setPackage(packageName).putExtra("msg", "改画质失败: ${fail?.message}"))
                return@Thread
            }
            synchronized(gate) {
                if (mp == null) {
                    replacing = false
                    try { nv.stop() } catch (_: Exception) { }
                    return@Thread
                }
                video = nv
            }
            try { old?.stop() } catch (_: Exception) { }
            try { Thread.sleep(200) } catch (_: Exception) { }
            replacing = false
            sendHello(nv, wantAudio)
            sendBroadcast(
                Intent(ACTION_STAT).setPackage(packageName).putExtra(
                    "msg",
                    "投屏中 ${nv.outW}x${nv.outH}@${nv.fps}",
                ),
            )
        }.start()
    }

    override fun onDestroy() {
        stopCast()
        super.onDestroy()
    }

    companion object {
        const val ACTION_STOP = "com.whj.screenkit.CAST_STOP"
        const val ACTION_QUALITY = "com.whj.screenkit.CAST_QUALITY"
        const val ACTION_STAT = "com.whj.screenkit.CAST_STAT"
        const val EXTRA_CODE = "code"
        const val EXTRA_DATA = "data"
        const val EXTRA_Q = "q"
        const val EXTRA_AUDIO = "audio"
        const val EXTRA_MODE = "mode"
        const val EXTRA_IP = "ip"
        const val EXTRA_PORT = "port"
    }
}
