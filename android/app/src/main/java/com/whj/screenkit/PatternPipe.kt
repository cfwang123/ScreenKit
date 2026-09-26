package com.whj.screenkit

import android.media.MediaCodec
import android.media.MediaCodecInfo
import android.media.MediaFormat
import android.os.SystemClock
import android.util.Log

/** USB 配件测试：自绘动态 NV12，不截屏、不走 TCP/adb。 */
class PatternPipe(
    private val w: Int,
    private val h: Int,
    private val fps: Int,
    private val br: Int,
    private val sink: FrameSink,
    private val onDead: () -> Unit = {},
) {
    private val enc: MediaCodec
    private val nv12: ByteArray
    @Volatile private var running = true
    @Volatile private var stopped = false
    private val th: Thread

    init {
        if (w % 2 != 0 || h % 2 != 0) throw IllegalStateException("odd size")
        nv12 = ByteArray(w * h + w * h / 2)
        enc = openEnc()
        th = Thread({ loop() }, "pat-enc").also { it.start() }
        Log.i("scst", "pattern ${w}x${h}@$fps")
    }

    private fun openEnc(): MediaCodec {
        val c = MediaCodec.createEncoderByType(MediaFormat.MIMETYPE_VIDEO_AVC)
        val fmt = MediaFormat.createVideoFormat(MediaFormat.MIMETYPE_VIDEO_AVC, w, h)
        fmt.setInteger(MediaFormat.KEY_COLOR_FORMAT, MediaCodecInfo.CodecCapabilities.COLOR_FormatYUV420SemiPlanar)
        fmt.setInteger(MediaFormat.KEY_BIT_RATE, br)
        fmt.setInteger(MediaFormat.KEY_FRAME_RATE, fps)
        fmt.setInteger(MediaFormat.KEY_I_FRAME_INTERVAL, 1)
        try {
            fmt.setInteger(MediaFormat.KEY_PROFILE, MediaCodecInfo.CodecProfileLevel.AVCProfileBaseline)
            fmt.setInteger(MediaFormat.KEY_LEVEL, MediaCodecInfo.CodecProfileLevel.AVCLevel31)
        } catch (_: Exception) { }
        c.configure(fmt, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE)
        c.start()
        return c
    }

    private fun fill(n: Int) {
        val ysz = w * h
        val span = (w - BOX_W).coerceAtLeast(1)
        val rise = (h - BOX_H).coerceAtLeast(1)
        val bx = kotlin.math.abs(n * 8 % (span * 2) - span)
        val by = kotlin.math.abs(n * 5 % (rise * 2) - rise)
        val shift = n * 6
        var i = 0
        for (y in 0 until h) {
            for (x in 0 until w) {
                val bar = ((x + shift) / 48) and 5
                var v = when (bar) {
                    0 -> 40
                    1 -> 90
                    2 -> 140
                    3 -> 180
                    4 -> 210
                    else -> 70
                }
                if (x in bx until (bx + BOX_W) && y in by until (by + BOX_H))
                    v = 235
                nv12[i++] = v.toByte()
            }
        }
        val uv = (n * 3) and 255
        var u = ysz
        while (u < nv12.size) {
            nv12[u++] = 128.toByte()
            if (u < nv12.size) nv12[u++] = uv.toByte()
        }
        drawNum(n % 1000)
    }

    private fun drawNum(v: Int) {
        val s = "%03d".format(v)
        var ox = 16
        for (ch in s) {
            val g = DIGITS[ch - '0']
            for (row in 0 until 7) {
                val bits = g[row]
                for (col in 0 until 5) {
                    if ((bits shr (4 - col)) and 1 == 0) continue
                    for (yy in 0 until SCALE) {
                        val y = 16 + row * SCALE + yy
                        if (y !in 0 until h) continue
                        for (xx in 0 until SCALE) {
                            val x = ox + col * SCALE + xx
                            if (x in 0 until w) nv12[y * w + x] = 255.toByte()
                        }
                    }
                }
            }
            ox += 5 * SCALE + SCALE
        }
    }

    private fun loop() {
        val info = MediaCodec.BufferInfo()
        var sps: ByteArray? = null
        var pps: ByteArray? = null
        var sentCfg = false
        var n = 0
        var peer = false
        val tick = 1000L / fps.coerceAtLeast(1)
        try {
            while (running && !stopped) {
                val t0 = SystemClock.elapsedRealtime()
                if (n % 30 == 0) {
                    try {
                        val b = android.os.Bundle()
                        b.putInt(MediaCodec.PARAMETER_KEY_REQUEST_SYNC_FRAME, 0)
                        enc.setParameters(b)
                    } catch (_: Exception) { }
                }
                val inIx = enc.dequeueInputBuffer(20_000)
                if (inIx >= 0) {
                    fill(n)
                    val ib = enc.getInputBuffer(inIx)
                    if (ib != null) {
                        ib.clear()
                        ib.put(nv12)
                        enc.queueInputBuffer(inIx, 0, nv12.size, System.nanoTime() / 1000, 0)
                    }
                    n++
                }
                while (true) {
                    val ix = enc.dequeueOutputBuffer(info, 0)
                    if (ix == MediaCodec.INFO_TRY_AGAIN_LATER) break
                    if (ix == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED) {
                        val fmt = enc.outputFormat
                        val pair = VideoPipe.parseCsd(fmt.getByteBuffer("csd-0"))
                        sps = pair.first
                        pps = pair.second
                        if (pps.isEmpty()) pps = VideoPipe.annexOfCsd(fmt.getByteBuffer("csd-1"))
                        val head = (sps ?: ByteArray(0)) + (pps ?: ByteArray(0))
                        if (head.isNotEmpty()) {
                            sink.send(Proto.T_VIDEO, Proto.withPts(0, head))
                            sentCfg = true
                        }
                        continue
                    }
                    if (ix < 0) continue
                    try {
                        val buf = enc.getOutputBuffer(ix) ?: continue
                        val data = ByteArray(info.size)
                        buf.position(info.offset)
                        buf.limit(info.offset + info.size)
                        buf.get(data)
                        var nal = if (info.flags and MediaCodec.BUFFER_FLAG_CODEC_CONFIG != 0) {
                            val pair = VideoPipe.parseCsd(java.nio.ByteBuffer.wrap(data))
                            sps = pair.first
                            if (pair.second.isNotEmpty()) pps = pair.second
                            sentCfg = true
                            (sps ?: ByteArray(0)) + (pps ?: ByteArray(0))
                        } else {
                            var x = VideoPipe.toAnnexB(data)
                            if ((!sentCfg || n <= 5) && sps != null)
                                x = sps!! + (pps ?: ByteArray(0)) + x
                            sentCfg = true
                            x
                        }
                        if (nal.isNotEmpty()) {
                            if (n <= 3 || n % 30 == 0)
                                Log.i("scst", "pattern $n ${nal.size}b")
                            if (!sink.send(Proto.T_VIDEO, Proto.withPts(Proto.monoUs(info.presentationTimeUs), nal))) {
                                peer = true
                                running = false
                                break
                            }
                        }
                    } finally {
                        try { enc.releaseOutputBuffer(ix, false) } catch (_: Exception) { }
                    }
                }
                val used = SystemClock.elapsedRealtime() - t0
                if (used < tick) try { Thread.sleep(tick - used) } catch (_: Exception) { }
            }
        } catch (ex: Throwable) {
            Log.e("scst", "pattern ${ex.javaClass.simpleName} ${ex.message}")
        }
        if (peer && !stopped) onDead()
    }

    fun stop() {
        stopped = true
        running = false
        try { th.join(800) } catch (_: Exception) { }
        try { enc.stop() } catch (_: Exception) { }
        try { enc.release() } catch (_: Exception) { }
    }

    companion object {
        private const val BOX_W = 96
        private const val BOX_H = 64
        private const val SCALE = 8
        private val DIGITS = arrayOf(
            intArrayOf(0x0E, 0x11, 0x13, 0x15, 0x19, 0x11, 0x0E),
            intArrayOf(0x04, 0x0C, 0x04, 0x04, 0x04, 0x04, 0x0E),
            intArrayOf(0x0E, 0x11, 0x01, 0x06, 0x08, 0x10, 0x1F),
            intArrayOf(0x0E, 0x11, 0x01, 0x06, 0x01, 0x11, 0x0E),
            intArrayOf(0x02, 0x06, 0x0A, 0x12, 0x1F, 0x02, 0x02),
            intArrayOf(0x1F, 0x10, 0x1E, 0x01, 0x01, 0x11, 0x0E),
            intArrayOf(0x06, 0x08, 0x10, 0x1E, 0x11, 0x11, 0x0E),
            intArrayOf(0x1F, 0x01, 0x02, 0x04, 0x08, 0x08, 0x08),
            intArrayOf(0x0E, 0x11, 0x11, 0x0E, 0x11, 0x11, 0x0E),
            intArrayOf(0x0E, 0x11, 0x11, 0x0F, 0x01, 0x02, 0x0C),
        )
    }
}
