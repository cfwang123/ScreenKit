package com.whj.screenkit

import android.content.Context
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import android.os.Build
import android.util.Log
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.Inet4Address
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.NetworkInterface
import java.net.Socket
import java.util.concurrent.CountDownLatch
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicReference

object UsbLan {
    private const val TAG = "scst"

    @Volatile
    var lastNet: Network? = null
        private set

    @Volatile
    var lastAddr: Inet4Address? = null
        private set

    fun clear() {
        lastNet = null
        lastAddr = null
    }

    fun beacon() {
        val payload = """{"v":1,"app":"screencast","name":"phone","role":"send","tcp":${Proto.TCP_PORT}}"""
            .toByteArray(Charsets.UTF_8)
        DatagramSocket(null).use { sock ->
            sock.broadcast = true
            sock.reuseAddress = true
            try {
                val a = lastAddr
                if (a != null) sock.bind(InetSocketAddress(a, 0))
            } catch (_: Exception) { }
            try {
                sock.send(DatagramPacket(payload, payload.size, InetAddress.getByName("255.255.255.255"), SendPorts.UDP_DISCOVER))
            } catch (_: Exception) { }
        }
    }

    fun pickNet(ctx: Context): Network? {
        lastNet = null
        lastAddr = null
        val cm = ctx.getSystemService(Context.CONNECTIVITY_SERVICE) as? ConnectivityManager
        if (cm != null && Build.VERSION.SDK_INT >= 21) {
            val nets = try { cm.allNetworks } catch (_: Exception) { emptyArray() }
            for (n in nets) {
                val caps = try { cm.getNetworkCapabilities(n) } catch (_: Exception) { null } ?: continue
                val usb = (Build.VERSION.SDK_INT >= 31 && caps.hasTransport(NetworkCapabilities.TRANSPORT_USB)) ||
                    caps.hasTransport(NetworkCapabilities.TRANSPORT_ETHERNET)
                if (!usb) continue
                val lp = try { cm.getLinkProperties(n) } catch (_: Exception) { null }
                lastNet = n
                lastAddr = firstV4(lp)
                Log.i(TAG, "usb-lan net USB/ETH ${lastAddr?.hostAddress}")
                return n
            }
        }
        lastAddr = ifaceAddr()
        if (lastAddr != null || hasUsbIface())
            Log.i(TAG, "usb-lan iface ${lastAddr?.hostAddress}")
        return lastNet
    }

    private fun firstV4(lp: android.net.LinkProperties?): Inet4Address? {
        if (lp == null) return null
        for (la in lp.linkAddresses) {
            val a = la.address as? Inet4Address ?: continue
            if (!a.isLoopbackAddress) return a
        }
        return null
    }

    private fun ifaceAddr(): Inet4Address? {
        val list = try { NetworkInterface.getNetworkInterfaces() } catch (_: Exception) { return null }
        if (list == null) return null
        for (ni in list) {
            try {
                if (!ni.isUp || ni.isLoopback) continue
                val n = (ni.name ?: "").lowercase()
                val usb = n.contains("rndis") || n.contains("ncm") || n.contains("usb") ||
                    n.contains("tether") || n.startsWith("eth")
                if (!usb) continue
                for (a in ni.inetAddresses) {
                    val v4 = a as? Inet4Address ?: continue
                    if (!v4.isLoopbackAddress) return v4
                }
            } catch (_: Exception) { }
        }
        return null
    }

    fun hasUsbIface(): Boolean {
        val list = try { NetworkInterface.getNetworkInterfaces() } catch (_: Exception) { return false }
        if (list == null) return false
        for (ni in list) {
            try {
                if (!ni.isUp || ni.isLoopback) continue
                val n = (ni.name ?: "").lowercase()
                if (n.contains("rndis") || n.contains("ncm") || n.contains("usb") ||
                    n.contains("tether") || n.startsWith("eth")
                ) return true
            } catch (_: Exception) { }
        }
        return false
    }

    fun findPc(ctx: Context): String? {
        lastNet = null
        val cands = LinkedHashMap<String, Network?>()
        collectCm(ctx, cands)
        collectNi(ctx, cands)
        if (cands.isEmpty()) {
            Log.w(TAG, "usb-lan 无候选")
            return null
        }
        Log.i(TAG, "usb-lan 候选 ${cands.size} ${cands.keys.take(8)}")
        val found = AtomicReference<String?>(null)
        val foundNet = AtomicReference<Network?>(null)
        val pool = Executors.newFixedThreadPool(32)
        val latch = CountDownLatch(cands.size)
        for ((ip, net) in cands) {
            pool.execute {
                try {
                    if (found.get() != null) return@execute
                    if (!probe(ip, net)) return@execute
                    if (found.compareAndSet(null, ip)) {
                        foundNet.set(net)
                        lastNet = net
                    }
                } finally {
                    latch.countDown()
                }
            }
        }
        try { latch.await(2500, TimeUnit.MILLISECONDS) } catch (_: Exception) { }
        pool.shutdownNow()
        val ip = found.get()
        if (ip != null) {
            lastNet = foundNet.get()
            Log.i(TAG, "usb-lan 找到 $ip net=${lastNet != null}")
        } else {
            Log.w(TAG, "usb-lan 候选均连不上 HTTP ${SendPorts.HTTP}")
        }
        return ip
    }

