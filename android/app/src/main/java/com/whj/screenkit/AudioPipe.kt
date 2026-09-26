package com.whj.screenkit

import android.media.AudioAttributes
import android.media.AudioFormat
import android.media.AudioPlaybackCaptureConfiguration
import android.media.AudioRecord
import android.media.MediaCodec
import android.media.MediaCodecInfo
import android.media.MediaFormat
import android.media.projection.MediaProjection
import android.os.Build

class AudioPipe(
    mp: MediaProjection,
    private val sink: FrameSink,
) {
    private val rec: AudioRecord
    private val enc: MediaCodec
    private var running = true
    private var nsent = 0
    private var pts = 0L
    private val th: Thread

    init {
        if (Build.VERSION.SDK_INT < 29) throw IllegalStateException("系统内录音需要 Android 10+")
        val cfg = AudioPlaybackCaptureConfiguration.Builder(mp)
            .addMatchingUsage(AudioAttributes.USAGE_MEDIA)
            .addMatchingUsage(AudioAttributes.USAGE_GAME)
            .addMatchingUsage(AudioAttributes.USAGE_UNKNOWN)
            .build()
        val fmt = AudioFormat.Builder()
            .setEncoding(AudioFormat.ENCODING_PCM_16BIT)
            .setSampleRate(48000)
            .setChannelMask(AudioFormat.CHANNEL_IN_STEREO)
            .build()
        val min = AudioRecord.getMinBufferSize(48000, AudioFormat.CHANNEL_IN_STEREO, AudioFormat.ENCODING_PCM_16BIT)
        val bufBytes = maxOf(if (min > 0) min * 2 else 8192, 38400)
        rec = AudioRecord.Builder()
            .setAudioFormat(fmt)
            .setBufferSizeInBytes(bufBytes)
            .setAudioPlaybackCaptureConfig(cfg)
            .build()
        enc = openEnc(bufBytes)
        rec.startRecording()
        android.util.Log.i("scst", "audio start enc=${enc.name} buf=$bufBytes rec=${rec.state}")
        th = Thread({ loop() }, "aenc").also { it.start() }
    }

    private fun openEnc(maxIn: Int): MediaCodec {
        val aac = MediaFormat.createAudioFormat(MediaFormat.MIMETYPE_AUDIO_AAC, 48000, 2)
        aac.setInteger(MediaFormat.KEY_AAC_PROFILE, MediaCodecInfo.CodecProfileLevel.AACObjectLC)
        aac.setInteger(MediaFormat.KEY_BIT_RATE, 128000)
        aac.setInteger(MediaFormat.KEY_MAX_INPUT_SIZE, maxOf(maxIn, 4096))
        try { aac.setInteger(MediaFormat.KEY_IS_ADTS, 0) } catch (_: Exception) { }
        val c = MediaCodec.createEncoderByType(MediaFormat.MIMETYPE_AUDIO_AAC)
        c.configure(aac, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE)
        c.start()
        android.util.Log.i("scst", "aac enc ${c.name}")
        return c
    }

    private fun loop() {
        val hold = ByteArray(4096)
        var holdN = 0
        var holding = false
        val info = MediaCodec.BufferInfo()
        try {
        while (running) {
            if (!holding) {
                val n = try { rec.read(hold, 0, hold.size) } catch (ex: Throwable) {
                    android.util.Log.w("scst", "aread ${ex.message}")
                    break
                }
                if (n == AudioRecord.ERROR_DEAD_OBJECT || n == AudioRecord.ERROR_INVALID_OPERATION) {
                    android.util.Log.w("scst", "aread err $n")
                    break
                }
                if (n > 0) {
                    holdN = n
                    holding = true
                }
            }
            if (holding) {
                val inIx = enc.dequeueInputBuffer(2_000)
                if (inIx >= 0) {
                    val ib = enc.getInputBuffer(inIx)
                    if (ib != null) {
                        ib.clear()
                        ib.put(hold, 0, holdN)
                        val samples = holdN / 4
                        enc.queueInputBuffer(inIx, 0, holdN, pts, 0)
                        pts += samples * 1_000_000L / 48000
                    } else {
                        enc.queueInputBuffer(inIx, 0, 0, pts, 0)
                    }
                    holding = false
                }
            }
            while (true) {
                val outIx = try { enc.dequeueOutputBuffer(info, 0) } catch (_: Exception) { break }
                if (outIx < 0) break
                try {
                    val ob = enc.getOutputBuffer(outIx) ?: continue
                    if (info.size > 0 && info.flags and MediaCodec.BUFFER_FLAG_CODEC_CONFIG == 0) {
                        val raw = ByteArray(info.size)
                        ob.position(info.offset)
                        ob.get(raw)
                        val pkt = if (raw.size >= 2 && raw[0] == 0xFF.toByte() && (raw[1].toInt() and 0xF0) == 0xF0)
                            raw
                        else
                            addAdts(raw, 48000, 2)
                        nsent++
                        if (nsent <= 3 || nsent % 40 == 0)
                            android.util.Log.i("scst", "aac $nsent ${pkt.size}b ${pkt[0].toInt() and 0xFF} ${pkt[1].toInt() and 0xFF}")
                        if (!sink.send(Proto.T_AUDIO, pkt)) {
                            android.util.Log.w("scst", "aac send fail after $nsent")
                            running = false
                            break
                        }
                    }
                } catch (_: Exception) {
                    running = false
                    break
                } finally {
                    try { enc.releaseOutputBuffer(outIx, false) } catch (_: Exception) { }
                }
            }
        }
        } catch (ex: Throwable) {
            android.util.Log.e("scst", "aenc ${ex.javaClass.simpleName} ${ex.message}")
        }
    }

    fun stop() {
        running = false
        try { th.join(400) } catch (_: Exception) { }
        try { rec.stop() } catch (_: Exception) { }
        try { rec.release() } catch (_: Exception) { }
        try { enc.stop() } catch (_: Exception) { }
        try { enc.release() } catch (_: Exception) { }
    }

    companion object {
        fun addAdts(aac: ByteArray, rate: Int, ch: Int): ByteArray {
            val packetLen = aac.size + 7
            val p = ByteArray(packetLen)
            val srIdx = if (rate >= 48000) 3 else 4
            p[0] = 0xFF.toByte()
            p[1] = 0xF9.toByte()
            p[2] = (((2 - 1) shl 6) + (srIdx shl 2) + (ch shr 2)).toByte()
            p[3] = (((ch and 3) shl 6) + (packetLen shr 11)).toByte()
            p[4] = ((packetLen and 0x7FF) shr 3).toByte()
            p[5] = (((packetLen and 7) shl 5) + 0x1F).toByte()
            p[6] = 0xFC.toByte()
            System.arraycopy(aac, 0, p, 7, aac.size)
            return p
        }
    }
}
