package com.whj.screenkit

import android.content.Context
import android.os.Build
import java.util.UUID

class Prefs(ctx: Context) {
    private val p = ctx.getSharedPreferences("sendfile", Context.MODE_PRIVATE)

    var lastHost: String
        get() = p.getString("last_host", "") ?: ""
        set(v) { p.edit().putString("last_host", v).apply() }

    var lastPort: Int
        get() = p.getInt("last_port", 17532)
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