    private fun probe(ip: String, net: Network?): Boolean {
        return try {
            Socket().use { s ->
                s.tcpNoDelay = true
                try { net?.bindSocket(s) } catch (_: Exception) { }
                s.connect(InetSocketAddress(ip, SendPorts.HTTP), 250)
                true
            }
        } catch (_: Exception) {
            false
        }
    }

    private fun collectCm(ctx: Context, cands: LinkedHashMap<String, Network?>) {
        if (Build.VERSION.SDK_INT < 21) return
        val cm = ctx.getSystemService(Context.CONNECTIVITY_SERVICE) as? ConnectivityManager ?: return
        val nets = try { cm.allNetworks } catch (_: Exception) { return }
        for (n in nets) {
            val caps = try { cm.getNetworkCapabilities(n) } catch (_: Exception) { null } ?: continue
            val usb = (Build.VERSION.SDK_INT >= 31 && caps.hasTransport(NetworkCapabilities.TRANSPORT_USB)) ||
                caps.hasTransport(NetworkCapabilities.TRANSPORT_ETHERNET)
            if (!usb) continue
            val lp = try { cm.getLinkProperties(n) } catch (_: Exception) { null } ?: continue
            val own = HashSet<String>()
            val prefix = ArrayList<Inet4Address>()
            for (la in lp.linkAddresses) {
                val a = la.address as? Inet4Address ?: continue
                own.add(a.hostAddress ?: continue)
                prefix.add(a)
            }
            for (r in lp.routes) {
                val g = r.gateway as? Inet4Address ?: continue
                val ip = g.hostAddress ?: continue
                if (ip !in own && !g.isLoopbackAddress) put(cands, ip, n)
            }
            for (a in prefix) addSubnet(a, own, n, cands)
        }
    }

    private fun collectNi(ctx: Context, cands: LinkedHashMap<String, Network?>) {
        val cm = if (Build.VERSION.SDK_INT >= 21)
            ctx.getSystemService(Context.CONNECTIVITY_SERVICE) as? ConnectivityManager
        else null
        val list = try { NetworkInterface.getNetworkInterfaces() } catch (_: Exception) { return }
        if (list == null) return
        for (ni in list) {
            try {
                if (!ni.isUp || ni.isLoopback) continue
                val n = (ni.name ?: "").lowercase()
                val usb = n.contains("rndis") || n.contains("ncm") || n.contains("usb") ||
                    n.contains("tether") || n.startsWith("eth")
                if (!usb) continue
                val own = HashSet<String>()
                val addrs = ArrayList<Inet4Address>()
                for (a in ni.inetAddresses) {
                    val v4 = a as? Inet4Address ?: continue
                    if (v4.isLoopbackAddress) continue
                    own.add(v4.hostAddress ?: continue)
                    addrs.add(v4)
                }
                val net = matchNet(cm, own)
                for (a in addrs) addSubnet(a, own, net, cands)
            } catch (_: Exception) { }
        }
    }

    private fun matchNet(cm: ConnectivityManager?, own: Set<String>): Network? {
        if (cm == null || own.isEmpty()) return null
        val nets = try { cm.allNetworks } catch (_: Exception) { return null }
        for (n in nets) {
            val lp = try { cm.getLinkProperties(n) } catch (_: Exception) { null } ?: continue
            for (la in lp.linkAddresses) {
                val ip = (la.address as? Inet4Address)?.hostAddress ?: continue
                if (ip in own) return n
            }
        }
        return null
    }

    private fun addSubnet(a: Inet4Address, own: Set<String>, net: Network?, cands: LinkedHashMap<String, Network?>) {
        val b = a.address ?: return
        if (b.size != 4) return
        val p = "${b[0].toUByte()}.${b[1].toUByte()}.${b[2].toUByte()}"
        val first = intArrayOf(1, 2, 3, 129, 137, 100, 101, 254, 10, 20)
        for (i in first) put(cands, "$p.$i", net)
        for (i in 1..254) {
            val ip = "$p.$i"
            if (ip in own) continue
            put(cands, ip, net)
        }
    }

    private fun put(cands: LinkedHashMap<String, Network?>, ip: String, net: Network?) {
        if (cands.containsKey(ip)) {
            if (cands[ip] == null && net != null) cands[ip] = net
            return
        }
        cands[ip] = net
    }
}
