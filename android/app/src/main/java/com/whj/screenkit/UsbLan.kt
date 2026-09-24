package com.whj.screenkit

import android.content.Context
import android.net.ConnectivityManager
import android.net.NetworkCapabilities
import android.os.Build
import java.net.Inet4Address
import java.net.InetSocketAddress
import java.net.NetworkInterface
import java.net.Socket

object UsbLan {
    fun findPc(ctx: Context): String? {
        val cands = LinkedHashSet<String>()
        collectCm(ctx, cands)
        collectNi(cands)
        for (ip in cands) {
            if (probe(ip)) return ip
        }
        return null
    }

    private fun probe(ip: String): Boolean {
        return try {
            Socket().use { s ->
                s.tcpNoDelay = true
                s.connect(InetSocketAddress(ip, Proto.TCP_PORT), 500)
                true
            }
        } catch (_: Exception) {
            false
        }
    }

    private fun collectCm(ctx: Context, cands: LinkedHashSet<String>) {
        if (Build.VERSION.SDK_INT < 21) return
        val cm = ctx.getSystemService(Context.CONNECTIVITY_SERVICE) as? ConnectivityManager ?: return
        val nets = try { cm.allNetworks } catch (_: Exception) { return }
        for (n in nets) {
            val caps = try { cm.getNetworkCapabilities(n) } catch (_: Exception) { null } ?: continue
            val usb = if (Build.VERSION.SDK_INT >= 31)
                caps.hasTransport(NetworkCapabilities.TRANSPORT_USB)
            else false
            if (!usb) continue
            val lp = try { cm.getLinkProperties(n) } catch (_: Exception) { null } ?: continue
            val own = HashSet<String>()
            for (la in lp.linkAddresses) {
                val a = la.address as? Inet4Address ?: continue
                own.add(a.hostAddress ?: continue)
                addNeighbors(a, cands)
            }
            for (r in lp.routes) {
                val g = r.gateway as? Inet4Address ?: continue
                val ip = g.hostAddress ?: continue
                if (ip !in own && !g.isLoopbackAddress) cands.add(ip)
            }
            cands.removeAll(own)
        }
    }

    private fun collectNi(cands: LinkedHashSet<String>) {
        val list = try { NetworkInterface.getNetworkInterfaces() } catch (_: Exception) { return }
        if (list == null) return
        for (ni in list) {
            try {
                if (!ni.isUp || ni.isLoopback) continue
                val n = (ni.name ?: "").lowercase()
                val usb = n.contains("rndis") || n.contains("ncm") || n.contains("usb") ||
                    n.contains("tether") || n == "eth0"
                if (!usb) continue
                val own = HashSet<String>()
                for (a in ni.inetAddresses) {
                    val v4 = a as? Inet4Address ?: continue
                    if (v4.isLoopbackAddress) continue
                    own.add(v4.hostAddress ?: continue)
                    addNeighbors(v4, cands)
                }
                cands.removeAll(own)
            } catch (_: Exception) { }
        }
    }

    private fun addNeighbors(a: Inet4Address, cands: LinkedHashSet<String>) {
        val b = a.address ?: return
        if (b.size != 4) return
        val p = "${b[0].toUByte()}.${b[1].toUByte()}.${b[2].toUByte()}"
        cands.add("$p.1")
        cands.add("$p.2")
        cands.add("$p.129")
    }
}
