package com.whj.screenkit

import android.hardware.display.DisplayManager
import android.hardware.display.VirtualDisplay
import android.media.MediaCodec
import android.media.MediaCodecInfo
import android.media.MediaFormat
import android.media.projection.MediaProjection
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import android.view.Surface

class VideoPipe(
    private val mp: MediaProjection,
    var srcW: Int,
    var srcH: Int,
    private var dpi: Int,
    q: Quality,
    private val sink: FrameSink,
    private val onDead: () -> Unit = {},
) {
    private lateinit var enc: MediaCodec
    private val vd: VirtualDisplay
    private lateinit var surface: Surface
    @Volatile private var running = true
    @Volatile private var stopped = false
    private var th: Thread
    var outW: Int = 0
        private set
    var outH: Int = 0
        private set
    val fps: Int = q.fps

    init {
        val fit = q.fit(srcW, srcH)
        if (!openEnc(fit.first, fit.second, q))
            throw IllegalStateException("encoder")
        vd = mp.createVirtualDisplay(
            "skcast",
            outW,
            outH,
            dpi,
            DisplayManager.VIRTUAL_DISPLAY_FLAG_AUTO_MIRROR,
            surface,
            null,
            null,
        )
        th = Thread({ loop() }, "venc").also { it.start() }
        val h = Handler(Looper.getMainLooper())
        h.postDelayed({ kick() }, 80)
        h.postDelayed({ kick() }, 250)
        h.postDelayed({ kick() }, 700)
        h.postDelayed({ kick() }, 1500)
    }

    fun poke() = kick()

    private fun kick() {
        if (stopped) return
        refresh()
        try {
            val b = Bundle()
            b.putInt(MediaCodec.PARAMETER_KEY_REQUEST_SYNC_FRAME, 0)
            enc.setParameters(b)
        } catch (_: Exception) { }
    }

    private fun makeFmt(w: Int, h: Int, q: Quality, withProfile: Boolean): MediaFormat {
        val fmt = MediaFormat.createVideoFormat(MediaFormat.MIMETYPE_VIDEO_AVC, w, h)
        fmt.setInteger(MediaFormat.KEY_COLOR_FORMAT, MediaCodecInfo.CodecCapabilities.COLOR_FormatSurface)
        fmt.setInteger(MediaFormat.KEY_BIT_RATE, q.bitrate)
        fmt.setInteger(MediaFormat.KEY_FRAME_RATE, q.fps)
        fmt.setInteger(MediaFormat.KEY_I_FRAME_INTERVAL, 1)
        if (Build.VERSION.SDK_INT >= 31)
            fmt.setLong(MediaFormat.KEY_REPEAT_PREVIOUS_FRAME_AFTER, 50_000L)
        if (Build.VERSION.SDK_INT >= 29)
            fmt.setLong(MediaFormat.KEY_MAX_PTS_GAP_TO_ENCODER, 50_000L)
        if (withProfile) {
            fmt.setInteger(MediaFormat.KEY_PROFILE, MediaCodecInfo.CodecProfileLevel.AVCProfileBaseline)
            val level = if (maxOf(w, h) >= 1080)
                MediaCodecInfo.CodecProfileLevel.AVCLevel4
            else
                MediaCodecInfo.CodecProfileLevel.AVCLevel31
            fmt.setInteger(MediaFormat.KEY_LEVEL, level)
        }
        return fmt
    }

    private fun openEnc(w: Int, h: Int, q: Quality): Boolean {
        var c: MediaCodec? = null
        var s: Surface? = null
        try {
            c = MediaCodec.createEncoderByType(MediaFormat.MIMETYPE_VIDEO_AVC)
            try {
                c.configure(makeFmt(w, h, q, true), null, null, MediaCodec.CONFIGURE_FLAG_ENCODE)
            } catch (_: Exception) {
                try { c.release() } catch (_: Exception) { }
                c = MediaCodec.createEncoderByType(MediaFormat.MIMETYPE_VIDEO_AVC)
                c.configure(makeFmt(w, h, q, false), null, null, MediaCodec.CONFIGURE_FLAG_ENCODE)
            }
            s = c.createInputSurface()
            c.start()
            try {
                val b = android.os.Bundle()
                b.putInt(MediaCodec.PARAMETER_KEY_REQUEST_SYNC_FRAME, 0)
                c.setParameters(b)
            } catch (_: Exception) { }
            enc = c
            surface = s
            outW = w
            outH = h
            return true
        } catch (ex: Exception) {
            android.util.Log.w("scst", "openEnc ${w}x${h} ${ex.message}")
            try { s?.release() } catch (_: Exception) { }
            try { c?.stop() } catch (_: Exception) { }
            try { c?.release() } catch (_: Exception) { }
            return false
        }
    }

    private fun closeEnc() {
        try { enc.stop() } catch (_: Exception) { }
        try { enc.release() } catch (_: Exception) { }
        try { surface.release() } catch (_: Exception) { }
    }

    private fun startLoop() {
        running = true
        stopped = false
        th = Thread({ loop() }, "venc").also { it.start() }
    }

    /** 停发送后在主线程调用：保留 VirtualDisplay，只换编码器尺寸。 */
    fun rebind(newSrcW: Int, newSrcH: Int, newDpi: Int, q: Quality): Boolean {
        if (stopped) return false
        val fit = q.fit(newSrcW, newSrcH)
        val nw = fit.first
        val nh = fit.second
        try { vd.setSurface(null) } catch (_: Exception) { }
        closeEnc()
        if (!openEnc(nw, nh, q)) return false
        try {
            vd.resize(nw, nh, newDpi)
            vd.setSurface(surface)
        } catch (ex: Exception) {
            android.util.Log.w("scst", "vd rebind ${ex.message}")
            closeEnc()
            return false
        }
        srcW = newSrcW
        srcH = newSrcH
        dpi = newDpi
        android.util.Log.i("scst", "rebind ${nw}x${nh} src=${newSrcW}x${newSrcH}")
        return true
    }

    fun resumeSend() {
        if (stopped) return
        startLoop()
    }

    private fun loop() {
        val info = MediaCodec.BufferInfo()
        var sps: ByteArray? = null
        var pps: ByteArray? = null
        var sentCfg = false
        var peer = false
        var nframe = 0
        var lastidr = SystemClock.elapsedRealtime()
        try {
        while (running && !stopped) {
            val now = SystemClock.elapsedRealtime()
            if (now - lastidr > 2000) {
                lastidr = now
                try {
                    val b = android.os.Bundle()
                    b.putInt(MediaCodec.PARAMETER_KEY_REQUEST_SYNC_FRAME, 0)
                    enc.setParameters(b)
                } catch (_: Exception) { }
            }
            val ix = try {
                enc.dequeueOutputBuffer(info, 80_000)
            } catch (ex: Exception) {
                android.util.Log.w("scst", "venc dq ${ex.message}")
                continue
            }
            if (ix == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED) {
                val fmt = enc.outputFormat
                val pair = parseCsd(fmt.getByteBuffer("csd-0"))
                sps = pair.first
                pps = pair.second
                if (pps.isEmpty()) pps = annexOfCsd(fmt.getByteBuffer("csd-1"))
                val head = (sps ?: ByteArray(0)) + (pps ?: ByteArray(0))
                if (head.isNotEmpty()) {
                    sink.send(Proto.T_VIDEO, head)
                    sentCfg = true
                }
                continue
            }
            if (ix < 0) continue
            var nal: ByteArray? = null
            try {
                val buf = enc.getOutputBuffer(ix) ?: continue
                val data = ByteArray(info.size)
                buf.position(info.offset)
                buf.limit(info.offset + info.size)
                buf.get(data)
                if (info.flags and MediaCodec.BUFFER_FLAG_CODEC_CONFIG != 0) {
                    val pair = parseCsd(java.nio.ByteBuffer.wrap(data))
                    sps = pair.first
                    if (pair.second.isNotEmpty()) pps = pair.second
                    nal = (sps ?: ByteArray(0)) + (pps ?: ByteArray(0))
                    sentCfg = nal.isNotEmpty()
                } else {
                    nal = toAnnexB(data)
                    val idr = isIdr(nal)
                    nframe++
                    if ((idr || !sentCfg || nframe <= 5) && sps != null) {
                        nal = sps!! + (pps ?: ByteArray(0)) + nal
                        sentCfg = true
                    }
                }
            } catch (_: Exception) {
                nal = null
            } finally {
                try { enc.releaseOutputBuffer(ix, false) } catch (_: Exception) { }
            }
            if (nal != null && nal.isNotEmpty()) {
                if (nframe <= 3 || nframe % 60 == 0)
                    android.util.Log.i("scst", "venc $nframe ${nal.size}b")
                if (!sink.send(Proto.T_VIDEO, nal)) {
                    peer = true
                    running = false
                    break
                }
            }
        }
        } catch (ex: Throwable) {
            android.util.Log.e("scst", "venc loop ${ex.javaClass.simpleName} ${ex.message}")
        }
        if (peer && !stopped) onDead()
    }

    fun haltSend() {
        running = false
        try { th.join(800) } catch (_: Exception) { }
    }

    fun refresh() {
        try { vd.resize(outW, outH, dpi) } catch (_: Exception) { }
    }

    fun stop() {
        stopped = true
        running = false
        try { th.join(500) } catch (_: Exception) { }
        try { vd.release() } catch (_: Exception) { }
        try { surface.release() } catch (_: Exception) { }
        try { enc.stop() } catch (_: Exception) { }
        try { enc.release() } catch (_: Exception) { }
    }

    companion object {
        internal fun annexOfCsd(buf: java.nio.ByteBuffer?): ByteArray {
            if (buf == null) return ByteArray(0)
            val data = ByteArray(buf.remaining())
            buf.get(data)
            return toAnnexB(data)
        }

        internal fun parseCsd(buf: java.nio.ByteBuffer?): Pair<ByteArray, ByteArray> {
            if (buf == null) return ByteArray(0) to ByteArray(0)
            val data = ByteArray(buf.remaining())
            val pos = buf.position()
            buf.get(data)
            buf.position(pos)
            if (data.size >= 4 && data[0] == 0.toByte() && data[1] == 0.toByte())
                return toAnnexB(data) to ByteArray(0)
            if (data.size > 7 && data[0] == 1.toByte()) {
                var i = 5
                val nSps = data[i].toInt() and 0x1F
                i++
                val sps = java.io.ByteArrayOutputStream()
                repeat(nSps) {
                    if (i + 2 > data.size) return@repeat
                    val len = ((data[i].toInt() and 0xFF) shl 8) or (data[i + 1].toInt() and 0xFF)
                    i += 2
                    if (len <= 0 || i + len > data.size) return@repeat
                    sps.write(0); sps.write(0); sps.write(0); sps.write(1)
                    sps.write(data, i, len)
                    i += len
                }
                if (i >= data.size) return sps.toByteArray() to ByteArray(0)
                val nPps = data[i].toInt() and 0xFF
                i++
                val pps = java.io.ByteArrayOutputStream()
                repeat(nPps) {
                    if (i + 2 > data.size) return@repeat
                    val len = ((data[i].toInt() and 0xFF) shl 8) or (data[i + 1].toInt() and 0xFF)
                    i += 2
                    if (len <= 0 || i + len > data.size) return@repeat
                    pps.write(0); pps.write(0); pps.write(0); pps.write(1)
                    pps.write(data, i, len)
                    i += len
                }
                return sps.toByteArray() to pps.toByteArray()
            }
            return toAnnexB(data) to ByteArray(0)
        }

        fun toAnnexB(data: ByteArray): ByteArray {
            if (data.size >= 4 && data[0] == 0.toByte() && data[1] == 0.toByte() &&
                (data[2] == 1.toByte() || (data[2] == 0.toByte() && data[3] == 1.toByte()))
            ) return data
            val out = java.io.ByteArrayOutputStream(data.size + 16)
            var i = 0
            var any = false
            while (i + 4 <= data.size) {
                val n = ((data[i].toInt() and 0xFF) shl 24) or
                    ((data[i + 1].toInt() and 0xFF) shl 16) or
                    ((data[i + 2].toInt() and 0xFF) shl 8) or
                    (data[i + 3].toInt() and 0xFF)
                i += 4
                if (n <= 0 || i + n > data.size) break
                out.write(0); out.write(0); out.write(0); out.write(1)
                out.write(data, i, n)
                i += n
                any = true
            }
            return if (!any) {
                byteArrayOf(0, 0, 0, 1) + data
            } else {
                out.toByteArray()
            }
        }

        private fun isIdr(nal: ByteArray): Boolean {
            var i = 0
            while (i + 4 < nal.size) {
                if (nal[i] == 0.toByte() && nal[i + 1] == 0.toByte()) {
                    val off = if (nal[i + 2] == 1.toByte()) i + 3
                    else if (nal[i + 2] == 0.toByte() && nal[i + 3] == 1.toByte()) i + 4
                    else -1
                    if (off > 0) {
                        val t = nal[off].toInt() and 0x1F
                        if (t == 5) return true
                        i = off
                    }
                }
                i++
            }
            return false
        }
    }
}
