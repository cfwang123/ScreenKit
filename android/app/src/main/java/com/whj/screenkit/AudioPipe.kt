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
        rec = AudioRecord.Builder()
            .setAudioFormat(fmt)
            .setBufferSizeInBytes(min * 2)
            .setAudioPlaybackCaptureConfig(cfg)
            .build()
        val aac = MediaFormat.createAudioFormat(MediaFormat.MIMETYPE_AUDIO_AAC, 48000, 2)
        aac.setInteger(MediaFormat.KEY_AAC_PROFILE, MediaCodecInfo.CodecProfileLevel.AACObjectLC)
        aac.setInteger(MediaFormat.KEY_BIT_RATE, 128000)
        aac.setInteger(MediaFormat.KEY_MAX_INPUT_SIZE, min)
        enc = MediaCodec.createEncoderByType(MediaFormat.MIMETYPE_AUDIO_AAC)
        enc.configure(aac, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE)
        enc.start()
        rec.startRecording()
        th = Thread({ loop() }, "aenc").also { it.start() }
    }

    private fun loop() {
        val pcm = ByteArray(4096)
        val info = MediaCodec.BufferInfo()
        while (running) {
            val n = try { rec.read(pcm, 0, pcm.size) } catch (_: Exception) { break }
            if (n > 0) {
                val inIx = enc.dequeueInputBuffer(10_000)
                if (inIx >= 0) {
                    val ib = enc.getInputBuffer(inIx)
                    ib?.clear()
                    ib?.put(pcm, 0, n)
                    enc.queueInputBuffer(inIx, 0, n, System.nanoTime() / 1000, 0)
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
                        if (!sink.send(Proto.T_AUDIO, addAdts(raw, 48000, 2))) {
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
