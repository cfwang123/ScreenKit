package com.whj.screenkit

import android.hardware.usb.UsbAccessory
import android.hardware.usb.UsbManager
import android.net.LocalSocket
import android.net.LocalSocketAddress
import android.os.ParcelFileDescriptor
import android.os.NetworkOnMainThreadException
import android.system.Os
import android.system.OsConstants
import android.system.StructTimeval
import android.util.Log
import java.io.FileInputStream
import java.io.FileOutputStream
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
        try {
            openAbstract().close()
            Log.i(TAG, "probe ok abstract:${Proto.ABSTRACT}")
            return "abstract"
        } catch (ex: Exception) {
            errs.add("abstract:${ex.javaClass.simpleName}:${ex.message}")
        }
        try {
            TcpSink("::1", Proto.TCP_PORT, net = null).close()
            Log.i(TAG, "probe ok ::1")
            return "::1"
        } catch (ex: Exception) {
            errs.add("::1:${ex.javaClass.simpleName}:${ex.message}")
        }
        try {
            TcpSink("127.0.0.1", Proto.TCP_PORT, net = null).close()
            Log.i(TAG, "probe ok 127.0.0.1")
            return "127.0.0.1"
        } catch (ex: Exception) {
            errs.add("v4:${ex.javaClass.simpleName}:${ex.message}")
        }
        val msg = errs.joinToString("; ")
        Log.w(TAG, "probe fail $msg")
        lastErr = msg
        return null
    }

    fun open(): FrameSink {
        var last: Exception? = null
        try {
            return openAbstract()
        } catch (ex: Exception) {
            last = ex
            Log.w(TAG, "open abstract ${ex.message}")
        }
        try {
            return TcpSink("::1", Proto.TCP_PORT, net = null)
        } catch (ex: Exception) {
            last = ex
            Log.w(TAG, "open ::1 ${ex.message}")
        }
        try {
            return TcpSink("127.0.0.1", Proto.TCP_PORT, net = null)
        } catch (ex: Exception) {
            last = ex
            Log.w(TAG, "open 127.0.0.1 ${ex.message}")
        }
        throw IllegalStateException("USB 转发连不上: ${last?.message}", last)
    }

    var lastErr: String = ""
        private set

    private fun openAbstract(): FrameSink = AbstractSink(Proto.ABSTRACT)
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
            sock.connect(LocalSocketAddress(name))
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
    private val pfd: ParcelFileDescriptor = manager.openAccessory(accessory)
        ?: throw IllegalStateException("openAccessory 失败")
    private val os = FileOutputStream(pfd.fileDescriptor)
    private val ins = FileInputStream(pfd.fileDescriptor)
    @Volatile private var dead = false
    private val gate = Any()
    private var pendingVideo: ByteArray? = null
    private val ctrl = java.util.concurrent.LinkedBlockingQueue<Pair<Byte, ByteArray>>(256)
    private val wlock = Any()
    private val ath: Thread
    private val vth: Thread

    init {
        Log.i("scst", "usb accessory opened ${accessory.manufacturer} ${accessory.model}")
        ath = Thread({ aloop() }, "usb-a").also { it.start() }
        vth = Thread({ vloop() }, "usb-v").also { it.start() }
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
                    synchronized(wlock) { Proto.write(os, Proto.T_VIDEO, v) }
                } catch (ex: Exception) {
                    Log.w("scst", "usb-v drop ${ex.javaClass.simpleName}")
                }
            }
        } catch (ex: Exception) {
            Log.w("scst", "usb-v ${ex.javaClass.simpleName} ${ex.message}")
            dead = true
        }
    }

    override fun close() {
        dead = true
        try { ath.interrupt() } catch (_: Exception) { }
        try { vth.interrupt() } catch (_: Exception) { }
        try { os.close() } catch (_: Exception) { }
        try { ins.close() } catch (_: Exception) { }
        try { pfd.close() } catch (_: Exception) { }
    }

    override fun input(): InputStream? = ins
}
