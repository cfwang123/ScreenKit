package com.whj.screenkit

import android.content.Intent
import android.net.Uri
import android.os.Bundle
import android.provider.OpenableColumns
import android.util.Log
import android.widget.AdapterView
import android.widget.ArrayAdapter
import android.widget.Toast
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AlertDialog
import androidx.appcompat.app.AppCompatActivity
import androidx.documentfile.provider.DocumentFile
import com.whj.screenkit.databinding.ActivityMainBinding
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

class MainActivity : AppCompatActivity() {
    companion object {
        private const val TAG = "SKSend"
    }
    private lateinit var bind: ActivityMainBinding
    private lateinit var prefs: Prefs
    private var api: Api? = null
    private var rel = ""
    private val items = ArrayList<RemoteItem>()
    private val job = Job()
    private val io = CoroutineScope(Dispatchers.Main + job)
    private var pendingText: String? = null
    private val pendingFiles = ArrayList<Uri>()

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
        prefs = Prefs(this)
        if (savedInstanceState == null) grabshare(intent)
        bind.brefresh.setOnClickListener { loadlist() }
        bind.bupload.setOnClickListener { pickUpload.launch(arrayOf("*/*")) }
        bind.bsync.setOnClickListener { dosync() }
        bind.btext.setOnClickListener {
            startActivity(Intent(this, TextSyncActivity::class.java))
        }
        bind.bfolder.setOnClickListener { pickFolder.launch(null) }
        bind.bpick.setOnClickListener { openpick() }
        bind.lbpath.setOnClickListener { goup() }
        bind.lvfiles.onItemClickListener = AdapterView.OnItemClickListener { _, _, pos, _ ->
            if (pos < 0 || pos >= items.size) return@OnItemClickListener
            val it = items[pos]
            if (it.dir) {
                rel = it.path
                loadlist()
            } else {
                downloadone(it)
            }
        }
        bind.lvfiles.onItemLongClickListener = AdapterView.OnItemLongClickListener { _, _, pos, _ ->
            if (pos < 0 || pos >= items.size) return@OnItemLongClickListener true
            confirmdelete(items[pos])
            true
        }
        connectlast()
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
            val ok = withContext(Dispatchers.IO) { tryconnect() }
            if (ok) afterconnect()
            else openpick()
        }
    }

    private fun tryconnect(): Boolean {
        val a = api ?: return false
        return try {
            Log.i(TAG, "tryconnect ${a.host}:${a.port}")
            var obj = a.info()
            Log.i(TAG, "info code=${obj.optInt("code")} data=${obj.opt("data")}")
            if (obj.optInt("code") == 401 || obj.optInt("code") == 403) {
                obj = a.pair()
                Log.i(TAG, "pair code=${obj.optInt("code")} data=${obj.opt("data")}")
                if (obj.optInt("code") == 100) {
                    prefs.token = a.token
                    obj = a.info()
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
                if (err != null) toast("发送失败: $err")
                else toast("已发送文本到电脑")
            }
        }
        if (files.isNotEmpty()) uploaduris(files)
        loadlist()
    }

    private fun openpick() {
        pickPc.launch(Intent(this, PickPcActivity::class.java))
    }

    private fun loadlist() {
        val a = api ?: return
        io.launch {
            val r = withContext(Dispatchers.IO) {
                try {
                    a.list(rel, false) to null
                } catch (ex: Exception) {
                    emptyList<RemoteItem>() to (ex.message ?: "list 失败")
                }
            }
            val err = r.second
            if (err != null) {
                toast(err)
                return@launch
            }
            items.clear()
            items.addAll(r.first.sortedWith(compareBy({ !it.dir }, { it.name.lowercase() })))
            Log.i(TAG, "list rel=$rel n=${items.size}")
            bind.lbpath.text = if (rel.isEmpty()) "/" else "/$rel"
            bind.lvfiles.adapter = ArrayAdapter(
                this@MainActivity,
                android.R.layout.simple_list_item_1,
                items.map { if (it.dir) "📁 ${it.name}" else "📄 ${it.name}" },
            )
        }
    }

    private fun goup() {
        if (rel.isEmpty()) return
        val i = rel.lastIndexOf('/')
        rel = if (i <= 0) "" else rel.substring(0, i)
        loadlist()
    }

    private fun confirmdelete(it: RemoteItem) {
        AlertDialog.Builder(this)
            .setTitle("删除")
            .setMessage("删除 ${it.path} ？")
            .setPositiveButton("删除") { _, _ ->
                val a = api ?: return@setPositiveButton
                io.launch {
                    val err = withContext(Dispatchers.IO) {
                        try {
                            a.delete(it.path)
                            null
                        } catch (ex: Exception) {
                            ex.message
                        }
                    }
                    if (err != null) toast(err)
                    else loadlist()
                }
            }
            .setNegativeButton("取消", null)
            .show()
    }

    private fun uploaduris(uris: List<Uri>) {
        val a = api ?: return
        if (uris.isEmpty()) return
        val connected = bind.lbstatus.text?.toString() ?: ""
        io.launch {
            var ok = 0
            var fail = 0
            var lastErr: String? = null
            val used = HashSet<String>()
            withContext(Dispatchers.IO) {
                for ((i, uri) in uris.withIndex()) {
                    withContext(Dispatchers.Main) {
                        bind.lbstatus.text = "正在上传 ${i + 1}/${uris.size}…"
                    }
                    try {
                        val raw = queryname(uri) ?: "upload.bin"
                        val name = uniqname(raw, used)
                        val dest = if (rel.isEmpty()) name else "$rel/$name"
                        val bytes = contentResolver.openInputStream(uri)?.use { it.readBytes() }
                            ?: throw RuntimeException("无法读取 $name")
                        a.upload(dest, bytes)
                        ok++
                    } catch (ex: Exception) {
                        fail++
                        lastErr = ex.message
                        Log.w(TAG, "upload fail $uri", ex)
                    }
                }
            }
            if (connected.isNotEmpty()) bind.lbstatus.text = connected
            when {
                fail == 0 -> toast(if (ok == 1) "已上传 1 个文件" else "已上传 $ok 个文件")
                ok == 0 -> toast("上传失败: ${lastErr ?: "未知错误"}")
                else -> toast("上传成功 $ok 个，失败 $fail 个")
            }
            if (ok > 0) loadlist()
        }
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

    private fun downloadone(it: RemoteItem) {
        val folder = boundfolder()
        if (folder == null) {
            toast("请先绑定文件夹")
            pickFolder.launch(null)
            return
        }
        val a = api ?: return
        io.launch {
            val err = withContext(Dispatchers.IO) {
                try {
                    writefile(folder, it.path, a.download(it.path))
                    null
                } catch (ex: Exception) {
                    ex.message
                }
            }
            if (err != null) toast("下载失败: $err")
            else toast("已保存 ${it.name}")
        }
    }

    private fun dosync() {
        val folder = boundfolder()
        if (folder == null) {
            toast("请先绑定文件夹")
            pickFolder.launch(null)
            return
        }
        val a = api ?: return
        bind.lbstatus.text = "正在同步…"
        io.launch {
            val err = withContext(Dispatchers.IO) {
                try {
                    val all = a.list("", true).filter { !it.dir }
                    var n = 0
                    for (it in all) {
                        val existing = findchild(folder, it.path)
                        if (existing != null && existing.length() == it.size) continue
                        writefile(folder, it.path, a.download(it.path))
                        n++
                    }
                    "同步完成，更新 $n 个文件"
                } catch (ex: Exception) {
                    "同步失败: ${ex.message}"
                }
            }
            bind.lbstatus.text = "已连接 ${prefs.lastName.ifEmpty { a.host }}  ${a.host}:${a.port}"
            toast(err)
        }
    }

    private fun boundfolder(): DocumentFile? {
        val s = prefs.folderUri
        if (s.isEmpty()) return null
        return DocumentFile.fromTreeUri(this, Uri.parse(s))
    }

    private fun findchild(root: DocumentFile, relPath: String): DocumentFile? {
        var cur: DocumentFile? = root
        val parts = relPath.replace('\\', '/').split('/').filter { it.isNotEmpty() }
        for (p in parts) {
            cur = cur?.findFile(p) ?: return null
        }
        return cur
    }

    private fun writefile(root: DocumentFile, relPath: String, bytes: ByteArray) {
        val parts = relPath.replace('\\', '/').split('/').filter { it.isNotEmpty() }
        if (parts.isEmpty()) return
        var dir = root
        for (i in 0 until parts.size - 1) {
            val name = parts[i]
            val next = dir.findFile(name) ?: dir.createDirectory(name)
            dir = next ?: throw RuntimeException("无法创建目录 $name")
        }
        val fname = parts.last()
        val old = dir.findFile(fname)
        if (old != null && old.isFile) old.delete()
        val f = dir.createFile("application/octet-stream", fname)
            ?: throw RuntimeException("无法创建 $fname")
        contentResolver.openOutputStream(f.uri, "w")?.use { it.write(bytes) }
            ?: throw RuntimeException("无法写入 $fname")
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
}
