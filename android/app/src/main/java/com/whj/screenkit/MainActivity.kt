package com.whj.screenkit

import android.content.ActivityNotFoundException
import android.content.ClipData
import android.content.Intent
import android.net.Uri
import android.os.Bundle
import android.provider.OpenableColumns
import android.util.Log
import android.webkit.MimeTypeMap
import android.view.Gravity
import android.view.LayoutInflater
import android.view.MenuItem
import android.view.View
import android.view.ViewGroup
import android.widget.BaseAdapter
import android.widget.Toast
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.appcompat.widget.PopupMenu
import androidx.documentfile.provider.DocumentFile
import com.whj.screenkit.databinding.ActivityMainBinding
import com.whj.screenkit.databinding.ItemSyncLogBinding
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

data class SyncLog(
    val key: String,
    var title: String,
    var sub: String,
    var pct: Int,
    var done: Boolean,
    var ok: Boolean,
    var fromPc: Boolean = false,
    var fileUri: Uri? = null,
    var fileRel: String = "",
)

class MainActivity : AppCompatActivity() {
    companion object {
        private const val TAG = "SKSend"
        private const val MAXLOG = 200
    }
    private lateinit var bind: ActivityMainBinding
    private lateinit var prefs: Prefs
    private var api: Api? = null
    private val job = Job()
    private val io = CoroutineScope(Dispatchers.Main + job)
    private var pendingText: String? = null
    private val pendingFiles = ArrayList<Uri>()
    private var pulling = false
    private var askedFolder = false
    private val logs = ArrayList<SyncLog>()
    private lateinit var logAdapter: LogAdapter
    private val timeFmt = SimpleDateFormat("HH:mm:ss", Locale.getDefault())
    private var lastLogUi = 0L

    private val pickPc = registerForActivityResult(ActivityResultContracts.StartActivityForResult()) {
        if (it.resultCode == RESULT_OK) {
            applyconn()
            afterconnect()
        } else if (api == null) {
            bind.lbstatus.text = "未连接"
        }
    }

    private val pickFolder = registerForActivityResult(ActivityResultContracts.OpenDocumentTree()) { uri ->
        if (uri == null) return@registerForActivityResult
        contentResolver.takePersistableUriPermission(
            uri,
            Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_GRANT_WRITE_URI_PERMISSION,
        )
        prefs.folderUri = uri.toString()
        askedFolder = false
        toast("已绑定文件夹")
    }

    private val pickUpload = registerForActivityResult(ActivityResultContracts.OpenMultipleDocuments()) { uris ->
        if (uris.isNullOrEmpty()) return@registerForActivityResult
        uploaduris(uris)
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        bind = ActivityMainBinding.inflate(layoutInflater)
        setContentView(bind.root)
        supportActionBar?.title = getString(R.string.app_name)
        supportActionBar?.setDisplayHomeAsUpEnabled(true)
        supportActionBar?.setHomeAsUpIndicator(R.drawable.ic_menu)
        prefs = Prefs(this)
        logAdapter = LogAdapter()
        bind.lvlog.adapter = logAdapter
        bind.lvlog.setOnItemClickListener { _, _, pos, _ ->
            if (pos in logs.indices) openrecv(logs[pos])
        }
        if (savedInstanceState == null) grabshare(intent)
        bind.bupload.setOnClickListener { pickUpload.launch(arrayOf("*/*")) }
        bind.btext.setOnClickListener {
            startActivity(Intent(this, TextSyncActivity::class.java))
        }
        connectlast()
    }

    override fun onOptionsItemSelected(item: MenuItem): Boolean {
        if (item.itemId == android.R.id.home) {
            showmenu()
            return true
        }
        return super.onOptionsItemSelected(item)
    }

    private fun showmenu() {
        val bar = findViewById<View>(androidx.appcompat.R.id.action_bar)
        val pop = PopupMenu(this, bar ?: bind.root, Gravity.START)
        pop.menu.add(0, 1, 0, "参数设置")
        pop.menu.add(0, 2, 1, "换电脑")
        pop.setOnMenuItemClickListener {
            when (it.itemId) {
                1 -> startActivity(Intent(this, SettingsActivity::class.java))
                2 -> openpick()
            }
            true
        }
        pop.show()
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        grabshare(intent)
        if (api != null) afterconnect()
        else connectlast()
    }

