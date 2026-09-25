package com.whj.screenkit

import java.net.InetSocketAddress
import java.net.Socket

/** 连接前探测：对电脑 HTTP 端口做短超时 TCP 连通性检查。 */
object PcPing {
    const val TIMEOUT_MS = 1500
    const val RETRY_INTERVAL_MS = 3000L

    fun tcp(host: String, port: Int, timeoutMs: Int = TIMEOUT_MS): Boolean {
        if (host.isEmpty() || port <= 0) return false
        val s = Socket()
        return try {
            s.connect(InetSocketAddress(host, port), timeoutMs)
            true
        } catch (_: Exception) {
            false
        } finally {
            try { s.close() } catch (_: Exception) { }
        }
    }
}
