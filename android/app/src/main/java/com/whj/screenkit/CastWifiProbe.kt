package com.whj.screenkit

import android.util.Log
import org.json.JSONObject
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit

/** 不截屏，只测 WiFi WebSocket `/cast` 与 hello 往返。 */
object CastWifiProbe {
    fun run(ip: String, port: Int = SendPorts.HTTP): String {
        var sink: WsSink? = null
        try {
            sink = WsSink(ip, port)
            val latch = CountDownLatch(1)
            val ins = sink.input() ?: return "wifi probe no input"
            val reader = Thread({
                try {
                    while (true) {
                        val pair = Proto.read(ins) ?: break
                        if (pair.first != Proto.T_JSON) continue
                        val obj = JSONObject(String(pair.second, Charsets.UTF_8))
                        if (obj.optString("cmd") == "hello") {
                            Log.i("scst", "pc hello wifi probe")
                            latch.countDown()
                            break
                        }
                    }
                } catch (ex: Exception) {
                    Log.w("scst", "wifi probe read ${ex.message}")
                }
            }, "wifi-probe-r")
            reader.isDaemon = true
            reader.start()
            Thread.sleep(40)
            val hello = JSONObject()
                .put("cmd", "hello")
                .put("name", "probe")
                .put("w", 64)
                .put("h", 64)
                .put("fps", 15)
                .put("via", "wifi")
            if (!sink.send(Proto.T_JSON, hello.toString().toByteArray(Charsets.UTF_8)))
                return "wifi probe send fail"
            val ack = latch.await(8000, TimeUnit.MILLISECONDS)
            return if (ack) "wifi probe ok $ip:$port" else "wifi probe hello ack=false"
        } catch (ex: Exception) {
            return "wifi probe fail ${ex.message}"
        } finally {
            try { sink?.close() } catch (_: Exception) { }
        }
    }
}
