package com.whj.screenkit

import android.hardware.usb.UsbAccessory
import android.hardware.usb.UsbManager
import android.net.LocalSocket
import android.net.LocalSocketAddress
import android.os.ParcelFileDescriptor
import android.system.Os
import android.system.OsConstants
import android.system.StructPollfd
import android.system.StructTimeval
import android.util.Log
import org.json.JSONObject
import java.io.FileInputStream
import java.io.InputStream
import java.io.OutputStream
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.Socket

interface FrameSink {
    fun send(type: Byte, payload: ByteArray): Boolean
    fun close()
    fun input(): InputStream? = null
}

object UsbLoop {
    private const val TAG = "scst"

    fun probe(): String? {
        val errs = ArrayList<String>()
        if (pingTcp("127.0.0.1", 1500)) {
            Log.i(TAG, "probe ok 127.0.0.1:${SendPorts.HTTP}")
            return "127.0.0.1"
        } else errs.add("v4")
        val msg = errs.joinToString("; ")
        Log.w(TAG, "probe fail $msg")
        lastErr = msg
        return null
    }

    fun open(): FrameSink {
        return WsSink("127.0.0.1", SendPorts.HTTP)
    }

    var lastErr: String = ""
        private set

    private fun pingTcp(ip: String, ms: Int): Boolean {
        val s = Socket()
        return try {
            s.connect(InetSocketAddress(ip, SendPorts.HTTP), ms)
            true
        } catch (_: Exception) {
            false
        } finally {
            try { s.close() } catch (_: Exception) { }
        }
    }
}

class TcpSink private constructor(private val sock: Socket) : FrameSink {
    private val os: OutputStream
    @Volatile private var dead = false
    private val gate = Any()
    private var pendingVideo: ByteArray? = null
    private val ctrl = java.util.concurrent.LinkedBlockingQueue<Pair<Byte, ByteArray>>(256)
    private val wlock = Any()
    private val ath: Thread
    private val vth: Thread

    constructor(ip: String, port: Int, net: android.net.Network? = UsbLan.lastNet) : this(
        connectSock(ip, port, net)
    )

    init {
        try {
            sock.tcpNoDelay = true
            try { sock.sendBufferSize = 512 * 1024 } catch (_: Exception) { }
            try { sock.receiveBufferSize = 256 * 1024 } catch (_: Exception) { }
            os = sock.getOutputStream()
            setsndto(250)
            android.util.Log.i("scst", "tcp connected ${sock.remoteSocketAddress}")
            ath = Thread({ aloop() }, "tcp-a").also { it.start() }
            vth = Thread({ vloop() }, "tcp-v").also { it.start() }
        } catch (ex: Exception) {
            try { sock.close() } catch (_: Exception) { }
            throw ex
        }
    }

    companion object {
        private fun connectSock(ip: String, port: Int, net: android.net.Network?): Socket {
            val sock = Socket()
            try {
                sock.tcpNoDelay = true
                try { sock.sendBufferSize = 512 * 1024 } catch (_: Exception) { }
                try { sock.receiveBufferSize = 256 * 1024 } catch (_: Exception) { }
                try { net?.bindSocket(sock) } catch (_: Exception) { }
                val addr = if (ip == "::1")
                    InetSocketAddress(InetAddress.getByName("::1"), port)
                else
                    InetSocketAddress(ip, port)
                sock.connect(addr, 4000)
                return sock
            } catch (ex: Exception) {
                try { sock.close() } catch (_: Exception) { }
                throw ex
            }
        }

        fun listen(net: android.net.Network?, port: Int = Proto.TCP_PORT): TcpSink {
            val ss = java.net.ServerSocket()
            val stopBe = java.util.concurrent.atomic.AtomicBoolean(false)
            try {
                ss.reuseAddress = true
                ss.soTimeout = 45000
                ss.bind(InetSocketAddress(port), 1)
                android.util.Log.i("scst", "usb-lan listen ${ss.localSocketAddress} net=${net != null}")
                Thread({
                    while (!stopBe.get()) {
                        try { UsbLan.beacon() } catch (_: Exception) { }
                        try { Thread.sleep(400) } catch (_: Exception) { break }
                    }
                }, "usb-beacon").apply { isDaemon = true; start() }
                val sock = ss.accept()
                sock.tcpNoDelay = true
                try { sock.sendBufferSize = 512 * 1024 } catch (_: Exception) { }
                return TcpSink(sock)
            } finally {
                stopBe.set(true)
                try { ss.close() } catch (_: Exception) { }
            }
        }
    }