    override fun onDestroy() {
        super.onDestroy()
        job.cancel()
    }

    @Suppress("DEPRECATION")
    private fun grabshare(intent: Intent?) {
        if (intent == null) return
        val action = intent.action ?: return
        if (action != Intent.ACTION_SEND && action != Intent.ACTION_SEND_MULTIPLE) return
        val text = intent.getStringExtra(Intent.EXTRA_TEXT)
        if (action == Intent.ACTION_SEND && !text.isNullOrEmpty()) {
            pendingText = text
            return
        }
        val uris = shareuris(intent)
        if (uris.isNotEmpty()) {
            pendingFiles.clear()
            pendingFiles.addAll(uris)
        } else if (!text.isNullOrEmpty()) {
            pendingText = text
        }
    }

    @Suppress("DEPRECATION")
    private fun shareuris(intent: Intent): List<Uri> {
        val out = ArrayList<Uri>()
        if (intent.action == Intent.ACTION_SEND_MULTIPLE) {
            val list = intent.getParcelableArrayListExtra<Uri>(Intent.EXTRA_STREAM)
            if (list != null) {
                for (u in list) if (u != null && u !in out) out.add(u)
            }
        } else {
            intent.getParcelableExtra<Uri>(Intent.EXTRA_STREAM)?.let { out.add(it) }
        }
        val clip = intent.clipData
        if (clip != null) {
            for (i in 0 until clip.itemCount) {
                val u = clip.getItemAt(i)?.uri ?: continue
                if (u !in out) out.add(u)
            }
        }
        return out
    }

    private fun applyconn() {
        if (prefs.lastHost.isEmpty()) {
            api = null
            return
        }
        api = Api(prefs.lastHost, prefs.lastPort, prefs.deviceId, prefs.token, prefs.deviceName)
    }

    private fun connectlast() {
        if (prefs.lastHost.isEmpty()) {
            openpick()
            return
        }
        applyconn()
        bind.lbstatus.text = "正在连接 ${prefs.lastName.ifEmpty { prefs.lastHost }}…"
        io.launch {
            val ok = tryconnect()
            if (ok) afterconnect()
            else openpick()
        }
    }

    private suspend fun tryconnect(): Boolean {
        val a = api ?: return false
        return try {
            Log.i(TAG, "tryconnect ${a.host}:${a.port}")
            var obj = withContext(Dispatchers.IO) { a.info() }
            Log.i(TAG, "info code=${obj.optInt("code")} data=${obj.opt("data")}")
            if (obj.optInt("code") == 401 || obj.optInt("code") == 403) {
                obj = pairWithWait(this@MainActivity, a, prefs.lastName.ifEmpty { a.host })
                    ?: return false
                Log.i(TAG, "pair code=${obj.optInt("code")} data=${obj.opt("data")}")
                if (obj.optInt("code") == 100) {
                    prefs.token = a.token
                    obj = withContext(Dispatchers.IO) { a.info() }
                }
            }
            if (obj.optInt("code") != 100) return false
            val data = obj.optJSONObject("data")
            prefs.lastName = data?.optString("name") ?: prefs.lastName
            prefs.lastPcId = data?.optString("pcId") ?: prefs.lastPcId
            prefs.token = a.token
            true
        } catch (ex: Exception) {
            Log.w(TAG, "tryconnect fail", ex)
            false
        }
    }

    private fun afterconnect() {
        val a = api ?: return
        bind.lbstatus.text = "已连接 ${prefs.lastName.ifEmpty { a.host }}  ${a.host}:${a.port}"
        startpull()
        val t = pendingText
        val files = ArrayList(pendingFiles)
        pendingText = null
        pendingFiles.clear()
        if (t != null) {
            io.launch {
                val err = withContext(Dispatchers.IO) {
                    try {
                        a.sendText(t)
                        null
                    } catch (ex: Exception) {
                        ex.message
                    }
                }
                if (err != null) {
                    upsert("txt", "发往电脑  文本", err, 0, done = true, ok = false, fromPc = false)
                    toast("发送失败: $err")
                } else {
                    upsert("txt", "发往电脑  文本", timeFmt.format(Date()), 100, done = true, ok = true, fromPc = false)
                }
            }
        }
        if (files.isNotEmpty()) uploaduris(files)
    }

    private fun openpick() {
        pickPc.launch(Intent(this, PickPcActivity::class.java))
    }

