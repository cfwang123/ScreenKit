package com.whj.screenkit

import org.json.JSONObject
import java.io.InputStream
import java.io.OutputStream

object Proto {
    const val UDP_PORT = 19518
    const val TCP_PORT = 19519
    const val ABSTRACT = "scst"
    const val MAGIC = 0x53435354
    const val T_VIDEO: Byte = 1
    const val T_AUDIO: Byte = 2
    const val T_JSON: Byte = 3

    fun pack(type: Byte, payload: ByteArray): ByteArray {
        val buf = ByteArray(9 + payload.size)
        buf[0] = (MAGIC ushr 24).toByte()
        buf[1] = (MAGIC ushr 16).toByte()
        buf[2] = (MAGIC ushr 8).toByte()
        buf[3] = MAGIC.toByte()
        buf[4] = type
        buf[5] = (payload.size ushr 24).toByte()
        buf[6] = (payload.size ushr 16).toByte()
        buf[7] = (payload.size ushr 8).toByte()
        buf[8] = payload.size.toByte()
        if (payload.isNotEmpty()) System.arraycopy(payload, 0, buf, 9, payload.size)
        return buf
    }

    fun packJson(obj: JSONObject): ByteArray =
        pack(T_JSON, obj.toString().toByteArray(Charsets.UTF_8))

    fun write(os: OutputStream, type: Byte, payload: ByteArray) {
        val buf = pack(type, payload)
        var o = 0
        while (o < buf.size) {
            val n = minOf(16383, buf.size - o)
            os.write(buf, o, n)
            o += n
        }
        os.flush()
    }

    fun read(ins: InputStream): Pair<Byte, ByteArray>? {
        val hdr = ByteArray(9)
        if (!readfull(ins, hdr)) return null
        val mag = ((hdr[0].toInt() and 0xFF) shl 24) or
            ((hdr[1].toInt() and 0xFF) shl 16) or
            ((hdr[2].toInt() and 0xFF) shl 8) or
            (hdr[3].toInt() and 0xFF)
        if (mag != MAGIC) return null
        val type = hdr[4]
        val len = ((hdr[5].toInt() and 0xFF) shl 24) or
            ((hdr[6].toInt() and 0xFF) shl 16) or
            ((hdr[7].toInt() and 0xFF) shl 8) or
            (hdr[8].toInt() and 0xFF)
        if (len < 0 || len > 8 * 1024 * 1024) return null
        if (len == 0) return type to ByteArray(0)
        val payload = ByteArray(len)
        if (!readfull(ins, payload)) return null
        return type to payload
    }

    private fun readfull(ins: InputStream, buf: ByteArray): Boolean {
        var g = 0
        while (g < buf.size) {
            val n = try { ins.read(buf, g, buf.size - g) } catch (_: Exception) { return false }
            if (n <= 0) return false
            g += n
        }
        return true
    }
}

data class Quality(
    val name: String,
    val shortEdge: Int,
    val fps: Int,
    val bitrate: Int,
) {
    companion object {
        val PRESETS = listOf(
            Quality("流畅 540p", 540, 15, 1_200_000),
            Quality("均衡 720p", 720, 24, 2_500_000),
            Quality("高清 1080p", 1080, 30, 4_000_000),
        )

        fun byName(name: String): Quality =
            PRESETS.firstOrNull { it.name == name } ?: PRESETS[1]
    }

    fun fit(srcW: Int, srcH: Int): Pair<Int, Int> {
        val srcMin = minOf(srcW, srcH).coerceAtLeast(1)
        val s = minOf(1.0, shortEdge.toDouble() / srcMin)
        var w = (srcW * s).toInt() / 16 * 16
        var h = (srcH * s).toInt() / 16 * 16
        if (w < 16) w = 16
        if (h < 16) h = 16
        return w to h
    }
}

data class Peer(val name: String, val ip: String, val tcp: Int, val role: String) {
    override fun toString(): String = "$name  $ip:$tcp  [$role]"
}