    override fun send(type: Byte, payload: ByteArray): Boolean {
        if (dead) return false
        if (type == Proto.T_VIDEO) {
            synchronized(gate) { pendingVideo = payload }
            return true
        }
        if (type == Proto.T_AUDIO) {
            if (!ctrl.offer(type to payload)) {
                ctrl.poll()
                ctrl.offer(type to payload)
            }
            return !dead
        }
        return try {
            ctrl.offer(type to payload, 400, java.util.concurrent.TimeUnit.MILLISECONDS) || !dead
        } catch (_: Exception) {
            !dead
        }
    }

    private fun setsndto(ms: Int) {
        try {
            var fd: java.io.FileDescriptor? = null
            try {
                val m = sock.javaClass.getDeclaredMethod("getFileDescriptor\$")
                m.isAccessible = true
                fd = m.invoke(sock) as? java.io.FileDescriptor
            } catch (_: Exception) {
                val implF = sock.javaClass.getDeclaredField("impl")
                implF.isAccessible = true
                val impl = implF.get(sock)
                val fdF = impl.javaClass.getDeclaredField("fd")
                fdF.isAccessible = true
                fd = fdF.get(impl) as? java.io.FileDescriptor
            }
            if (fd != null)
                Os.setsockoptTimeval(
                    fd,
                    OsConstants.SOL_SOCKET,
                    OsConstants.SO_SNDTIMEO,
                    StructTimeval.fromMillis(ms.toLong()),
                )
        } catch (ex: Exception) {
            Log.w("scst", "sndtimeo ${ex.message}")
        }
    }

    private fun aloop() {
        try {
            while (!dead) {
                val p = ctrl.poll(20, java.util.concurrent.TimeUnit.MILLISECONDS) ?: continue
                synchronized(wlock) { Proto.write(os, p.first, p.second) }
            }
        } catch (ex: Exception) {
            Log.w("scst", "tcp-a ${ex.javaClass.simpleName} ${ex.message}")
            dead = true
        }
    }

    private fun vloop() {
        try {
            while (!dead) {
                val v = synchronized(gate) {
                    val x = pendingVideo
                    pendingVideo = null
                    x
                }
                if (v == null) {
                    Thread.sleep(8)
                    continue
                }
                try {
                    synchronized(wlock) { Proto.write(os, Proto.T_VIDEO, v) }
                } catch (ex: Exception) {
                    Log.w("scst", "tcp-v drop ${ex.javaClass.simpleName}")
                }
            }
        } catch (ex: Exception) {
            Log.w("scst", "tcp-v ${ex.javaClass.simpleName} ${ex.message}")
            dead = true
        }
    }

    override fun close() {
        dead = true
        try { ath.interrupt() } catch (_: Exception) { }
        try { vth.interrupt() } catch (_: Exception) { }
        try { sock.shutdownOutput() } catch (_: Exception) { }
        try { os.close() } catch (_: Exception) { }
        try { sock.close() } catch (_: Exception) { }
    }

    override fun input(): InputStream? = try { sock.getInputStream() } catch (_: Exception) { null }
}

class AbstractSink(name: String) : FrameSink {
    private val sock = LocalSocket()
    private val os: OutputStream
    @Volatile private var dead = false
    private val gate = Any()
    private var pendingVideo: ByteArray? = null
    private val ctrl = java.util.concurrent.LinkedBlockingQueue<Pair<Byte, ByteArray>>(256)
    private val wlock = Any()
    private val ath: Thread
    private val vth: Thread

    init {
        try {
            val err = java.util.concurrent.atomic.AtomicReference<Exception>()
            val th = Thread({
                try { sock.connect(LocalSocketAddress(name)) }
                catch (ex: Exception) { err.set(ex) }
            }, "abs-conn")
            th.start()
            th.join(2000)
            if (th.isAlive) {
                try { sock.close() } catch (_: Exception) { }
                throw java.io.IOException("abstract 连接超时")
            }
            err.get()?.let { throw it }
            os = sock.outputStream
            ath = Thread({ aloop() }, "abs-a").also { it.start() }
            vth = Thread({ vloop() }, "abs-v").also { it.start() }
        } catch (ex: Exception) {
            try { sock.close() } catch (_: Exception) { }
            throw ex
        }
    }

