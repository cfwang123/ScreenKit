package com.whj.screenkit

import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONArray
import org.json.JSONObject
import java.io.InputStream
import java.net.URLEncoder
import java.util.concurrent.TimeUnit

data class RemoteItem(
    val name: String,
    val path: String,
    val dir: Boolean,
    val size: Long,
    val mtime: Long,
)

data class TextMsg(val id: Long, val text: String)

class Api(
    var host: String,
    var port: Int,
    var deviceId: String,
    var token: String,
    var deviceName: String,
) {
    private val http = OkHttpClient.Builder()
        .connectTimeout(8, TimeUnit.SECONDS)
        .readTimeout(120, TimeUnit.SECONDS)
        .writeTimeout(120, TimeUnit.SECONDS)
        .build()

    private fun base() = "http://$host:$port"

    private fun req(path: String): Request.Builder {
        val b = Request.Builder().url("${base()}$path")
        b.header("X-Device-Id", deviceId)
        if (token.isNotEmpty())
            b.header("Authorization", "Bearer $token")
        return b
    }

    fun info(): JSONObject {
        val r = http.newCall(req("/api/sendfile/info").get().build()).execute()
        return parse(r.body?.string() ?: "", r.code)
    }

    fun pair(): JSONObject {
        val body = JSONObject()
            .put("id", deviceId)
            .put("name", deviceName)
            .toString()
            .toRequestBody("application/json; charset=utf-8".toMediaType())
        val b = Request.Builder().url("${base()}/api/sendfile/pair").post(body)
        b.header("X-Device-Id", deviceId)
        if (token.isNotEmpty())
            b.header("Authorization", "Bearer $token")
        val r = http.newCall(b.build()).execute()
        val obj = parse(r.body?.string() ?: "", r.code)
        if (obj.optInt("code") == 100) {
            val data = obj.optJSONObject("data")
            val t = data?.optString("token") ?: ""
            if (t.isNotEmpty()) token = t
        }
        return obj
    }

    fun list(rel: String, deep: Boolean = false): List<RemoteItem> {
        val q = URLEncoder.encode(rel, "UTF-8")
        val deepQ = if (deep) "&deep=1" else ""
        val obj = get("/api/sendfile/list?path=$q$deepQ")
        if (obj.optInt("code") != 100) throw RuntimeException(obj.optString("data"))
        val data = obj.optJSONObject("data") ?: JSONObject()
        val arr = data.optJSONArray("items") ?: JSONArray()
        val out = ArrayList<RemoteItem>()
        for (i in 0 until arr.length()) {
            val it = arr.optJSONObject(i) ?: continue
            out.add(
                RemoteItem(
                    name = it.optString("name"),
                    path = it.optString("path"),
                    dir = it.optBoolean("dir"),
                    size = it.optLong("size"),
                    mtime = it.optLong("mtime"),
                ),
            )
        }
        return out
    }

    fun delete(rel: String) {
        val q = URLEncoder.encode(rel, "UTF-8")
        val obj = parse(
            http.newCall(req("/api/sendfile/delete?path=$q").delete().build()).execute().body?.string() ?: "",
            200,
        )
        if (obj.optInt("code") != 100) throw RuntimeException(obj.optString("data"))
    }

    fun upload(rel: String, bytes: ByteArray) {
        val q = URLEncoder.encode(rel, "UTF-8")
        val body = bytes.toRequestBody("application/octet-stream".toMediaType())
        val obj = parse(
            http.newCall(req("/api/sendfile/upload?path=$q").post(body).build()).execute().body?.string() ?: "",
            200,
        )
        if (obj.optInt("code") != 100) throw RuntimeException(obj.optString("data"))
    }

    fun download(rel: String): ByteArray {
        val q = URLEncoder.encode(rel, "UTF-8")
        val r = http.newCall(req("/api/sendfile/download?path=$q").get().build()).execute()
        val ct = r.header("Content-Type") ?: ""
        val raw = r.body?.bytes() ?: ByteArray(0)
        if (ct.contains("json")) {
            val obj = parse(String(raw, Charsets.UTF_8), r.code)
            throw RuntimeException(obj.optString("data", "下载失败"))
        }
        if (!r.isSuccessful) throw RuntimeException("下载 HTTP ${r.code}")
        return raw
    }

    fun downloadStream(rel: String): Pair<InputStream, Long> {
        val q = URLEncoder.encode(rel, "UTF-8")
        val r = http.newCall(req("/api/sendfile/download?path=$q").get().build()).execute()
        val ct = r.header("Content-Type") ?: ""
        if (ct.contains("json") || !r.isSuccessful) {
            val s = r.body?.string() ?: ""
            val obj = parse(s, r.code)
            throw RuntimeException(obj.optString("data", "下载失败"))
        }
        val len = r.body?.contentLength() ?: -1
        val ins = r.body?.byteStream() ?: throw RuntimeException("空响应")
        return ins to len
    }

    fun sendText(text: String) {
        val body = JSONObject().put("text", text).toString()
            .toRequestBody("application/json; charset=utf-8".toMediaType())
        val obj = parse(
            http.newCall(req("/api/sendfile/text").post(body).build()).execute().body?.string() ?: "",
            200,
        )
        if (obj.optInt("code") != 100) throw RuntimeException(obj.optString("data"))
    }

    fun pullText(since: Long): List<TextMsg> {
        val obj = get("/api/sendfile/text?since=$since")
        if (obj.optInt("code") != 100) throw RuntimeException(obj.optString("data"))
        val data = obj.opt("data")
        val arr = data as? JSONArray ?: JSONArray()
        val out = ArrayList<TextMsg>()
        for (i in 0 until arr.length()) {
            val it = arr.optJSONObject(i) ?: continue
            out.add(TextMsg(it.optLong("id"), it.optString("text")))
        }
        return out
    }

    private fun get(path: String): JSONObject {
        val r = http.newCall(req(path).get().build()).execute()
        return parse(r.body?.string() ?: "", r.code)
    }

    private fun parse(s: String, httpCode: Int): JSONObject {
        if (s.isBlank()) {
            if (httpCode == 401 || httpCode == 403)
                return JSONObject().put("code", httpCode).put("data", "未授权")
            return JSONObject().put("code", 900).put("data", "空响应 HTTP $httpCode")
        }
        val obj = JSONObject(s)
        if (httpCode == 401 || httpCode == 403)
            obj.put("code", httpCode)
        return obj
    }
}
