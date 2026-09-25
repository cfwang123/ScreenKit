package com.whj.screenkit

import android.Manifest
import android.content.ActivityNotFoundException
import android.content.BroadcastReceiver
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Bundle
import android.provider.OpenableColumns
import android.util.Log
import android.view.Gravity
import android.view.LayoutInflater
import android.view.MenuItem
import android.view.View
import android.view.ViewGroup
import android.webkit.MimeTypeMap
import android.widget.BaseAdapter
import android.widget.ListView
import android.widget.Toast
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import androidx.core.content.FileProvider
import androidx.appcompat.widget.PopupMenu
import androidx.documentfile.provider.DocumentFile
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import com.whj.screenkit.databinding.ActivityMainBinding
import com.whj.screenkit.databinding.ItemPcBinding
import com.whj.screenkit.databinding.ItemSyncLogBinding
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File
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
    private val pcs = ArrayList<PcInfo>()
    private lateinit var pcAdapter: PcAdapter
    private var connected = false
    private var showPicker = true
    private var connectBusy = false
    private var retryJob: Job? = null
    private var retryPc: PcInfo? = null
    private var textSince = 0L
    private var textPolling = false
    private var statusSub = ""
    private var castRecOn = false

    private val castStatRec = object : BroadcastReceiver() {
        override fun onReceive(context: Context?, intent: Intent?) {
            val msg = intent?.getStringExtra("msg") ?: return
            applyCastBtn(msg)
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
        showfolder()
        toast("已绑定文件夹")
    }

    private var cameraSnapUri: Uri? = null

    private val pickUpload = registerForActivityResult(ActivityResultContracts.OpenMultipleDocuments()) { uris ->
        if (uris.isNullOrEmpty()) return@registerForActivityResult
        uploaduris(uris)
    }

    private val takePhoto = registerForActivityResult(ActivityResultContracts.TakePicture()) { ok ->
        val raw = cameraSnapUri
        cameraSnapUri = null
        if (!ok || raw == null) return@registerForActivityResult
        if (!connected) {
            toast(getString(R.string.toast_connect_pc_first))
            return@registerForActivityResult
        }
        io.launch {
            val out = withContext(Dispatchers.IO) {
                try { PhotoCompress.prepare(this@MainActivity, raw, prefs) }
                catch (ex: Exception) {
                    Log.w(TAG, "photo compress", ex)
                    null
                }
            }
            if (out == null) {
                toast("处理照片失败")
                return@launch
            }
            val name = PhotoCompress.outName(prefs)
            uploaduris(listOf(out), listOf(name))
        }
    }

    private val camPerm = registerForActivityResult(ActivityResultContracts.RequestPermission()) { ok ->
        if (ok) openCamera()
        else toast("需要相机权限才能拍照上传")
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        bind = ActivityMainBinding.inflate(layoutInflater)
        setContentView(bind.root)
        supportActionBar?.title = getString(R.string.label_launcher)
        supportActionBar?.setDisplayHomeAsUpEnabled(true)
        supportActionBar?.setHomeAsUpIndicator(R.drawable.ic_menu)
        prefs = Prefs(this)
        logAdapter = LogAdapter()
        bind.lvlog.adapter = logAdapter
        bind.lvlog.setOnItemClickListener { _, _, pos, _ ->
            if (pos in logs.indices) openrecv(logs[pos])
        }
        pcAdapter = PcAdapter()
        bind.lvpcs.adapter = pcAdapter
        bind.lvpcs.setOnItemClickListener { _, _, pos, _ ->
            if (pos in pcs.indices) connectPc(pcs[pos])
        }
        if (savedInstanceState == null) grabshare(intent)
        bind.bdevices.setOnClickListener { showDeviceSheet() }
        bind.bscan.setOnClickListener { scanPcs() }
        bind.bconnect.setOnClickListener { connectManual() }
        bind.bChangePc.setOnClickListener {
            showPicker = true
            syncUi()
            showDeviceSheet()
        }
        bind.bupload.setOnClickListener {
            if (!connected) {
                toast(getString(R.string.toast_connect_pc_first))
                return@setOnClickListener
            }
            pickUpload.launch(arrayOf("*/*"))
        }
        bind.bcam.setOnClickListener {
            if (!connected) {
                toast(getString(R.string.toast_connect_pc_first))
                return@setOnClickListener
            }
            startcamera()
        }
        bind.bweb.setOnClickListener { openWebManager() }
        bind.bcast.setOnClickListener { openCast() }
        bind.bfolder.setOnClickListener { pickFolder.launch(null) }
        bind.rowFolder.setOnClickListener { pickFolder.launch(null) }
        bind.bsendtext.setOnClickListener { sendText() }
        bind.bcopytext.setOnClickListener { copyText() }
        bind.bcleartext.setOnClickListener { bind.etext.setText("") }
        showfolder()
        syncUi()
        ContextCompat.registerReceiver(
            this,
            castStatRec,
            IntentFilter(CastService.ACTION_STAT),
            ContextCompat.RECEIVER_NOT_EXPORTED,
        )
        castRecOn = true
        applyCastBtn()
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
        pop.menu.add(0, 1, 0, getString(R.string.menu_goto_cast))
        pop.menu.add(0, 2, 1, getString(R.string.menu_web_files))
        pop.menu.add(0, 3, 2, "参数设置")
        pop.menu.add(0, 4, 3, "检查更新")
        pop.setOnMenuItemClickListener {
            when (it.itemId) {
                1 -> openCast()
                2 -> openWebManager()
                3 -> startActivity(Intent(this, SettingsActivity::class.java))
                4 -> checkupdate()
            }
            true
        }
        pop.show()
    }

    private fun checkupdate() {
        val wait = MaterialAlertDialogBuilder(this)
            .setTitle("检查更新")
            .setMessage("正在从 GitHub 检查…")
            .setCancelable(false)
            .create()
        wait.show()
        io.launch {
            try {
                val r = AppUpdate.check(this@MainActivity)
                withContext(Dispatchers.Main) {
                    wait.dismiss()
                    val msg = buildString {
                        if (r.current.isNotEmpty()) append("当前：${r.current}\n")
                        append("最新：${r.latest}\n")
                        if (r.sizeBytes > 0) append("大小：${formatBytes(r.sizeBytes)}\n")
                        append(
                            if (r.hasUpdate) "发现新版本，可下载安装。"
                            else if (r.latest.isNotEmpty()) "已是最新版本。"
                            else "未解析到版本号。",
                        )
                    }
                    val b = MaterialAlertDialogBuilder(this@MainActivity)
                        .setTitle("检查更新")
                        .setMessage(msg.trim())
                    if (r.hasApk && (r.hasUpdate || r.downloadUrl.isNotEmpty())) {
                        b.setPositiveButton("下载") { _, _ ->
                            val url = r.downloadUrl.ifEmpty { r.htmlUrl }
                            try {
                                startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url)))
                            } catch (_: ActivityNotFoundException) {
                                Toast.makeText(this@MainActivity, "无法打开链接", Toast.LENGTH_SHORT).show()
                            }
                        }
                        b.setNegativeButton("关闭", null)
                    } else {
                        b.setPositiveButton("发布页") { _, _ ->
                            try {
                                startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(r.htmlUrl)))
                            } catch (_: ActivityNotFoundException) {
                                Toast.makeText(this@MainActivity, "无法打开链接", Toast.LENGTH_SHORT).show()
                            }
                        }
                        b.setNegativeButton("关闭", null)
                    }
                    b.show()
                }
            } catch (ex: Exception) {
                withContext(Dispatchers.Main) {
                    wait.dismiss()
                    MaterialAlertDialogBuilder(this@MainActivity)
                        .setTitle("检查更新")
                        .setMessage("检查失败：${ex.message ?: ex}")
                        .setPositiveButton("关闭", null)
                        .show()
                }
            }
        }
    }

    private fun formatBytes(n: Long): String {
        if (n < 1024) return "$n B"
        val kb = n / 1024.0
        if (kb < 1024) return String.format(Locale.getDefault(), "%.1f KB", kb)
        val mb = kb / 1024.0
        if (mb < 1024) return String.format(Locale.getDefault(), "%.1f MB", mb)
        return String.format(Locale.getDefault(), "%.1f GB", mb / 1024.0)
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        grabshare(intent)
        if (connected) afterconnect()
        else connectlast()
    }

    private var usbUiOn = false
    private var usbAsked = false
    private val usbUiTick = object : Runnable {
        override fun run() {
            if (!usbUiOn) return
            refreshUsbAcc()
            bind.root.postDelayed(this, 1000)
        }
    }

    override fun onResume() {
        super.onResume()
        showfolder()
        applyCastBtn()
        usbUiOn = true
        bind.root.removeCallbacks(usbUiTick)
        bind.root.post(usbUiTick)
    }

    override fun onPause() {
        usbUiOn = false
        bind.root.removeCallbacks(usbUiTick)
        super.onPause()
    }

    override fun onDestroy() {
        if (castRecOn) {
            unregisterReceiver(castStatRec)
            castRecOn = false
        }
        super.onDestroy()
        job.cancel()
    }

    private fun applyCastBtn(msg: String = CastService.statMsg) {
        CastUi.applyCastButton(bind.bcast, CastService.isCastingMsg(msg), this)
    }

    private fun syncUi() {
        bind.panelPc.visibility = if (showPicker || !connected) View.VISIBLE else View.GONE
        bind.cardActions.visibility = if (connected) View.VISIBLE else View.GONE
        bind.cardText.visibility = if (connected) View.VISIBLE else View.GONE
        bind.bChangePc.visibility = if (connected && !showPicker) View.VISIBLE else View.GONE
        if (connected) {
            bind.dotConn.setBackgroundResource(R.drawable.dot_connected)
            bind.lbPcname.text = prefs.lastName.ifEmpty { prefs.lastHost }
            bind.lbPchost.text = statusSub.ifEmpty { "${prefs.lastHost}:${prefs.lastPort}" }
        } else {
            bind.dotConn.setBackgroundResource(R.drawable.dot_disconnected)
            bind.lbPcname.text = "未连接电脑"
            bind.lbPchost.text = statusSub.ifEmpty { "在下方选择或输入 IP" }
        }
        bind.emptyLog.visibility = if (logs.isEmpty()) View.VISIBLE else View.GONE
        bind.lvlog.visibility = if (logs.isEmpty()) View.GONE else View.VISIBLE
        refreshUsbAcc()
    }

    private fun refreshUsbAcc() {
        val usb = getSystemService(USB_SERVICE) as android.hardware.usb.UsbManager
        val on = usb.accessoryList?.isNotEmpty() == true
        bind.lbUsbacc.text = when {
            on -> "USB配件：已连接"
            usbAsked -> "USB配件：等待中"
            else -> "USB配件：未连接"
        }
    }

    private fun onUsbAsk(ask: Boolean) {
        refreshUsbAcc()
        if (!ask) {
            usbAsked = false
            return
        }
        if (usbAsked) return
        usbAsked = true
        toast("电脑已打开 USB 配件，请点投屏后再点 USB")
    }

    private fun setStatusSub(s: String) {
        statusSub = s
        syncUi()
    }

    private fun showfolder() {
        val s = prefs.folderUri
        if (s.isEmpty()) {
            bind.lbfolder.text = "未绑定 — 无法接收电脑文件"
            return
        }
        val name = try {
            DocumentFile.fromTreeUri(this, Uri.parse(s))?.name
        } catch (_: Exception) {
            null
        }
        bind.lbfolder.text = name?.ifEmpty { s } ?: s
    }

    private fun showDeviceSheet() {
        PcDeviceSheet.show(this) { e ->
            bind.eip.setText(eipText(e))
            connectPc(e.toPcInfo())
        }
    }

    private fun eipText(e: PcHistoryEntry): String {
        if (e.http == SendPorts.HTTP) return e.host
        return "${e.host}:${e.http}"
    }

    private fun scanPcs() {
        bind.bscan.isEnabled = false
        bind.lbScanHint.text = "正在搜索…"
        io.launch {
            var list = withContext(Dispatchers.IO) { Discover.scan(this@MainActivity) }
            if (list.isEmpty()) {
                delay(400)
                list = withContext(Dispatchers.IO) { Discover.scan(this@MainActivity) }
            }
            pcs.clear()
            pcs.addAll(list)
            pcAdapter.notifyDataSetChanged()
            bind.bscan.isEnabled = true
            bind.lbScanHint.text = when {
                pcs.isEmpty() -> "未发现电脑，请确认与电脑同一局域网，或手填 IP"
                else -> "点一台电脑连接（${pcs.size} 台）"
            }
        }
    }

    private fun connectManual() {
        val raw = bind.eip.text?.toString()?.trim() ?: return
        if (raw.isEmpty()) return
        val host: String
        val port: Int
        val i = raw.lastIndexOf(':')
        if (i > 0) {
            host = raw.substring(0, i).trim()
            port = raw.substring(i + 1).toIntOrNull() ?: SendPorts.HTTP
        } else {
            host = raw
            port = SendPorts.HTTP
        }
        connectPc(PcInfo(host, port, host, ""))
    }

    private fun stopRetry() {
        retryJob?.cancel()
        retryJob = null
        retryPc = null
    }

    private fun scheduleRetry(pc: PcInfo) {
        if (retryJob?.isActive == true) {
            val cur = retryPc
            if (cur != null && cur.host == pc.host && cur.port == pc.port) return
        }
        stopRetry()
        retryPc = pc
        val label = pc.name.ifEmpty { pc.host }
        setStatusSub(getString(R.string.conn_wait_pc, label))
        retryJob = io.launch {
            while (isActive) {
                delay(PcPing.RETRY_INTERVAL_MS)
                if (connected) break
                if (connectBusy) continue
                val target = retryPc ?: break
                val up = withContext(Dispatchers.IO) { PcPing.tcp(target.host, target.port) }
                if (!up) continue
                connectPc(target, fromRetry = true)
            }
        }
    }

    private fun connectPc(pc: PcInfo, fromRetry: Boolean = false) {
        if (connectBusy) return
        if (!fromRetry) stopRetry()
        connectBusy = true
        setStatusSub("正在连接 ${pc.name.ifEmpty { pc.host }}…")
        bind.bconnect.isEnabled = false
        io.launch {
            var pairCancelled = false
            try {
                val pingOk = withContext(Dispatchers.IO) { PcPing.tcp(pc.host, pc.port) }
                if (!pingOk) {
                    if (!fromRetry) toast(getString(R.string.conn_ping_fail, pc.host))
                    return@launch
                }
                val apiNew = Api(pc.host, pc.port, prefs.deviceId, prefs.token, prefs.deviceName)
                var obj = try {
                    withContext(Dispatchers.IO) { apiNew.info() }
                } catch (ex: Exception) {
                    if (!fromRetry) toast(connfail(ex, pc))
                    return@launch
                }
                when (obj.optInt("code")) {
                    401, 403 -> {
                        val paired = try {
                            pairWithWait(this@MainActivity, apiNew, pc.toString())
                        } catch (ex: Exception) {
                            if (!fromRetry) toast(connfail(ex, pc))
                            return@launch
                        }
                        if (paired == null) {
                            pairCancelled = true
                            return@launch
                        }
                        obj = paired
                        if (obj.optInt("code") != 100) {
                            if (!fromRetry) toast(obj.optString("data", "配对失败"))
                            return@launch
                        }
                        prefs.token = apiNew.token
                    }
                    100 -> { }
                    else -> {
                        if (!fromRetry) toast(obj.optString("data", "连接失败"))
                        return@launch
                    }
                }
                val data = obj.optJSONObject("data")
                prefs.lastName = data?.optString("name") ?: pc.name
                prefs.lastPcId = data?.optString("pcId") ?: pc.pcId
                prefs.lastHost = pc.host
                prefs.lastPort = pc.port
                if (prefs.lastName.isEmpty()) prefs.lastName = pc.name
                api = apiNew
                connected = true
                showPicker = false
                statusSub = ""
                applyconn()
                syncUi()
                val alias = PcHistory.load(this@MainActivity)
                    .find { it.host == pc.host && it.http == pc.port }?.alias ?: ""
                PcHistory.remember(
                    this@MainActivity,
                    PcHistoryEntry(
                        pc.host,
                        pc.port,
                        prefs.lastName,
                        alias,
                        prefs.lastPcId,
                        pc.port,
                        System.currentTimeMillis(),
                    ),
                )
                toast("已连接")
                afterconnect()
            } finally {
                connectBusy = false
                bind.bconnect.isEnabled = true
                if (connected) {
                    stopRetry()
                    setStatusSub("")
                } else if (pairCancelled) {
                    setStatusSub("")
                } else {
                    scheduleRetry(pc)
                }
            }
        }
    }

    private fun connfail(ex: Exception, pc: PcInfo): String {
        val base = ex.message ?: "连接失败"
        return if (pc.port != SendPorts.HTTP)
            "$base（电脑端口一般为 ${SendPorts.HTTP}）"
        else
            "$base（请确认电脑已开 ScreenKit、文件传输已启用、与手机同一 Wi‑Fi）"
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
            connected = false
            return
        }
        api = Api(prefs.lastHost, prefs.lastPort, prefs.deviceId, prefs.token, prefs.deviceName)
    }

    private fun connectlast() {
        if (prefs.lastPort == 17532) prefs.lastPort = SendPorts.HTTP
        if (prefs.lastHost.isEmpty() || Discover.isUsbHost(prefs.lastHost)) {
            if (Discover.isUsbHost(prefs.lastHost)) {
                val alt = PcHistory.load(this).firstOrNull { !Discover.isUsbHost(it.host) }
                if (alt != null) {
                    connectPc(alt.toPcInfo())
                    return
                }
            }
            connected = false
            showPicker = true
            syncUi()
            scanPcs()
            return
        }
        applyconn()
        setStatusSub("正在连接 ${prefs.lastName.ifEmpty { prefs.lastHost }}…")
        io.launch {
            val ok = tryconnect()
            if (ok) {
                connected = true
                showPicker = false
                statusSub = ""
                PcHistory.remember(
                    this@MainActivity,
                    PcHistoryEntry(
                        prefs.lastHost,
                        prefs.lastPort,
                        prefs.lastName,
                        "",
                        prefs.lastPcId,
                        prefs.lastPort,
                        System.currentTimeMillis(),
                    ),
                )
                syncUi()
                afterconnect()
            } else {
                connected = false
                showPicker = true
                statusSub = ""
                syncUi()
                scanPcs()
                scheduleRetry(
                    PcInfo(
                        prefs.lastHost,
                        prefs.lastPort,
                        prefs.lastName,
                        prefs.lastPcId,
                    ),
                )
            }
        }
    }

    private suspend fun tryconnect(): Boolean {
        val a = api ?: return false
        return try {
            Log.i(TAG, "tryconnect ${a.host}:${a.port}")
            if (!withContext(Dispatchers.IO) { PcPing.tcp(a.host, a.port) }) {
                Log.w(TAG, "tryconnect ping fail")
                return false
            }
            var obj = withContext(Dispatchers.IO) { a.info() }
            if (obj.optInt("code") == 401 || obj.optInt("code") == 403) {
                obj = pairWithWait(this@MainActivity, a, prefs.lastName.ifEmpty { a.host })
                    ?: return false
                if (obj.optInt("code") == 100) {
                    prefs.token = a.token
                    obj = withContext(Dispatchers.IO) { a.info() }
                }
            }
            if (obj.optInt("code") != 100) return false
            val data = obj.optJSONObject("data")
            prefs.lastName = data?.optString("name") ?: prefs.lastName
            prefs.lastPcId = data?.optString("pcId") ?: prefs.lastPcId
            prefs.applyPhoto(data)
            prefs.token = a.token
            true
        } catch (ex: Exception) {
            Log.w(TAG, "tryconnect fail", ex)
            false
        }
    }

    private fun afterconnect() {
        val a = api ?: return
        startpull()
        startTextPoll()
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

    private fun copyText() {
        val t = bind.etext.text?.toString() ?: ""
        if (t.isEmpty()) {
            toast("没有可复制的文本")
            return
        }
        val cm = getSystemService(Context.CLIPBOARD_SERVICE) as? ClipboardManager
        if (cm == null) {
            toast("无法访问剪贴板")
            return
        }
        cm.setPrimaryClip(ClipData.newPlainText("ScreenKit", t))
        toast("已复制")
    }

    private fun sendText() {
        val text = bind.etext.text?.toString() ?: ""
        if (text.isBlank()) return
        val a = api ?: return
        io.launch {
            val err = withContext(Dispatchers.IO) {
                try {
                    a.sendText(text)
                    null
                } catch (ex: Exception) {
                    ex.message
                }
            }
            if (err != null) toast(err)
            else {
                upsert("txt-${System.currentTimeMillis()}", "发往电脑  文本", timeFmt.format(Date()), 100, done = true, ok = true, fromPc = false)
            }
        }
    }

    private fun startTextPoll() {
        if (textPolling) return
        textPolling = true
        io.launch {
            try {
                while (isActive && connected) {
                    val a = api
                    if (a == null) break
                    try {
                        val neu = withContext(Dispatchers.IO) { a.pullText(textSince) }
                        if (neu.isNotEmpty()) {
                            val m = neu.maxByOrNull { it.id } ?: neu.last()
                            if (m.id > textSince) textSince = m.id
                            val box = bind.etext
                            if (box.text?.toString() != m.text) {
                                box.setText(m.text)
                                box.setSelection(box.text?.length ?: 0)
                            }
                        }
                    } catch (_: Exception) { }
                    delay(1000)
                }
            } finally {
                textPolling = false
            }
        }
    }

    private fun startcamera() {
        if (ContextCompat.checkSelfPermission(this, Manifest.permission.CAMERA)
            != PackageManager.PERMISSION_GRANTED
        ) {
            camPerm.launch(Manifest.permission.CAMERA)
            return
        }
        openCamera()
    }

    private fun openCamera() {
        try {
            val dir = File(cacheDir, "cam").apply { mkdirs() }
            val raw = File(dir, "snap_${System.currentTimeMillis()}.jpg")
            if (!raw.exists()) raw.createNewFile()
            val uri = FileProvider.getUriForFile(this, "${packageName}.fileprovider", raw)
            cameraSnapUri = uri
            takePhoto.launch(uri)
        } catch (ex: Exception) {
            Log.w(TAG, "open camera", ex)
            cameraSnapUri = null
            toast("无法打开相机")
        }
    }

    private fun uploaduris(uris: List<Uri>, fixedNames: List<String>? = null) {
        val a = api ?: return
        if (uris.isEmpty()) return
        val used = HashSet<String>()
        val jobs = ArrayList<Pair<Uri, String>>()
        for (i in uris.indices) {
            val uri = uris[i]
            val base = fixedNames?.getOrNull(i) ?: queryname(uri) ?: "upload.bin"
            val name = uniqname(base, used)
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
                        setStatusSub("正在上传 ${i + 1}/${jobs.size}…")
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
            setStatusSub("")
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
                while (isActive && connected) {
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
        val ask = a.usbAccAsk
        withContext(Dispatchers.Main) { onUsbAsk(ask) }
        if (items.isEmpty()) return
        val folder = boundfolder()
        if (folder == null) {
            if (!askedFolder) {
                askedFolder = true
                toast("请先绑定接收文件夹")
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
        adjustListHeight(bind.lvlog)
        syncUi()
    }

    private fun adjustListHeight(lv: ListView) {
        val ad = lv.adapter ?: return
        if (ad.count == 0) return
        var h = 0
        val w = View.MeasureSpec.makeMeasureSpec(lv.width.coerceAtLeast(1), View.MeasureSpec.EXACTLY)
        for (i in 0 until ad.count) {
            val v = ad.getView(i, null, lv)
            v.measure(w, View.MeasureSpec.makeMeasureSpec(0, View.MeasureSpec.UNSPECIFIED))
            h += v.measuredHeight
        }
        h += lv.dividerHeight * maxOf(0, ad.count - 1)
        val lp = lv.layoutParams
        lp.height = h
        lv.layoutParams = lp
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

    private fun openCast() {
        val host = when {
            connected && api != null -> api!!.host
            else -> prefs.lastHost
        }
        val port = when {
            connected && api != null -> api!!.port
            else -> prefs.lastPort
        }
        AppNav.openCast(this, host, port)
    }

    private fun openWebManager() {
        val host = when {
            connected && api != null -> api!!.host
            else -> prefs.lastHost
        }
        val port = when {
            connected && api != null -> api!!.port
            else -> prefs.lastPort
        }
        if (host.isEmpty()) {
            toast(getString(R.string.toast_connect_pc_first))
            return
        }
        if (!AppNav.openWebManager(this, host, port))
            toast(getString(R.string.toast_no_browser))
    }

    private fun toast(s: String) {
        Toast.makeText(this, s, Toast.LENGTH_SHORT).show()
    }

    private inner class PcAdapter : BaseAdapter() {
        override fun getCount() = pcs.size
        override fun getItem(position: Int) = pcs[position]
        override fun getItemId(position: Int) = position.toLong()
        override fun getView(position: Int, convertView: View?, parent: ViewGroup): View {
            val row = if (convertView != null) {
                ItemPcBinding.bind(convertView)
            } else {
                ItemPcBinding.inflate(LayoutInflater.from(parent.context), parent, false)
            }
            val pc = pcs[position]
            row.lbname.text = pc.name.ifEmpty { pc.host }
            row.lbhost.text = "${pc.host}:${pc.port}"
            return row.root
        }
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
