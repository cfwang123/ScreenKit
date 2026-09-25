package com.whj.screenkit

import android.content.Context
import android.content.pm.PackageManager
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient
import okhttp3.Request
import org.json.JSONArray
import org.json.JSONObject
import java.util.concurrent.TimeUnit
import java.util.regex.Pattern

/** 与 PC {@code AppUpdater.CheckLatestApkAsync} 相同：查 GitHub Releases 最新 APK。 */
object AppUpdate {
    private const val REPO_API = "https://api.github.com/repos/cfwang123/ScreenKit/releases/latest"
    private const val REPO_LIST = "https://api.github.com/repos/cfwang123/ScreenKit/releases?per_page=30"
    private const val REPO_PAGE = "https://github.com/cfwang123/ScreenKit/releases"

    private val verPat = Pattern.compile("\\d+(?:\\.\\d+){0,3}")

    private val http = OkHttpClient.Builder()
        .connectTimeout(12, TimeUnit.SECONDS)
        .readTimeout(20, TimeUnit.SECONDS)
        .build()

    data class Result(
        val current: String,
        val latest: String,
        val hasUpdate: Boolean,
        val hasApk: Boolean,
        val downloadUrl: String,
        val htmlUrl: String,
        val sizeBytes: Long,
        val assetName: String?,
    )

    fun currentVersion(ctx: Context): String {
        return try {
            val pi = if (android.os.Build.VERSION.SDK_INT >= 33) {
                ctx.packageManager.getPackageInfo(
                    ctx.packageName,
                    PackageManager.PackageInfoFlags.of(0),
                )
            } else {
                @Suppress("DEPRECATION")
                ctx.packageManager.getPackageInfo(ctx.packageName, 0)
            }
            pi.versionName ?: ""
        } catch (_: Exception) {
            ""
        }
    }

    suspend fun check(ctx: Context): Result = withContext(Dispatchers.IO) {
        val cur = normalize(currentVersion(ctx)) ?: currentVersion(ctx)
        var info = fetchRelease(REPO_API)
        if (!info.hasApk) {
            try {
                val fromList = fetchReleaseList(REPO_LIST)
                if (fromList.hasApk) info = fromList
            } catch (_: Exception) { }
        }
        val hasUpdate = info.latest.isNotEmpty() && cur.isNotEmpty() && isNewer(info.latest, cur)
        info.copy(current = cur, hasUpdate = hasUpdate)
    }

    private fun fetchRelease(url: String): Result {
        val json = JSONObject(getJson(url))
        return parseRelease(json, "")
    }

    private fun fetchReleaseList(url: String): Result {
        val text = getJson(url)
        val arr = JSONArray(text)
        var first: Result? = null
        for (i in 0 until arr.length()) {
            val one = parseRelease(arr.getJSONObject(i), "")
            if (first == null) first = one
            if (one.hasApk) return one
        }
        return first ?: Result("", "", false, false, REPO_PAGE, REPO_PAGE, 0, null)
    }

    private fun getJson(url: String): String {
        val req = Request.Builder()
            .url(url)
            .header("User-Agent", "ScreenKit-Android/1.0")
            .header("Accept", "application/vnd.github+json")
            .get()
            .build()
        http.newCall(req).execute().use { r ->
            if (!r.isSuccessful) throw IllegalStateException("HTTP ${r.code}")
            return r.body?.string() ?: throw IllegalStateException("empty body")
        }
    }

    private fun parseRelease(root: JSONObject, current: String): Result {
        val tag = root.optString("tag_name", "")
        val html = root.optString("html_url", REPO_PAGE).ifEmpty { REPO_PAGE }
        var ver = normalize(tag) ?: tag.trim().removePrefix("v").removePrefix("V")
        var assetUrl: String? = null
        var assetName: String? = null
        var size = 0L
        val assets = root.optJSONArray("assets")
        if (assets != null) {
            var preferred: JSONObject? = null
            var anyApk: JSONObject? = null
            for (i in 0 until assets.length()) {
                val a = assets.getJSONObject(i)
                val an = a.optString("name", "")
                if (an.isEmpty() || !an.lowercase().endsWith(".apk")) continue
                if (anyApk == null) anyApk = a
                if (an.lowercase().contains("debug")) continue
                if (an.lowercase().startsWith("screenkit") || an.lowercase().contains("android")) {
                    preferred = a
                    break
                }
                if (preferred == null) preferred = a
            }
            val pick = preferred ?: anyApk
            if (pick != null) {
                assetName = pick.optString("name", null)
                assetUrl = pick.optString("browser_download_url", null)
                size = pick.optLong("size", 0L)
            }
        }
        val hasApk = !assetUrl.isNullOrEmpty()
        val dl = if (hasApk) assetUrl!! else html
        return Result(
            current = current,
            latest = ver,
            hasUpdate = false,
            hasApk = hasApk,
            downloadUrl = dl,
            htmlUrl = html,
            sizeBytes = size,
            assetName = assetName,
        )
    }

    private fun normalize(s: String?): String? {
        if (s.isNullOrBlank()) return null
        var t = s.trim()
        if (t.length > 1 && (t[0] == 'v' || t[0] == 'V') && t[1].isDigit())
            t = t.substring(1)
        val m = verPat.matcher(t)
        return if (m.find()) m.group() else t
    }

    private fun isNewer(remote: String, local: String): Boolean {
        val r = verParts(remote)
        val l = verParts(local)
        if (r == null) return !remote.equals(local, ignoreCase = true)
        if (l == null) return true
        val n = maxOf(r.size, l.size)
        for (i in 0 until n) {
            val a = if (i < r.size) r[i] else 0
            val b = if (i < l.size) l[i] else 0
            if (a != b) return a > b
        }
        return false
    }

    private fun verParts(s: String): IntArray? {
        val n = normalize(s) ?: return null
        val bits = n.split('.')
        return try {
            IntArray(bits.size) { bits[it].toInt() }
        } catch (_: Exception) {
            null
        }
    }
}
