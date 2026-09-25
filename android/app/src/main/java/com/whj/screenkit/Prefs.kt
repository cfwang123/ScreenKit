package com.whj.screenkit

import android.content.Context
import android.os.Build
import org.json.JSONObject
import java.util.UUID

class Prefs(ctx: Context) {
    private val p = ctx.getSharedPreferences("sendfile", Context.MODE_PRIVATE)

    var lastHost: String
        get() = p.getString("last_host", "") ?: ""
        set(v) { p.edit().putString("last_host", v).apply() }

    var lastPort: Int
        get() = p.getInt("last_port", SendPorts.HTTP)
        set(v) { p.edit().putInt("last_port", v).apply() }

    var lastPcId: String
        get() = p.getString("last_pc_id", "") ?: ""
        set(v) { p.edit().putString("last_pc_id", v).apply() }

    var lastName: String
        get() = p.getString("last_name", "") ?: ""
        set(v) { p.edit().putString("last_name", v).apply() }

    var token: String
        get() = p.getString("token", "") ?: ""
        set(v) { p.edit().putString("token", v).apply() }

    var folderUri: String
        get() = p.getString("folder_uri", "") ?: ""
        set(v) { p.edit().putString("folder_uri", v).apply() }

    /** jpg | png */
    var photoFormat: String
        get() = p.getString("photo_fmt", "jpg") ?: "jpg"
        set(v) { p.edit().putString("photo_fmt", if (v == "png") "png" else "jpg").apply() }

    var photoQuality: Int
        get() = p.getInt("photo_quality", 60).coerceIn(1, 100)
        set(v) { p.edit().putInt("photo_quality", v.coerceIn(1, 100)).apply() }

    var photoLimitSize: Boolean
        get() = p.getBoolean("photo_limit", true)
        set(v) { p.edit().putBoolean("photo_limit", v).apply() }

    var photoMaxPx: Int
        get() = p.getInt("photo_max_px", 2000).coerceIn(64, 16000)
        set(v) { p.edit().putInt("photo_max_px", v.coerceIn(64, 16000)).apply() }

    /** 连接成功后写入电脑下发的拍照压缩参数；缺字段则保留本地缓存。 */
    fun applyPhoto(data: JSONObject?) {
        if (data == null) return
        if (data.has("photoFmt")) photoFormat = data.optString("photoFmt", "jpg")
        if (data.has("photoQuality")) photoQuality = data.optInt("photoQuality", 60)
        if (data.has("photoLimit")) photoLimitSize = data.optBoolean("photoLimit", true)
        if (data.has("photoMaxPx")) photoMaxPx = data.optInt("photoMaxPx", 2000)
    }

    val deviceId: String
        get() {
            var id = p.getString("device_id", "") ?: ""
            if (id.isEmpty()) {
                id = UUID.randomUUID().toString()
                p.edit().putString("device_id", id).apply()
            }
            return id
        }

    val deviceName: String
        get() = Build.MODEL ?: "Android"
}
