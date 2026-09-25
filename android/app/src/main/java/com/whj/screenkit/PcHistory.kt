package com.whj.screenkit

import android.content.Context
import org.json.JSONArray
import org.json.JSONObject

/** 传文件 / 投屏共用的「最近连接过的电脑」列表（按 host + HTTP 端口去重）。 */
data class PcHistoryEntry(
    val host: String,
    val http: Int,
    val name: String,
    val alias: String,
    val pcId: String,
    val tcp: Int,
    val lastUsed: Long,
) {
    fun displayTitle() = alias.ifEmpty { name.ifEmpty { host } }

    fun displaySub(): String {
        val addr = "${host}:${http}"
        val pc = name.trim()
        return when {
            alias.isNotEmpty() && pc.isNotEmpty() && pc != alias -> "$pc · $addr"
            else -> addr
        }
    }

    fun toPcInfo() = PcInfo(host, http, displayTitle(), pcId)

    fun toPeer() = Peer(
        displayTitle(),
        host,
        if (tcp > 0) tcp else SendPorts.HTTP,
        "pc",
        if (http > 0) http else SendPorts.HTTP,
    )

    fun lineSub() = displaySub()
}

object PcHistory {
    private const val KEY = "pc_history"
    private const val MAX = 12

    fun load(ctx: Context): List<PcHistoryEntry> {
        val p = ctx.getSharedPreferences("sendfile", Context.MODE_PRIVATE)
        var list = parse(p.getString(KEY, "") ?: "")
        if (list.isEmpty()) migrateLast(p, ctx)
        list = parse(p.getString(KEY, "") ?: "")
        return list.sortedByDescending { it.lastUsed }
    }

    fun remember(ctx: Context, entry: PcHistoryEntry) {
        if (entry.host.isEmpty()) return
        val p = ctx.getSharedPreferences("sendfile", Context.MODE_PRIVATE)
        val list = parse(p.getString(KEY, "") ?: "").toMutableList()
        val http = if (entry.http > 0) entry.http else SendPorts.HTTP
        val idx = list.indexOfFirst { it.host == entry.host && it.http == http }
        val old = if (idx >= 0) list.removeAt(idx) else null
        val tcp = when {
            entry.tcp > 0 -> entry.tcp
            old?.tcp ?: 0 > 0 -> old!!.tcp
            else -> SendPorts.HTTP
        }
        val name = entry.name.ifEmpty { old?.name ?: "" }.ifEmpty { entry.host }
        val alias = entry.alias.ifEmpty { old?.alias ?: "" }
        val pcId = entry.pcId.ifEmpty { old?.pcId ?: "" }
        val merged = PcHistoryEntry(entry.host, http, name, alias, pcId, tcp, System.currentTimeMillis())
        list.add(0, merged)
        while (list.size > MAX) list.removeAt(list.size - 1)
        save(p, list)
    }

    fun remove(ctx: Context, host: String, http: Int) {
        val p = ctx.getSharedPreferences("sendfile", Context.MODE_PRIVATE)
        val list = parse(p.getString(KEY, "") ?: "").filter { it.host != host || it.http != http }
        save(p, list)
    }

    fun setAlias(ctx: Context, host: String, http: Int, alias: String) {
        val p = ctx.getSharedPreferences("sendfile", Context.MODE_PRIVATE)
        val list = parse(p.getString(KEY, "") ?: "").map {
            if (it.host == host && it.http == http) it.copy(alias = alias.trim()) else it
        }
        save(p, list)
    }

    private fun migrateLast(p: android.content.SharedPreferences, ctx: Context) {
        val prefs = Prefs(ctx)
        if (prefs.lastHost.isEmpty()) return
        val e = PcHistoryEntry(
            prefs.lastHost,
            if (prefs.lastPort > 0) prefs.lastPort else SendPorts.HTTP,
            prefs.lastName,
            "",
            prefs.lastPcId,
            SendPorts.HTTP,
            System.currentTimeMillis(),
        )
        save(p, listOf(e))
    }

    private fun parse(raw: String): List<PcHistoryEntry> {
        if (raw.isEmpty()) return emptyList()
        return try {
            val arr = JSONArray(raw)
            val out = ArrayList<PcHistoryEntry>()
            for (i in 0 until arr.length()) {
                val o = arr.optJSONObject(i) ?: continue
                val host = o.optString("host", "")
                if (host.isEmpty()) continue
                out.add(
                    PcHistoryEntry(
                        host,
                        o.optInt("http", SendPorts.HTTP),
                        o.optString("name", ""),
                        o.optString("alias", ""),
                        o.optString("pcId", ""),
                        o.optInt("tcp", SendPorts.HTTP),
                        o.optLong("lastUsed", 0L),
                    ),
                )
            }
            out
        } catch (_: Exception) {
            emptyList()
        }
    }

    private fun save(p: android.content.SharedPreferences, list: List<PcHistoryEntry>) {
        val arr = JSONArray()
        for (e in list) {
            arr.put(
                JSONObject()
                    .put("host", e.host)
                    .put("http", e.http)
                    .put("name", e.name)
                    .put("alias", e.alias)
                    .put("pcId", e.pcId)
                    .put("tcp", e.tcp)
                    .put("lastUsed", e.lastUsed),
            )
        }
        p.edit().putString(KEY, arr.toString()).apply()
    }
}
