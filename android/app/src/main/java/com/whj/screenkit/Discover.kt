package com.whj.screenkit

import android.content.Context
import android.net.ConnectivityManager
import android.net.NetworkCapabilities
import android.net.wifi.WifiManager
import android.util.Log
import org.json.JSONObject
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.InetSocketAddress
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
    private const val TAG = "SKSend"

    fun scan(ctx: Context, timeoutMs: Int = 4000, udpPort: Int = SendPorts.UDP_DISCOVER): List<PcInfo> {
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
        val sock = DatagramSocket(null)
        try {
            sock.reuseAddress = true
            sock.broadcast = true
            bindWifi(ctx, sock)
            try {
                sock.bind(InetSocketAddress(0))
            } catch (_: Exception) {
                sock.bind(InetSocketAddress(udpPort))
            }
            sock.soTimeout = 400
            val payload = "SCREENKIT_DISCOVER".toByteArray(StandardCharsets.UTF_8)
            val dests = ArrayList<InetAddress>()
            dests.add(InetAddress.getByName("255.255.255.255"))
            wifiBroadcast(wifi)?.let { dests.add(it) }
            ifaceBroadcasts(dests)
            try {
                val last = Prefs(ctx).lastHost.trim()
                if (last.isNotEmpty()) dests.add(InetAddress.getByName(last))
            } catch (_: Exception) { }
            for (d in dests) {
                try {
                    sock.send(DatagramPacket(payload, payload.size, d, udpPort))
                    Log.i(TAG, "discover send $d:$udpPort")
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
                    val s = String(pkt.data, 0, pkt.length, StandardCharsets.UTF_8).trim()
                    if (!s.startsWith("{")) continue
                    val o = JSONObject(s)
                    val host = pkt.address.hostAddress ?: continue
                    var port = o.optInt("httpPort", 0)
                    if (port <= 0) port = SendPorts.HTTP
                    val name = o.optString("name", host)
                    val pcId = o.optString("pcId", "")
                    val key = "$host:$port"
                    found[key] = PcInfo(host, port, name, pcId)
                    Log.i(TAG, "discover $name $host:$port")
                } catch (_: Exception) {
                }
            }
        } finally {
            try { sock.close() } catch (_: Exception) { }
            try { if (lock?.isHeld == true) lock.release() } catch (_: Exception) { }
        }
        return found.values.toList()
    }

    private fun bindWifi(ctx: Context, sock: DatagramSocket) {
        try {
            val cm = ctx.getSystemService(Context.CONNECTIVITY_SERVICE) as? ConnectivityManager ?: return
            val net = cm.allNetworks.firstOrNull { n ->
                val cap = cm.getNetworkCapabilities(n) ?: return@firstOrNull false
                cap.hasTransport(NetworkCapabilities.TRANSPORT_WIFI)
            } ?: return
            net.bindSocket(sock)
            Log.i(TAG, "discover bind wifi")
        } catch (ex: Exception) {
            Log.w(TAG, "discover bind wifi ${ex.message}")
        }
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
                    if (!dests.contains(b)) dests.add(b)
                }
            } catch (_: Exception) { }
        }
    }
}
