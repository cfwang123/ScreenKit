package com.whj.screenkit

import android.util.Log
import java.io.InputStream
import java.io.OutputStream
import java.net.InetSocketAddress
import java.net.Socket
import java.security.SecureRandom
import kotlin.experimental.xor

/** 投屏媒体走电脑 HTTP 同端口的 WebSocket `/cast`，不再另开 TCP。 */
class WsSink(ip: String, port: Int) : FrameSink {
    private val sock = Socket()
    private val os: OutputStream
    private val ins: InputStream
    @Volatile private var dead = false
    private val gate = Any()
    private var pendingVideo: ByteArray? = null
    private val ctrl = java.util.concurrent.LinkedBlockingQueue<Pair<Byte, ByteArray>>(256)
    private val wlock = Any()
    private val ath: Thread
    private val vth: Thread
    private val wsIn: WsInput

    init {
        val p = if (port <= 0) SendPorts.HTTP else port
        try {
            sock.tcpNoDelay = true
            sock.connect(InetSocketAddress(ip, p), 4000)
            os = sock.getOutputStream()
            ins = sock.getInputStream()
            handshake(ip, p)
            wsIn = WsInput(ins)
            ath = Thread({ aloop() }, "ws-a").also { it.start() }
            vth = Thread({ vloop() }, "ws-v").also { it.start() }
        } catch (ex: Exception) {
            try { sock.close() } catch (_: Exception) { }
            throw ex
        }
    }

    private fun handshake(host: String, port: Int) {
        val rnd = ByteArray(16)
        SecureRandom().nextBytes(rnd)
        val key = android.util.Base64.encodeToString(rnd, android.util.Base64.NO_WRAP)
        val req = "GET ${Proto.CAST_PATH} HTTP/1.1\r\n" +
            "Host: $host:$port\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Key: $key\r\n" +
            "Sec-WebSocket-Version: 13\r\n\r\n"
        os.write(req.toByteArray(Charsets.US_ASCII))
        os.flush()
        val headers = readHeaders()
        val line = headers.substringBefore("\r\n")
        if (!line.contains(" 101"))
            throw java.io.IOException("WebSocket 握手失败: $line")
        Log.i("scst", "ws ok $host:$port${Proto.CAST_PATH}")
    }

    private fun readHeaders(): String {
        val buf = StringBuilder()
        while (true) {
            val n = ins.read()
            if (n < 0) throw java.io.IOException("握手对端关闭")
            buf.append(n.toChar())
            if (buf.length >= 4 && buf.substring(buf.length - 4) == "\r\n\r\n")
                return buf.toString()
            if (buf.length > 8192) throw java.io.IOException("握手头过长")
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

    private fun writePkt(type: Byte, payload: ByteArray) {
        val raw = Proto.pack(type, payload)
        writeFrame(raw)
    }

    private fun writeFrame(payload: ByteArray) {
        val mask = ByteArray(4)
        SecureRandom().nextBytes(mask)
        val len = payload.size
        val head = when {
            len < 126 -> ByteArray(6).also {
                it[0] = 0x82.toByte()
                it[1] = (0x80 or len).toByte()
                System.arraycopy(mask, 0, it, 2, 4)
            }
            len <= 0xFFFF -> ByteArray(8).also {
                it[0] = 0x82.toByte()
                it[1] = (0x80 or 126).toByte()
                it[2] = (len ushr 8).toByte()
                it[3] = len.toByte()
                System.arraycopy(mask, 0, it, 4, 4)
            }
            else -> ByteArray(14).also {
                it[0] = 0x82.toByte()
                it[1] = (0x80 or 127).toByte()
                var n = len.toLong()
                for (i in 9 downTo 2) {
                    it[i] = n.toByte()
                    n = n ushr 8
                }
                System.arraycopy(mask, 0, it, 10, 4)
            }
        }
        val body = ByteArray(payload.size)
        for (i in payload.indices) body[i] = payload[i] xor mask[i and 3]
        os.write(head)
        os.write(body)
        os.flush()
    }

    private fun aloop() {
        try {
            while (!dead) {
                val p = ctrl.poll(20, java.util.concurrent.TimeUnit.MILLISECONDS) ?: continue
                synchronized(wlock) { writePkt(p.first, p.second) }
            }
        } catch (ex: Exception) {
            Log.w("scst", "ws-a ${ex.javaClass.simpleName} ${ex.message}")
            dead = true
        }
    }

    private fun vloop() {
        try {
            while (!dead) {
                if (!ctrl.isEmpty()) {
                    Thread.sleep(2)
                    continue
                }
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
                    synchronized(wlock) { writePkt(Proto.T_VIDEO, v) }
                } catch (ex: Exception) {
                    Log.w("scst", "ws-v drop ${ex.javaClass.simpleName}")
                }
            }
        } catch (ex: Exception) {
            Log.w("scst", "ws-v ${ex.javaClass.simpleName} ${ex.message}")
            dead = true
        }
    }

    override fun close() {
        dead = true
        try { ath.interrupt() } catch (_: Exception) { }
        try { vth.interrupt() } catch (_: Exception) { }
        try { sock.close() } catch (_: Exception) { }
    }

    override fun input(): InputStream = wsIn
}

class WsInput(private val ins: InputStream) : InputStream() {
    private var hold = ByteArray(0)
    private var o = 0

    override fun read(): Int {
        val b = ByteArray(1)
        val n = read(b, 0, 1)
        return if (n <= 0) -1 else b[0].toInt() and 0xFF
    }

    override fun read(buf: ByteArray, off: Int, len: Int): Int {
        while (o >= hold.size) {
            hold = readFrame() ?: return -1
            o = 0
        }
        val n = minOf(len, hold.size - o)
        System.arraycopy(hold, o, buf, off, n)
        o += n
        return n
    }

    private fun readFrame(): ByteArray? {
        val h = ByteArray(2)
        if (!readfull(h)) return null
        val op = h[0].toInt() and 0x0F
        var length = (h[1].toInt() and 0x7F).toLong()
        val masked = (h[1].toInt() and 0x80) != 0
        if (length == 126L) {
            val ext = ByteArray(2)
            if (!readfull(ext)) return null
            length = ((ext[0].toInt() and 0xFF) shl 8 or (ext[1].toInt() and 0xFF)).toLong()
        } else if (length == 127L) {
            val ext = ByteArray(8)
            if (!readfull(ext)) return null
            length = 0
            for (b in ext) length = (length shl 8) or (b.toInt() and 0xFF).toLong()
        }
        if (length > 8L * 1024 * 1024) return null
        val mask = if (masked) {
            val m = ByteArray(4)
            if (!readfull(m)) return null
            m
        } else null
        val data = ByteArray(length.toInt())
        if (data.isNotEmpty() && !readfull(data)) return null
        if (mask != null)
            for (i in data.indices) data[i] = data[i] xor mask[i and 3]
        if (op == 8) return null
        if (op == 9 || op == 10) return readFrame()
        if (data.isEmpty()) return readFrame()
        return data
    }

    private fun readfull(buf: ByteArray): Boolean {
        var g = 0
        while (g < buf.size) {
            val n = try { ins.read(buf, g, buf.size - g) } catch (_: Exception) { return false }
            if (n <= 0) return false
            g += n
        }
        return true
    }
}