    override fun send(type: Byte, payload: ByteArray): Boolean {
        if (dead) return false
        if (type == Proto.T_VIDEO) {
            synchronized(gate) { pendingVideo = payload }
            return true
        }
        if (type == Proto.T_AUDIO) {
            if (!ctrl.offer(type to payload)) {
                ctrl.poll()
                ctrl.offer(type to payload)
            }
            return !dead
        }
        return try {
            ctrl.offer(type to payload, 400, java.util.concurrent.TimeUnit.MILLISECONDS) || !dead
        } catch (_: Exception) {
            !dead
        }
    }

    private fun aloop() {
        try {
            while (!dead) {
                val p = ctrl.poll(20, java.util.concurrent.TimeUnit.MILLISECONDS) ?: continue
                synchronized(wlock) { Proto.write(os, p.first, p.second) }
            }
        } catch (_: Exception) { dead = true }
    }

    private fun vloop() {
        try {
            while (!dead) {
                val v = synchronized(gate) {
                    val x = pendingVideo
                    pendingVideo = null
                    x
                }
                if (v == null) { Thread.sleep(8); continue }
                try { synchronized(wlock) { Proto.write(os, Proto.T_VIDEO, v) } }
                catch (_: Exception) { }
            }
        } catch (_: Exception) { dead = true }
    }

    override fun close() {
        dead = true
        try { ath.interrupt() } catch (_: Exception) { }
        try { vth.interrupt() } catch (_: Exception) { }
        try { os.close() } catch (_: Exception) { }
        try { sock.close() } catch (_: Exception) { }
    }

    override fun input(): InputStream? = try { sock.inputStream } catch (_: Exception) { null }
}

class UsbSink(manager: UsbManager, accessory: UsbAccessory) : FrameSink {
    private class UsbPkt(val type: Byte, val payload: ByteArray, val done: java.util.concurrent.CountDownLatch? = null)

    private val pfd: ParcelFileDescriptor = manager.openAccessory(accessory)
        ?: throw IllegalStateException("openAccessory 失败")
    private val writeFd = pfd.fileDescriptor
    @Volatile private var dead = false
    private val gate = Any()
    private var pendingVideo: ByteArray? = null
    private val ctrl = java.util.concurrent.LinkedBlockingQueue<UsbPkt>(256)
    private val wlock = Any()
    private val iolock = Any()
    private val ath: Thread
    private val vth: Thread
    private val rth: Thread
    @Volatile private var helloLatch = java.util.concurrent.CountDownLatch(1)
    var onQuality: ((String) -> Unit)? = null
    var onBye: (() -> Unit)? = null

    init {
        Log.i("scst", "usb accessory opened ${accessory.manufacturer} ${accessory.model}")
        try {
            Os.setsockoptTimeval(writeFd, OsConstants.SOL_SOCKET, OsConstants.SO_SNDTIMEO, StructTimeval.fromMillis(400))
        } catch (ex: Exception) {
            Log.w("scst", "usb sockopt ${ex.message}")
        }
        ath = Thread({ aloop() }, "usb-a").also { it.start() }
        vth = Thread({ vloop() }, "usb-v").also { it.start() }
        rth = Thread({ rloop() }, "usb-r")
    }

    fun prepareHelloWait() {
        helloLatch = java.util.concurrent.CountDownLatch(1)
    }

    fun startRead() {
        if (rth.isAlive) return
        rth.start()
    }

    fun sendJsonNow(payload: ByteArray): Boolean {
        return try {
            synchronized(wlock) { rawWrite(Proto.pack(Proto.T_JSON, payload)) }
            true
        } catch (ex: Exception) {
            Log.w("scst", "usb hello write ${ex.message}")
            false
        }
    }

    fun awaitHello(ms: Long = 8000): Boolean {
        val ok = try {
            helloLatch.await(ms, java.util.concurrent.TimeUnit.MILLISECONDS)
        } catch (_: Exception) {
            false
        }
        Log.i("scst", "hello ack=$ok")
        return ok && !dead
    }