    private fun uploaduris(uris: List<Uri>) {
        val a = api ?: return
        if (uris.isEmpty()) return
        val connected = bind.lbstatus.text?.toString() ?: ""
        val used = HashSet<String>()
        val jobs = ArrayList<Pair<Uri, String>>()
        for (uri in uris) {
            val name = uniqname(queryname(uri) ?: "upload.bin", used)
            jobs.add(uri to name)
            upsert("up-$name", "发往电脑  $name", "等待", 0, done = false, ok = true, fromPc = false, fileRel = name)
        }
        io.launch {
            var ok = 0
            var fail = 0
            var lastErr: String? = null
            withContext(Dispatchers.IO) {
                for ((i, job) in jobs.withIndex()) {
                    val uri = job.first
                    val name = job.second
                    val key = "up-$name"
                    withContext(Dispatchers.Main) {
                        bind.lbstatus.text = "正在上传 ${i + 1}/${jobs.size}…"
                        upsert(key, "发往电脑  $name", "0%", 0, done = false, ok = true)
                    }
                    try {
                        val size = querysize(uri)
                        contentResolver.openInputStream(uri)?.use { ins ->
                            a.upload(name, ins, size) { done, total ->
                                val pct = if (total > 0) (done * 100 / total).toInt() else 0
                                runOnUiThread {
                                    upsert(key, "发往电脑  $name", fmtprog(done, total), pct, done = false, ok = true)
                                }
                            }
                        } ?: throw RuntimeException("无法读取 $name")
                        ok++
                        withContext(Dispatchers.Main) {
                            upsert(key, "发往电脑  $name", timeFmt.format(Date()), 100, done = true, ok = true)
                        }
                    } catch (ex: Exception) {
                        fail++
                        lastErr = ex.message
                        Log.w(TAG, "upload fail $uri", ex)
                        withContext(Dispatchers.Main) {
                            upsert(key, "发往电脑  $name", ex.message ?: "失败", 0, done = true, ok = false)
                        }
                    }
                }
            }
            if (connected.isNotEmpty()) bind.lbstatus.text = connected
            when {
                fail == 0 -> { }
                ok == 0 -> toast("上传失败: ${lastErr ?: "未知错误"}")
                else -> toast("上传成功 $ok 个，失败 $fail 个")
            }
        }
    }

    private fun startpull() {
        if (pulling) return
        pulling = true
        io.launch {
            try {
                while (isActive) {
                    try {
                        recvpull()
                    } catch (ex: Exception) {
                        Log.w(TAG, "pull", ex)
                    }
                    delay(1500)
                }
            } finally {
                pulling = false
            }
        }
    }

    private suspend fun recvpull() {
        val a = api ?: return
        val items = withContext(Dispatchers.IO) { a.pull() }
        if (items.isEmpty()) return
        val folder = boundfolder()
        if (folder == null) {
            if (!askedFolder) {
                askedFolder = true
                toast("请先绑定文件夹，才能接收电脑文件")
                pickFolder.launch(null)
            }
            return
        }
        withContext(Dispatchers.Main) {
            for (it in items) {
                val rel = it.rel.ifEmpty { it.name }
                if (rel.isEmpty()) continue
                upsert("dn-${it.id}", "来自电脑  $rel", "等待", 0, done = false, ok = true, fromPc = true, fileRel = rel)
            }
        }
        withContext(Dispatchers.IO) {
            for (it in items) {
                val rel = it.rel.ifEmpty { it.name }
                if (rel.isEmpty()) continue
                val key = "dn-${it.id}"
                try {
                    val saved = writefile(folder, rel, it.path, it.size) { done, total ->
                        val pct = if (total > 0) (done * 100 / total).toInt() else 0
                        runOnUiThread {
                            upsert(key, "来自电脑  $rel", fmtprog(done, total), pct, done = false, ok = true, fromPc = true, fileRel = rel)
                        }
                    }
                    a.pulldone(it.id)
                    withContext(Dispatchers.Main) {
                        upsert(key, "来自电脑  $rel", timeFmt.format(Date()), 100, done = true, ok = true, fromPc = true, fileUri = saved, fileRel = rel)
                    }
                } catch (ex: Exception) {
                    Log.w(TAG, "recv ${it.path}", ex)
                    withContext(Dispatchers.Main) {
                        upsert(key, "来自电脑  $rel", ex.message ?: "失败", 0, done = true, ok = false)
                    }
                }
            }
        }
    }

