package com.whj.screenkit

import android.content.Context
import android.net.wifi.WifiManager
import org.json.JSONObject
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.nio.charset.StandardCharsets

data class PcInfo(
    val host: String,
    val port: Int,
    val name: String,
    val pcId: String,
) {
    override fun toString(): String = "$name  $host:$port"
}

object Discover {
    fun scan(ctx: Context, timeoutMs: Int = 3000, udpPort: Int = 17531): List<PcInfo> {
        val found = LinkedHashMap<String, PcInfo>()
        val wifi = ctx.applicationContext.getSystemService(Context.WIFI_SERVICE) as? WifiManager
        val lock = try {
            wifi?.createMulticastLock("sksend")?.also {
                it.setReferenceCounted(false)
                it.acquire()
            }
        } catch (_: Exception) {
            null
        }
        val sock = DatagramSocket()
        try {
            sock.broadcast = true
            sock.soTimeout = 400
            val payload = "SCREENKIT_DISCOVER".toByteArray(StandardCharsets.UTF_8)
            val dest = InetAddress.getByName("255.255.255.255")
            sock.send(DatagramPacket(payload, payload.size, dest, udpPort))
            try {
                sock.send(DatagramPacket(payload, payload.size, dest, udpPort))
            } catch (_: Exception) {
            }
            val buf = ByteArray(2048)
            val deadline = System.currentTimeMillis() + timeoutMs
            while (System.currentTimeMillis() < deadline) {
                try {
                    val pkt = DatagramPacket(buf, buf.size)
                    sock.receive(pkt)
                    val s = String(pkt.data, 0, pkt.length, StandardCharsets.UTF_8).trim()
                    if (!s.startsWith("{")) continue
                    val o = JSONObject(s)
                    val host = pkt.address.hostAddress ?: continue
                    val port = o.optInt("httpPort", 17532)
                    val name = o.optString("name", host)
                    val pcId = o.optString("pcId", "")
                    val key = "$host:$port"
                    found[key] = PcInfo(host, port, name, pcId)
                } catch (_: Exception) {
                    // timeout slice
                }
            }
        } finally {
            try { sock.close() } catch (_: Exception) { }
            try { if (lock?.isHeld == true) lock.release() } catch (_: Exception) { }
        }
        return found.values.toList()
    }
}