    fun handshakeHello(payload: ByteArray, ms: Long = 8000): Boolean {
        prepareHelloWait()
        if (!sendJsonNow(payload)) {
            Log.i("scst", "hello ack=false")
            return false
        }
        startRead()
        return awaitHello(ms)
    }

    override fun send(type: Byte, payload: ByteArray): Boolean {
        if (dead) return false
        if (type == Proto.T_VIDEO) {
            synchronized(gate) { pendingVideo = payload }
            return true
        }
        if (type == Proto.T_AUDIO) {
            if (!ctrl.offer(UsbPkt(type, payload))) {
                ctrl.poll()
                ctrl.offer(UsbPkt(type, payload))
            }
            return !dead
        }
        val done = java.util.concurrent.CountDownLatch(1)
        return try {
            if (!ctrl.offer(UsbPkt(type, payload, done), 400, java.util.concurrent.TimeUnit.MILLISECONDS))
                return false
            done.await(8000, java.util.concurrent.TimeUnit.MILLISECONDS) && !dead
        } catch (_: Exception) {
            false
        }
    }

    private fun aloop() {
        try {
            while (!dead) {
                val p = ctrl.poll(20, java.util.concurrent.TimeUnit.MILLISECONDS) ?: continue
                try {
                    synchronized(wlock) { rawWrite(Proto.pack(p.type, p.payload)) }
                } finally {
                    p.done?.countDown()
                }
            }
        } catch (ex: Exception) {
            Log.w("scst", "usb-a ${ex.javaClass.simpleName} ${ex.message}")
            dead = true
        }
    }

    private fun vloop() {
        try {
            while (!dead) {
                val v = synchronized(gate) {
                    val x = pendingVideo
                    pendingVideo = null
                    x
                }
                if (v == null) {
                    Thread.sleep(8)
                    continue
                }
                try {
                    synchronized(wlock) { rawWrite(Proto.pack(Proto.T_VIDEO, v)) }
                } catch (ex: Exception) {
                    Log.w("scst", "usb-v drop ${ex.javaClass.simpleName}")
                }
            }
        } catch (ex: Exception) {
            Log.w("scst", "usb-v ${ex.javaClass.simpleName} ${ex.message}")
            dead = true
        }
    }

    private val rx = java.io.ByteArrayOutputStream(256)

    private fun rloop() {
        Log.i("scst", "usb-r start")
        try {
            val chunk = ByteArray(16384)
            while (!dead) {
                val n = try {
                    Os.read(writeFd, chunk, 0, chunk.size)
                } catch (ex: android.system.ErrnoException) {
                    if (usbTimeout(ex) && !dead) {
                        try { Thread.sleep(8) } catch (_: Exception) { }
                        continue
                    }
                    Log.w("scst", "usb-r ${ex.javaClass.simpleName} ${ex.message}")
                    break
                }
                if (n <= 0) break
                rx.write(chunk, 0, n)
                while (true) {
                    val pair = takePkt() ?: break
                    if (pair.first != Proto.T_JSON) continue
                    val obj = try {
                        JSONObject(String(pair.second, Charsets.UTF_8))
                    } catch (_: Exception) {
                        continue
                    }
                    when (obj.optString("cmd")) {
                        "hello" -> {
                            Log.i("scst", "pc hello ${pair.second.size}B")
                            helloLatch.countDown()
                        }
                        "bye" -> {
                            Log.i("scst", "pc bye")
                            onBye?.invoke()
                        }
                        "ping" -> {
                            val pong = JSONObject().put("cmd", "pong").put("t", obj.optLong("t"))
                            send(Proto.T_JSON, pong.toString().toByteArray(Charsets.UTF_8))
                        }
                        "quality" -> {
                            val name = obj.optString("name")
                            if (name.isNotEmpty()) onQuality?.invoke(name)
                        }
                    }
                }
            }
        } catch (ex: Exception) {
            Log.w("scst", "usb-r ${ex.javaClass.simpleName} ${ex.message}")
            dead = true
        }
    }