    private fun upsert(
        key: String,
        title: String,
        sub: String,
        pct: Int,
        done: Boolean,
        ok: Boolean,
        force: Boolean = true,
        fromPc: Boolean? = null,
        fileUri: Uri? = null,
        fileRel: String? = null,
    ) {
        val now = System.currentTimeMillis()
        if (!force && !done && now - lastLogUi < 150) return
        lastLogUi = now
        val i = logs.indexOfFirst { it.key == key }
        if (i >= 0) {
            val row = logs[i]
            row.title = title
            row.sub = sub
            row.pct = pct
            row.done = done
            row.ok = ok
            if (fromPc != null) row.fromPc = fromPc
            if (fileUri != null) row.fileUri = fileUri
            if (!fileRel.isNullOrEmpty()) row.fileRel = fileRel
        } else {
            logs.add(0, SyncLog(key, title, sub, pct, done, ok, fromPc == true, fileUri, fileRel ?: ""))
            while (logs.size > MAXLOG) logs.removeAt(logs.lastIndex)
        }
        logAdapter.notifyDataSetChanged()
    }

    private fun fmtprog(done: Long, total: Long): String {
        return if (total > 0) "${sfsize(done)} / ${sfsize(total)}" else sfsize(done)
    }

    private fun sfsize(n: Long): String {
        if (n < 1024) return "$n B"
        if (n < 1024 * 1024) return "${n / 1024} KB"
        if (n < 1024L * 1024 * 1024) return String.format(Locale.getDefault(), "%.1f MB", n / (1024.0 * 1024))
        return String.format(Locale.getDefault(), "%.1f GB", n / (1024.0 * 1024 * 1024))
    }

    private fun uniqname(name: String, used: HashSet<String>): String {
        if (used.add(name)) return name
        val dot = name.lastIndexOf('.')
        val base = if (dot > 0) name.substring(0, dot) else name
        val ext = if (dot > 0) name.substring(dot) else ""
        var n = 2
        while (!used.add("$base ($n)$ext")) n++
        return "$base ($n)$ext"
    }

    private fun boundfolder(): DocumentFile? {
        val s = prefs.folderUri
        if (s.isEmpty()) return null
        return DocumentFile.fromTreeUri(this, Uri.parse(s))
    }

    private fun writefile(
        root: DocumentFile,
        relPath: String,
        storePath: String,
        size: Long,
        onProg: (Long, Long) -> Unit,
    ): Uri {
        val a = api ?: throw RuntimeException("未连接")
        val parts = relPath.replace('\\', '/').split('/').filter { it.isNotEmpty() }
        if (parts.isEmpty()) throw RuntimeException("路径为空")
        var dir = root
        for (i in 0 until parts.size - 1) {
            val name = parts[i]
            val next = dir.findFile(name) ?: dir.createDirectory(name)
            dir = next ?: throw RuntimeException("无法创建目录 $name")
        }
        val fname = parts.last()
        val old = dir.findFile(fname)
        if (old != null && old.isFile) old.delete()
        val f = dir.createFile(mimeof(fname), fname)
            ?: throw RuntimeException("无法创建 $fname")
        val pair = a.downloadStream(storePath)
        val ins = pair.first
        val total = if (pair.second > 0) pair.second else size
        ins.use { input ->
            contentResolver.openOutputStream(f.uri, "w")?.use { os ->
                val buf = ByteArray(64 * 1024)
                var done = 0L
                while (true) {
                    val n = input.read(buf)
                    if (n <= 0) break
                    os.write(buf, 0, n)
                    done += n
                    onProg(done, total)
                }
            } ?: throw RuntimeException("无法写入 $fname")
        }
        return f.uri
    }

    private fun findfile(root: DocumentFile?, relPath: String): DocumentFile? {
        if (root == null) return null
        var cur = root
        val parts = relPath.replace('\\', '/').split('/').filter { it.isNotEmpty() }
        for (p in parts) {
            cur = cur?.findFile(p) ?: return null
        }
        return cur
    }

