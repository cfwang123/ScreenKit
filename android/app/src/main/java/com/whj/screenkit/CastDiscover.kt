package com.whj.screenkit

import android.content.Context
import android.net.wifi.WifiManager
import android.util.Log
import org.json.JSONObject
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress

object CastDiscover {
    private const val TAG = "Screencast"

    fun scan(ctx: Context, timeoutMs: Int = 4000): List<Peer> {
        val found = LinkedHashMap<String, Peer>()
        val wifi = ctx.applicationContext.getSystemService(Context.WIFI_SERVICE) as? WifiManager
        val lock = try {
            wifi?.createMulticastLock("skcast")?.also {
                it.setReferenceCounted(false)
                it.acquire()
            }
        } catch (_: Exception) {
            null
        }
        val sock = DatagramSocket(null)
        try {
            sock.reuseAddress = true
            sock.broadcast = true
            try {
                sock.bind(java.net.InetSocketAddress(0))
            } catch (_: Exception) {
                sock.bind(java.net.InetSocketAddress(Proto.UDP_PORT))
            }
            sock.soTimeout = 400
            val hello = """{"v":1,"app":"screencast","name":"phone","role":"send","tcp":0}"""
                .toByteArray(Charsets.UTF_8)
            val dests = ArrayList<InetAddress>()
            dests.add(InetAddress.getByName("255.255.255.255"))
            wifiBroadcast(wifi)?.let { dests.add(it) }
            ifaceBroadcasts(dests)
            for (d in dests) {
                try {
                    sock.send(DatagramPacket(hello, hello.size, d, Proto.UDP_PORT))
                } catch (ex: Exception) {
                    Log.w(TAG, "discover send $d: ${ex.message}")
                }
            }
            val buf = ByteArray(2048)
            val deadline = System.currentTimeMillis() + timeoutMs
            while (System.currentTimeMillis() < deadline) {
                try {
                    val pkt = DatagramPacket(buf, buf.size)
                    sock.receive(pkt)
                    val s = String(pkt.data, 0, pkt.length, Charsets.UTF_8).trim()
                    if (!s.startsWith("{")) continue
                    val o = JSONObject(s)
                    if (o.optString("app") != "screencast") continue
                    val ip = pkt.address.hostAddress ?: continue
                    val tcp = o.optInt("tcp", Proto.TCP_PORT)
                    val name = o.optString("name", ip)
                    val role = o.optString("role", "")
                    if (role == "send") continue
                    found["$ip:$tcp"] = Peer(name, ip, tcp, role, o.optInt("http", 1224).let { if (it > 0) it else 1224 })
                    Log.i(TAG, "discover $name $ip:$tcp $role")
                } catch (_: Exception) {
                }
            }
        } finally {
            try { sock.close() } catch (_: Exception) { }
            try { if (lock?.isHeld == true) lock.release() } catch (_: Exception) { }
        }
        return found.values.toList()
    }

    @Suppress("DEPRECATION")
    private fun wifiBroadcast(wifi: WifiManager?): InetAddress? {
        val ip = wifi?.connectionInfo?.ipAddress ?: return null
        if (ip == 0) return null
        val b = byteArrayOf(
            (ip and 0xFF).toByte(),
            (ip shr 8 and 0xFF).toByte(),
            (ip shr 16 and 0xFF).toByte(),
            0xFF.toByte(),
        )
        return try {
            InetAddress.getByAddress(b)
        } catch (_: Exception) {
            null
        }
    }

    private fun ifaceBroadcasts(dests: ArrayList<InetAddress>) {
        val list = try { java.net.NetworkInterface.getNetworkInterfaces() } catch (_: Exception) { return }
        if (list == null) return
        for (ni in list) {
            try {
                if (!ni.isUp || ni.isLoopback) continue
                for (a in ni.interfaceAddresses) {
                    val b = a.broadcast ?: continue
                    dests.add(b)
                }
            } catch (_: Exception) { }
        }
    }
}