    private fun takePkt(): Pair<Byte, ByteArray>? {
        val buf = rx.toByteArray()
        var i = 0
        while (i + 9 <= buf.size) {
            val mag = ((buf[i].toInt() and 0xFF) shl 24) or
                ((buf[i + 1].toInt() and 0xFF) shl 16) or
                ((buf[i + 2].toInt() and 0xFF) shl 8) or
                (buf[i + 3].toInt() and 0xFF)
            if (mag != Proto.MAGIC) {
                i++
                continue
            }
            val len = ((buf[i + 5].toInt() and 0xFF) shl 24) or
                ((buf[i + 6].toInt() and 0xFF) shl 16) or
                ((buf[i + 7].toInt() and 0xFF) shl 8) or
                (buf[i + 8].toInt() and 0xFF)
            if (len < 0 || len > 8 * 1024 * 1024) {
                i++
                continue
            }
            if (i + 9 + len > buf.size) break
            val payload = buf.copyOfRange(i + 9, i + 9 + len)
            val rest = buf.copyOfRange(i + 9 + len, buf.size)
            rx.reset()
            if (rest.isNotEmpty()) rx.write(rest)
            return buf[i + 4] to payload
        }
        if (i > 0) {
            val rest = buf.copyOfRange(i, buf.size)
            rx.reset()
            if (rest.isNotEmpty()) rx.write(rest)
        }
        return null
    }

    private fun rawWrite(buf: ByteArray) {
        var o = 0
        while (o < buf.size) {
            if (dead) throw java.io.IOException("usb dead")
            val n = synchronized(iolock) {
                try {
                    Os.write(writeFd, buf, o, minOf(16383, buf.size - o))
                } catch (ex: android.system.ErrnoException) {
                    if (usbTimeout(ex) && !dead) 0 else throw ex
                }
            }
            if (n == 0) {
                try { Thread.sleep(8) } catch (_: Exception) { }
                continue
            }
            if (n < 0) throw java.io.IOException("usb write $n")
            o += n
        }
    }

    private fun usbRead(): Pair<Byte, ByteArray>? {
        val hdr = ByteArray(9)
        if (!readfull(hdr)) return null
        while (true) {
            val mag = ((hdr[0].toInt() and 0xFF) shl 24) or
                ((hdr[1].toInt() and 0xFF) shl 16) or
                ((hdr[2].toInt() and 0xFF) shl 8) or
                (hdr[3].toInt() and 0xFF)
            if (mag == Proto.MAGIC) break
            System.arraycopy(hdr, 1, hdr, 0, 8)
            val one = ByteArray(1)
            if (!readfull(one)) return null
            hdr[8] = one[0]
        }
        val type = hdr[4]
        val len = ((hdr[5].toInt() and 0xFF) shl 24) or
            ((hdr[6].toInt() and 0xFF) shl 16) or
            ((hdr[7].toInt() and 0xFF) shl 8) or
            (hdr[8].toInt() and 0xFF)
        if (len < 0 || len > 8 * 1024 * 1024) return null
        if (len == 0) return type to ByteArray(0)
        val payload = ByteArray(len)
        if (!readfull(payload)) return null
        return type to payload
    }

    private fun readfull(buf: ByteArray): Boolean {
        var g = 0
        while (g < buf.size) {
            if (dead) return false
            val n = try {
                Os.read(writeFd, buf, g, buf.size - g)
            } catch (ex: android.system.ErrnoException) {
                    if (usbTimeout(ex) && !dead) 0
                    else {
                        Log.w("scst", "usb-r ${ex.javaClass.simpleName} ${ex.message}")
                        -1
                    }
            }
            if (n == 0) {
                try { Thread.sleep(8) } catch (_: Exception) { }
                continue
            }
            if (n < 0) return false
            if (g == 0) Log.i("scst", "usb-r ${n}B")
            g += n
        }
        return true
    }

    private fun usbTimeout(ex: android.system.ErrnoException): Boolean {
        val n = ex.errno
        return n == OsConstants.EAGAIN || n == OsConstants.EINTR || n == OsConstants.ETIMEDOUT
    }

    override fun close() {
        dead = true
        try { ath.interrupt() } catch (_: Exception) { }
        try { vth.interrupt() } catch (_: Exception) { }
        try { rth.interrupt() } catch (_: Exception) { }
        try { pfd.close() } catch (_: Exception) { }
    }

    override fun input(): InputStream? = null
}
