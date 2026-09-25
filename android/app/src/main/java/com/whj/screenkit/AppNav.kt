package com.whj.screenkit

import android.content.ActivityNotFoundException
import android.content.Context
import android.content.Intent
import android.net.Uri

/** 传文件 / 投屏互跳、网页文件管理（/m）等。 */
object AppNav {
    fun webManagerUrl(host: String, port: Int): String {
        val h = host.trim()
        if (h.isEmpty()) return ""
        val hostPart = if (h.contains(':') && !h.startsWith('[')) "[$h]" else h
        return "http://$hostPart:$port/m"
    }

    fun openWebManager(ctx: Context, host: String, port: Int): Boolean {
        val url = webManagerUrl(host, port)
        if (url.isEmpty()) return false
        return openUrl(ctx, url)
    }

    fun openUrl(ctx: Context, url: String): Boolean {
        try {
            ctx.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url)))
            return true
        } catch (_: ActivityNotFoundException) {
            return false
        }
    }

    fun openCast(ctx: Context, host: String = "", http: Int = 0) {
        val i = Intent(ctx, CastActivity::class.java)
        val h = host.trim()
        if (h.isNotEmpty()) {
            val s = if (http > 0 && http != SendPorts.HTTP) "$h:$http" else h
            i.putExtra("scst_ip", s)
        }
        ctx.startActivity(i)
    }
}