    private fun openrecv(m: SyncLog) {
        if (!m.fromPc) return
        if (!m.done) {
            toast("文件还在接收")
            return
        }
        if (!m.ok) {
            toast("接收失败，无法打开")
            return
        }
        var uri = m.fileUri
        if (uri == null && m.fileRel.isNotEmpty())
            uri = findfile(boundfolder(), m.fileRel)?.uri
        if (uri == null) {
            toast("找不到文件，请确认已绑定文件夹")
            return
        }
        val name = m.fileRel.substringAfterLast('/').ifEmpty { m.fileRel }
        val mime = mimeof(name)
        val intent = Intent(Intent.ACTION_VIEW)
        intent.setDataAndType(uri, mime)
        intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_GRANT_PREFIX_URI_PERMISSION)
        intent.clipData = ClipData.newUri(contentResolver, name, uri)
        try {
            startActivity(Intent.createChooser(intent, "选择打开方式"))
        } catch (_: ActivityNotFoundException) {
            toast("没有可打开此文件的应用")
        } catch (ex: Exception) {
            toast(ex.message ?: "无法打开")
        }
    }

    private fun mimeof(name: String): String {
        val ext = name.substringAfterLast('.', "").lowercase(Locale.getDefault())
        if (ext.isEmpty()) return "application/octet-stream"
        return MimeTypeMap.getSingleton().getMimeTypeFromExtension(ext) ?: "application/octet-stream"
    }

    private fun iconof(name: String): Int {
        val mime = mimeof(name)
        val ext = name.substringAfterLast('.', "").lowercase(Locale.getDefault())
        return when {
            mime.startsWith("image/") -> R.drawable.ic_file_image
            mime.startsWith("video/") -> R.drawable.ic_file_video
            mime.startsWith("audio/") -> R.drawable.ic_file_audio
            mime == "application/pdf" || ext == "pdf" -> R.drawable.ic_file_pdf
            mime.startsWith("text/") || ext in listOf("txt", "md", "csv", "log", "json", "xml") -> R.drawable.ic_file_text
            ext in listOf("zip", "rar", "7z", "gz", "tar") -> R.drawable.ic_file_zip
            else -> R.drawable.ic_file
        }
    }

    private fun querysize(uri: Uri): Long {
        try {
            val c = contentResolver.query(uri, null, null, null, null)
            c?.use {
                if (it.moveToFirst()) {
                    val i = it.getColumnIndex(OpenableColumns.SIZE)
                    if (i >= 0 && !it.isNull(i)) return it.getLong(i)
                }
            }
        } catch (_: Exception) { }
        return -1
    }

    private fun queryname(uri: Uri): String? {
        try {
            val c = contentResolver.query(uri, null, null, null, null)
            c?.use {
                if (it.moveToFirst()) {
                    val i = it.getColumnIndex(OpenableColumns.DISPLAY_NAME)
                    if (i >= 0) {
                        val n = it.getString(i)
                        if (!n.isNullOrEmpty()) return n
                    }
                }
            }
        } catch (_: Exception) { }
        val last = uri.lastPathSegment ?: return null
        val n = last.substringAfterLast('/')
        return n.ifEmpty { null }
    }

    private fun toast(s: String) {
        Toast.makeText(this, s, Toast.LENGTH_SHORT).show()
    }

    private inner class LogAdapter : BaseAdapter() {
        override fun getCount() = logs.size
        override fun getItem(position: Int) = logs[position]
        override fun getItemId(position: Int) = position.toLong()
        override fun getView(position: Int, convertView: View?, parent: ViewGroup): View {
            val row = if (convertView != null) {
                ItemSyncLogBinding.bind(convertView)
            } else {
                ItemSyncLogBinding.inflate(LayoutInflater.from(parent.context), parent, false)
            }
            val m = logs[position]
            val name = m.fileRel.substringAfterLast('/').ifEmpty { m.fileRel }
            row.icfile.setImageResource(if (name.isNotEmpty()) iconof(name) else R.drawable.ic_file)
            row.icfile.visibility = if (m.fromPc || name.isNotEmpty()) View.VISIBLE else View.GONE
            row.lbtitle.text = m.title
            row.lbsub.text = m.sub
            if (m.done) {
                row.lbpct.text = if (m.ok) "完成" else "失败"
                row.bar.visibility = View.GONE
            } else {
                row.lbpct.text = "${m.pct.coerceIn(0, 100)}%"
                row.bar.visibility = View.VISIBLE
                row.bar.progress = m.pct.coerceIn(0, 100)
            }
            return row.root
        }
    }
}
