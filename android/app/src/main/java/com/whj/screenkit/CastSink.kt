package com.whj.screenkit

import android.hardware.usb.UsbAccessory
import android.hardware.usb.UsbManager
import android.net.LocalSocket
import android.net.LocalSocketAddress
import android.os.ParcelFileDescriptor
import android.os.NetworkOnMainThreadException
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
            TcpSink("::1", Proto.TCP_PORT).close()
            Log.i(TAG, "probe ok ::1")
            return "::1"
        } catch (ex: Exception) {
            errs.add("::1:${ex.javaClass.simpleName}:${ex.message}")
        }
        try {
            TcpSink("127.0.0.1", Proto.TCP_PORT).close()
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
            return TcpSink("::1", Proto.TCP_PORT)
        } catch (ex: Exception) {
            last = ex
            Log.w(TAG, "open ::1 ${ex.message}")
        }
        try {
            return TcpSink("127.0.0.1", Proto.TCP_PORT)
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

class TcpSink(ip: String, port: Int) : FrameSink {
    private val sock = Socket()
    private val os: OutputStream
    @Volatile private var dead = false
    private val gate = Any()
    private var pendingVideo: ByteArray? = null
    private val ctrl = java.util.concurrent.LinkedBlockingQueue<Pair<Byte, ByteArray>>(32)
    private val th: Thread

    init {
        try {
            sock.tcpNoDelay = true
            try { sock.sendBufferSize = 512 * 1024 } catch (_: Exception) { }
            try { sock.receiveBufferSize = 256 * 1024 } catch (_: Exception) { }
            val addr = if (ip == "::1")
                InetSocketAddress(InetAddress.getByName("::1"), port)
            else
                InetSocketAddress(ip, port)
            sock.connect(addr, 4000)
            os = sock.getOutputStream()
            th = Thread({ loop() }, "tcp-send").also { it.start() }
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
        if (ctrl.offer(type to payload)) return true
        if (type == Proto.T_AUDIO) return true
        return try {
            ctrl.offer(type to payload, 200, java.util.concurrent.TimeUnit.MILLISECONDS)
        } catch (_: Exception) {
            !dead
        }
    }

    private fun loop() {
        try {
            while (!dead) {
                val p = ctrl.poll()
                if (p != null) Proto.write(os, p.first, p.second)
                val v = synchronized(gate) {
                    val x = pendingVideo
                    pendingVideo = null
                    x
                }
                if (v != null) {
                    Proto.write(os, Proto.T_VIDEO, v)
                    continue
                }
                if (p == null) ctrl.poll(15, java.util.concurrent.TimeUnit.MILLISECONDS)
            }
        } catch (_: Exception) {
            dead = true
        }
    }

    override fun close() {
        dead = true
        try { th.interrupt() } catch (_: Exception) { }
        try { os.close() } catch (_: Exception) { }
        try { sock.close() } catch (_: Exception) { }
    }

    override fun input(): InputStream? = try { sock.getInputStream() } catch (_: Exception) { null }
}

class AbstractSink(name: String) : FrameSink {
    private val sock = LocalSocket()
    private val os: OutputStream
    @Volatile private var dead = false

    init {
        try {
            sock.connect(LocalSocketAddress(name))
            os = sock.outputStream
        } catch (ex: Exception) {
            try { sock.close() } catch (_: Exception) { }
            throw ex
        }
    }

    @Synchronized
    override fun send(type: Byte, payload: ByteArray): Boolean {
        if (dead) return false
        return try {
            Proto.write(os, type, payload)
            true
        } catch (_: Exception) {
            dead = true
            false
        }
    }

    override fun close() {
        dead = true
        try { os.close() } catch (_: Exception) { }
        try { sock.close() } catch (_: Exception) { }
    }

    override fun input(): InputStream? = try { sock.inputStream } catch (_: Exception) { null }
}

class UsbSink(manager: UsbManager, accessory: UsbAccessory) : FrameSink {
    private val pfd: ParcelFileDescriptor = manager.openAccessory(accessory)
        ?: throw IllegalStateException("openAccessory 失败")
    private val os = FileOutputStream(pfd.fileDescriptor)
    @Volatile private var dead = false

    @Synchronized
    override fun send(type: Byte, payload: ByteArray): Boolean {
        if (dead) return false
        return try {
            Proto.write(os, type, payload)
            true
        } catch (_: Exception) {
            dead = true
            false
        }
    }

    override fun close() {
        dead = true
        try { os.close() } catch (_: Exception) { }
        try { pfd.close() } catch (_: Exception) { }
    }
}
