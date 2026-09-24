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
    private var pattern: PatternPipe? = null
    private var audio: AudioPipe? = null
    private var sink: FrameSink? = null
    private var q: Quality? = null
    private var wantAudio = false
    private var via = "wifi"
    private var httpHost = ""
    private var httpPort = 1224
    private var cfgOn = false
    @Volatile private var replacing = false
    private val sessgen = java.util.concurrent.atomic.AtomicInteger()
    private var lastcfg = 0L
    private val gate = Any()

    private val cfgCb = object : ComponentCallbacks {
        override fun onConfigurationChanged(newConfig: Configuration) {
            val now = android.os.SystemClock.elapsedRealtime()
            lastcfg = now
            Handler(Looper.getMainLooper()).postDelayed({
                if (lastcfg != now) return@postDelayed
                if (mp == null || q == null || video == null) return@postDelayed
                Thread({ onOrient() }, "scst-orient").start()
            }, 500)
        }
        override fun onLowMemory() {}
    }

    override fun onCreate() {
        super.onCreate()
        val prev = Thread.getDefaultUncaughtExceptionHandler()
        Thread.setDefaultUncaughtExceptionHandler { t, e ->
            Log.e("scst", "crash ${t.name} ${e.javaClass.simpleName} ${e.message}", e)
            prev?.uncaughtException(t, e)
        }
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
        val pattern = intent?.getBooleanExtra(EXTRA_PATTERN, false) == true
        if (mp != null || sink != null || video != null || this.pattern != null) {
            Log.i("scst", "restart, stop previous")
            replacing = true
            stopCast()
            replacing = true
        }
        startFg(pattern)
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
        via = when (mode) {
            "usb", "usb-lan" -> "usb"
            "usb-adb" -> "usb(adb)"
            else -> "wifi"
        }
        val ip = intent?.getStringExtra(EXTRA_IP) ?: ""
        val port = intent?.getIntExtra(EXTRA_PORT, Proto.TCP_PORT) ?: Proto.TCP_PORT
        httpHost = ip
        httpPort = intent?.getIntExtra(EXTRA_HTTP, 1224) ?: 1224
        if (httpPort <= 0) httpPort = 1224
        if (pattern) {
            val mygen = sessgen.incrementAndGet()
            Thread {
                try {
                    val s = openUsbSink()
                    if (sessgen.get() != mygen) return@Thread
                    sink = s
                    beginPattern(s)
                    replacing = false
                } catch (ex: Exception) {
                    if (sessgen.get() != mygen) return@Thread
                    Log.w("scst", "pattern ${ex.javaClass.simpleName} ${ex.message}")
                    val msg = ex.message?.takeIf { it.contains("电脑") } ?: "USB 测试失败"
                    sendBroadcast(Intent(ACTION_STAT).setPackage(packageName).putExtra("msg", msg))
                    stopCast()
                    stopSelf()
                }
            }.start()
            return START_STICKY
        }
        if (data == null) {
            stopSelf()
            return START_NOT_STICKY
        }
        Thread {
            val mygen = sessgen.incrementAndGet()
            try {
                val s: FrameSink = when (mode) {
                    "usb" -> openUsbSink()
                    "usb-lan" -> TcpSink.listen(UsbLan.lastNet)
                    "usb-adb" -> UsbLoop.open()
                    else -> TcpSink(ip, port)
                }
                if (sessgen.get() != mygen) return@Thread
                sink = s
                startCtrl(s)
                if (mode == "usb") {
                    val qtmp = Quality.byName(qname)
                    this.q = qtmp
                    this.wantAudio = wantAudio
                    val dm = metrics()
                    val fit = qtmp.fit(dm.widthPixels, dm.heightPixels)
                    if (!sendHello(fit.first, fit.second, qtmp.fps, wantAudio) || !probeUsb())
                        throw IllegalStateException("电脑未打开 ScreenKit")
                }
                if (sessgen.get() != mygen) return@Thread
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
                replacing = false
                val e = fail
                if (e != null) throw e
            } catch (ex: Exception) {
                if (sessgen.get() != mygen) return@Thread
                Log.w("scst", "start ${ex.javaClass.simpleName} ${ex.message}")
                val msg = if (mode == "usb") (ex.message ?: "连接失败") else "连接失败"
                sendBroadcast(Intent(ACTION_STAT).setPackage(packageName).putExtra("msg", msg))
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
                Log.w("scst", "projection onStop replacing=$replacing")
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
        val dm = metrics()
        val fit = q.fit(dm.widthPixels, dm.heightPixels)
        sendHello(fit.first, fit.second, q.fps, wantAudio)
        val v = VideoPipe(projection, dm.widthPixels, dm.heightPixels, dm.densityDpi, q, s, ::peerGone)
        synchronized(gate) {
            video = v
        }
        sendHello(v.outW, v.outH, v.fps, wantAudio)
        sendBroadcast(
            Intent(ACTION_STAT).setPackage(packageName).putExtra(
                "msg",
                "投屏中 $via ${v.outW}x${v.outH}@${v.fps}",
            ),
        )
        if (wantAudio) {
            Handler(Looper.getMainLooper()).postDelayed({
                if (mp == null) return@postDelayed
                try {
                    audio = AudioPipe(projection, s)
                    sendBroadcast(Intent(ACTION_STAT).setPackage(packageName).putExtra("msg", "投屏中 $via ${v.outW}x${v.outH}@${v.fps} 有声"))
                } catch (ex: Exception) {
                    Log.w("scst", "audio ${ex.message}")
                    sendBroadcast(Intent(ACTION_STAT).setPackage(packageName).putExtra("msg", "投屏中 $via ${v.outW}x${v.outH}@${v.fps} 无声"))
                }
            }, 400)
        }
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

    private fun sendHello(outW: Int, outH: Int, fps: Int, wantAudio: Boolean): Boolean {
        val s = sink ?: return false
        val dm = metrics()
        val hello = JSONObject()
            .put("cmd", "hello")
            .put("name", Build.MODEL)
            .put("w", outW)
            .put("h", outH)
            .put("dw", dm.widthPixels)
            .put("dh", dm.heightPixels)
            .put("fps", fps)
            .put("br", this.q?.bitrate ?: 0)
            .put("audio", wantAudio)
            .put("pix", "h264")
            .put("aud", "aac")
            .put("via", via)
        val ok = s.send(Proto.T_JSON, hello.toString().toByteArray(Charsets.UTF_8))
        Log.i("scst", "hello $via $outW x $outH ok=$ok")
        return ok
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

    private fun onOrient() {
        val cur = q ?: return
        val v = synchronized(gate) {
            if (replacing || mp == null) return
            video
        } ?: return
        val dm = metrics()
        val fit = cur.fit(dm.widthPixels, dm.heightPixels)
        if (v.outW == fit.first && v.outH == fit.second) {
            try { sendOrient() } catch (ex: Exception) { Log.w("scst", "orient ${ex.message}") }
            return
        }
        val oldW = v.srcW
        val oldH = v.srcH
        val oldDpi = dm.densityDpi
        Log.i("scst", "orient rebind ${dm.widthPixels}x${dm.heightPixels} enc ${v.outW}x${v.outH} -> ${fit.first}x${fit.second}")
        replacing = true
        try {
            v.haltSend()
            val ok = runOnMain { v.rebind(dm.widthPixels, dm.heightPixels, dm.densityDpi, cur) }
            if (!ok) {
                Log.w("scst", "orient rebind fail, restore ${oldW}x${oldH}")
                val restored = runOnMain { v.rebind(oldW, oldH, oldDpi, cur) }
                if (restored) runOnMain { v.resumeSend(); true }
                try { sendOrient() } catch (ex: Exception) { Log.w("scst", "orient ${ex.message}") }
            } else {
                sendHello(v.outW, v.outH, v.fps, wantAudio)
                runOnMain { v.resumeSend(); true }
                sendBroadcast(
                    Intent(ACTION_STAT).setPackage(packageName).putExtra(
                        "msg",
                        "投屏中 $via ${v.outW}x${v.outH}@${v.fps}",
                    ),
                )
            }
        } catch (ex: Exception) {
            Log.w("scst", "orient ${ex.message}")
            try { sendOrient() } catch (_: Exception) { }
        } finally {
            replacing = false
        }
    }

    private fun runOnMain(fn: () -> Boolean): Boolean {
        val latch = java.util.concurrent.CountDownLatch(1)
        var ok = false
        Handler(Looper.getMainLooper()).post {
            try { ok = fn() } catch (ex: Exception) { Log.w("scst", "main ${ex.message}") }
            latch.countDown()
        }
        return try {
            latch.await()
            ok
        } catch (_: Exception) {
            false
        }
    }

    private fun peerGone() {
        Handler(Looper.getMainLooper()).post {
            if (mp == null && pattern == null) return@post
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

    private fun beginPattern(s: FrameSink) {
        startCtrl(s)
        if (!sendHello(640, 360, 15, false) || !probeUsb())
            throw IllegalStateException("电脑未打开 ScreenKit")
        pattern = PatternPipe(640, 360, 15, 800_000, s, ::peerGone)
        sendBroadcast(
            Intent(ACTION_STAT).setPackage(packageName).putExtra("msg", "USB 测试画面 640x360"),
        )
        Log.i("scst", "pattern usb 640x360")
    }

    private fun startFg(pattern: Boolean = false) {
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
            val t = if (pattern && Build.VERSION.SDK_INT >= 31)
                ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE
            else
                ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PROJECTION
            startForeground(1, n, t)
        } else {
            startForeground(1, n)
        }
    }

    private fun stopCast() {
        if (cfgOn) {
            try { unregisterComponentCallbacks(cfgCb) } catch (_: Exception) { }
            cfgOn = false
        }
		notifyPcStop()
        try { sink?.send(Proto.T_JSON, """{"cmd":"bye"}""".toByteArray(Charsets.UTF_8)) } catch (_: Exception) { }
        try { audio?.stop() } catch (_: Exception) { }
        try { video?.stop() } catch (_: Exception) { }
        try { pattern?.stop() } catch (_: Exception) { }
        try { sink?.close() } catch (_: Exception) { }
        try { mp?.stop() } catch (_: Exception) { }
        audio = null
        video = null
        pattern = null
        sink = null
        mp = null
        q = null
        replacing = false
        sendBroadcast(Intent(ACTION_STAT).setPackage(packageName).putExtra("msg", "已停止"))
    }

    private fun notifyPcStop() {
        var host = httpHost
        var port = httpPort
        if (host.isEmpty()) {
            try {
                val p = Prefs(this)
                host = p.lastHost
                if (p.lastPort > 0) port = p.lastPort
            } catch (_: Exception) { }
        }
        if (host.isEmpty()) return
        val th = Thread({
            try {
                val u = java.net.URL("http://$host:$port/api/cast/stop")
                val c = u.openConnection() as java.net.HttpURLConnection
                c.connectTimeout = 400
                c.readTimeout = 400
                c.requestMethod = "GET"
                c.doInput = true
                val code = try {
                    c.inputStream.use { it.readBytes() }
                    c.responseCode
                } catch (_: Exception) {
                    try { c.errorStream?.close() } catch (_: Exception) { }
                    c.responseCode
                }
                c.disconnect()
                Log.i("scst", "http stop $host:$port $code")
            } catch (ex: Exception) {
                Log.w("scst", "http stop ${ex.message}")
            }
        }, "scst-http-stop")
        th.start()
        try { th.join(400) } catch (_: Exception) { }
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

    private fun probeUsb(): Boolean {
        val s = sink ?: return false
        val pad = CharArray(3500) { 'x' }.concatToString()
        val raw = JSONObject().put("cmd", "probe").put("z", pad).toString().toByteArray(Charsets.UTF_8)
        repeat(12) {
            if (!s.send(Proto.T_JSON, raw)) {
                Log.w("scst", "usb probe fail @$it")
                return false
            }
        }
        Log.i("scst", "usb probe ok")
        return true
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
            sendHello(nv.outW, nv.outH, nv.fps, wantAudio)
            sendBroadcast(
                Intent(ACTION_STAT).setPackage(packageName).putExtra(
                    "msg",
                    "投屏中 $via ${nv.outW}x${nv.outH}@${nv.fps}",
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
        const val EXTRA_HTTP = "http"
        const val EXTRA_PATTERN = "pattern"
    }
}
