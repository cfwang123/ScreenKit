package com.whj.screenkit

import android.Manifest
import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.PackageManager
import android.hardware.usb.UsbManager
import android.media.projection.MediaProjectionManager
import android.os.Build
import android.os.Bundle
import android.widget.AdapterView
import android.widget.ArrayAdapter
import android.widget.ListView
import android.widget.Toast
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import com.whj.screenkit.databinding.ActivityCastBinding

class CastActivity : AppCompatActivity() {
    private lateinit var b: ActivityCastBinding
    private var peers = listOf<Peer>()
    private var pendingMode = "tcp"
    private var pendingIp = ""
    private var pendingPort = SendPorts.HTTP
    private var pendingHttp = 1224
    private var waitUsb = false
    private var waitPat = false
    private var scanning = false

    private val proj = registerForActivityResult(ActivityResultContracts.StartActivityForResult()) { r ->
        if (r.resultCode != RESULT_OK || r.data == null) {
            toast("未授权截屏")
            return@registerForActivityResult
        }
        val i = Intent(this, CastService::class.java)
            .putExtra(CastService.EXTRA_CODE, r.resultCode)
            .putExtra(CastService.EXTRA_DATA, r.data)
            .putExtra(CastService.EXTRA_Q, b.eq.selectedItem as? String)
            .putExtra(CastService.EXTRA_AUDIO, b.caudio.isChecked)
            .putExtra(CastService.EXTRA_MODE, pendingMode)
            .putExtra(CastService.EXTRA_IP, pendingIp)
            .putExtra(CastService.EXTRA_PORT, pendingPort)
            .putExtra(CastService.EXTRA_HTTP, pendingHttp)
        ContextCompat.startForegroundService(this, i)
        b.lbstat.text = "正在启动…"
    }

    private val perm = registerForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) { }

    private val rec = object : BroadcastReceiver() {
        override fun onReceive(context: Context?, intent: Intent?) {
            val msg = intent?.getStringExtra("msg") ?: ""
            b.lbstat.text = msg
            if (msg.contains("电脑未打开") || msg == "连接失败" || msg == "USB 测试失败")
                toast(msg)
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        b = ActivityCastBinding.inflate(layoutInflater)
        setContentView(b.root)
        supportActionBar?.title = getString(R.string.label_cast)
        supportActionBar?.setDisplayHomeAsUpEnabled(true)
        b.eq.adapter = ArrayAdapter(
            this,
            android.R.layout.simple_spinner_dropdown_item,
            Quality.PRESETS.map { it.name },
        )
        b.eq.setSelection(1)
        b.eq.onItemSelectedListener = object : AdapterView.OnItemSelectedListener {
            override fun onItemSelected(parent: AdapterView<*>?, view: android.view.View?, position: Int, id: Long) {
                if (b.lbstat.text?.contains("投屏中") != true) return
                val name = Quality.PRESETS.getOrNull(position)?.name ?: return
                startService(
                    Intent(this@CastActivity, CastService::class.java)
                        .setAction(CastService.ACTION_QUALITY)
                        .putExtra(CastService.EXTRA_Q, name),
                )
            }
            override fun onNothingSelected(parent: AdapterView<*>?) {}
        }
        b.lpeers.choiceMode = ListView.CHOICE_MODE_SINGLE
        b.lpeers.setOnItemClickListener { _, _, pos, _ ->
            b.lpeers.setItemChecked(pos, true)
            val p = peers.getOrNull(pos) ?: return@setOnItemClickListener
            selectPeer(p)
        }
        b.bdevices.setOnClickListener { showDeviceSheet() }
        b.bscan.setOnClickListener { scan() }
        b.bstart.setOnClickListener { startLan() }
        b.bmanual.setOnClickListener { startManual() }
        b.busb.setOnClickListener { startUsb() }
        b.busbadb.setOnClickListener { startUsbAdb() }
        b.bstop.setOnClickListener {
            startService(Intent(this, CastService::class.java).setAction(CastService.ACTION_STOP))
        }
        askPerm()
        loadIp()
        handleUsb(intent)
        ContextCompat.registerReceiver(
            this,
            rec,
            IntentFilter(CastService.ACTION_STAT),
            ContextCompat.RECEIVER_NOT_EXPORTED,
        )
        ContextCompat.registerReceiver(
            this,
            usbPermRec,
            IntentFilter(ACTION_USB_PERM),
            ContextCompat.RECEIVER_NOT_EXPORTED,
        )
        maybeTestIntent(intent)
    }

    override fun onSupportNavigateUp(): Boolean {
        finish()
        return true
    }

    override fun onResume() {
        super.onResume()
        val skip = intent?.getBooleanExtra("scst_go", false) == true ||
            intent?.getBooleanExtra("scst_usb", false) == true ||
            intent?.getBooleanExtra("scst_usb_pat", false) == true ||
            intent?.getBooleanExtra("scst_adb", false) == true
        if (skip) return
        val st = b.lbstat.text?.toString().orEmpty()
        if (waitUsb || pendingMode == "usb" || pendingMode == "usb-lan" || pendingMode == "usb-adb") return
        if (st.contains("投屏中") || st.contains("正在启动") || st.contains("扫描中") || st.contains("等待 USB")) return
        b.root.postDelayed({
            if (isFinishing) return@postDelayed
            if (waitUsb || pendingMode == "usb") return@postDelayed
            val s2 = b.lbstat.text?.toString().orEmpty()
            if (s2.contains("投屏中") || s2.contains("正在启动") || s2.contains("等待 USB") || scanning) return@postDelayed
            scan()
        }, 300)
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        handleUsb(intent)
        maybeTestIntent(intent)
    }

    private fun maybeTestIntent(intent: Intent?) {
        if (intent?.getBooleanExtra("scst_stop", false) == true) {
            startService(Intent(this, CastService::class.java).setAction(CastService.ACTION_STOP))
            return
        }
        if (intent?.getBooleanExtra("scst_usb_pat", false) == true) {
            b.eip.post {
                waitPat = true
                startUsb()
            }
            return
        }
        if (intent?.getBooleanExtra("scst_usb", false) == true) {
            b.eip.post { startUsb() }
            return
        }
        if (intent?.getBooleanExtra("scst_adb", false) == true) {
            b.eip.post { startUsbAdb() }
            return
        }
        val ip = intent?.getStringExtra("scst_ip")?.trim().orEmpty()
        if (ip.isEmpty()) return
        pendingIp = ""
        fillIp(ip)
        resolvePendingFromUi()
        if (intent?.getBooleanExtra("scst_go", false) == true)
            b.eip.post { startManual() }
    }

    private val usbPermRec = object : BroadcastReceiver() {
        override fun onReceive(context: Context?, intent: Intent?) {
            if (intent?.getBooleanExtra(UsbManager.EXTRA_PERMISSION_GRANTED, false) == true) {
                toast("USB 配件已授权")
                if (waitUsb) startUsb()
            }
        }
    }

    override fun onDestroy() {
        try { unregisterReceiver(rec) } catch (_: Exception) { }
        try { unregisterReceiver(usbPermRec) } catch (_: Exception) { }
        super.onDestroy()
    }

    private fun askPerm() {
        val need = mutableListOf<String>()
        if (ContextCompat.checkSelfPermission(this, Manifest.permission.RECORD_AUDIO)
            != PackageManager.PERMISSION_GRANTED
        ) need.add(Manifest.permission.RECORD_AUDIO)
        if (Build.VERSION.SDK_INT >= 33 &&
            ContextCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS)
            != PackageManager.PERMISSION_GRANTED
        ) need.add(Manifest.permission.POST_NOTIFICATIONS)
        if (need.isNotEmpty()) perm.launch(need.toTypedArray())
    }

    private fun scan() {
        if (scanning) return
        scanning = true
        b.lbstat.text = "扫描中…"
        Thread {
            val list = try {
                CastDiscover.scan(this)
            } catch (ex: Exception) {
                runOnUiThread {
                    scanning = false
                    toast("扫描失败")
                }
                return@Thread
            }
            runOnUiThread {
                scanning = false
                peers = list
                b.lpeers.adapter = ArrayAdapter(
                    this,
                    R.layout.item_cast_peer,
                    list.map { it.toString() },
                )
                if (list.size == 1) {
                    b.lpeers.setItemChecked(0, true)
                    selectPeer(list[0])
                } else {
                    val last = pendingIp.ifEmpty { b.eip.text?.toString()?.trim().orEmpty() }
                    val ix = list.indexOfFirst { it.ip == last || last.startsWith("${it.ip}:") }
                    if (ix >= 0) {
                        b.lpeers.setItemChecked(ix, true)
                        selectPeer(list[ix])
                    } else if (pendingIp.isNotEmpty()) {
                        b.lpeers.clearChoices()
                        b.lbstat.text = "已选 $pendingIp"
                    } else {
                        b.lpeers.clearChoices()
                        b.lbstat.text = "扫描到 ${list.size} 台，点选一台"
                    }
                }
            }
        }.start()
    }

    private fun startLan() {
        val ix = b.lpeers.checkedItemPosition
        val p = if (ix >= 0 && ix < peers.size) peers[ix] else null
        if (p != null) {
            pendingIp = p.ip
            pendingHttp = if (p.http > 0) p.http else SendPorts.HTTP
            pendingPort = pendingHttp
            fillPeer(p)
        } else {
            resolvePendingFromUi()
            if (pendingHttp <= 0) pendingHttp = SendPorts.HTTP
            pendingPort = pendingHttp
        }
        if (pendingIp.isEmpty()) {
            toast("请先选择一台电脑")
            return
        }
        UsbLan.clear()
        pendingMode = "tcp"
        requestProj()
    }

    private fun startManual() {
        val t = b.eip.text?.toString()?.trim().orEmpty()
        if (t.isEmpty()) {
            toast("请输入 IP")
            return
        }
        var ip = t
        var port = SendPorts.HTTP
        val sp = t.split(":")
        if (sp.size == 2) {
            ip = sp[0]
            port = sp[1].toIntOrNull() ?: SendPorts.HTTP
        }
        UsbLan.clear()
        pendingMode = "tcp"
        pendingIp = ip
        pendingHttp = port
        pendingPort = port
        fillIp(if (port != SendPorts.HTTP) "$ip:$port" else ip)
        requestProj()
    }

    private fun startUsb() {
        android.util.Log.i("scst", "startUsb")
        val st = b.lbstat.text?.toString().orEmpty()
        if (st.contains("投屏中") || st.contains("USB 测试") || st.contains("正在启动")) {
            android.util.Log.i("scst", "startUsb skip $st")
            return
        }
        pendingMode = "usb"
        pendingIp = b.eip.text?.toString()?.trim().orEmpty()
        pendingPort = SendPorts.HTTP
        pendingHttp = 1224
        val usb = getSystemService(USB_SERVICE) as UsbManager
        val acc = usb.accessoryList?.firstOrNull()
        android.util.Log.i("scst", "startUsb acc=${acc?.manufacturer}/${acc?.model}/${acc?.version} n=${usb.accessoryList?.size ?: 0}")
        if (acc == null) {
            waitUsb = true
            b.lbstat.text = "等待 USB 配件…"
            toast("请用数据线连接电脑，允许 USB 配件")
            pollAccessory(0)
            return
        }
        if (!usb.hasPermission(acc)) {
            waitUsb = true
            val pi = PendingIntent.getBroadcast(
                this,
                0,
                Intent(ACTION_USB_PERM).setPackage(packageName),
                PendingIntent.FLAG_MUTABLE,
            )
            usb.requestPermission(acc, pi)
            toast("请允许 USB 配件权限")
            return
        }
        waitUsb = false
        if (waitPat) {
            startPatternSvc()
            return
        }
        requestProj()
    }

    private fun startPatternSvc() {
        waitPat = false
        val i = Intent(this, CastService::class.java)
            .putExtra(CastService.EXTRA_PATTERN, true)
            .putExtra(CastService.EXTRA_MODE, "usb")
            .putExtra(CastService.EXTRA_AUDIO, false)
            .putExtra(CastService.EXTRA_Q, b.eq.selectedItem as? String)
            .putExtra(CastService.EXTRA_IP, pendingIp.ifEmpty { b.eip.text?.toString()?.trim().orEmpty() })
            .putExtra(CastService.EXTRA_HTTP, pendingHttp)
        ContextCompat.startForegroundService(this, i)
        b.lbstat.text = "USB 测试画面…"
        toast("USB 测试画面")
    }

    private fun pollAccessory(n: Int) {
        if (!waitUsb || isFinishing) return
        val acc = (getSystemService(USB_SERVICE) as UsbManager).accessoryList?.firstOrNull()
        if (acc != null) {
            startUsb()
            return
        }
        if (n >= 225) {
            waitUsb = false
            if (waitPat) {
                waitPat = false
                b.lbstat.text = "没有 USB 配件"
                toast("没有 USB 配件")
                return
            }
            pendingMode = "usb-lan"
            UsbLan.pickNet(this)
            b.lbstat.text = "未检测到配件，改用 USB 网络共享…"
            toast("未检测到 USB 配件。请在通知栏打开 USB 网络共享后再试，或用 USB 投屏(adb)")
            requestProj()
            return
        }
        if (n % 8 == 0) b.lbstat.text = "等待 USB 配件… ${n / 2}s"
        b.root.postDelayed({ pollAccessory(n + 1) }, 400)
    }

    private fun startUsbAdb() {
        pendingMode = "usb-adb"
        pendingIp = b.eip.text?.toString()?.trim().orEmpty()
        pendingPort = SendPorts.HTTP
        pendingHttp = 1224
        b.lbstat.text = "正在探测 adb 转发…"
        Thread {
            val p = try { UsbLoop.probe() } catch (_: Exception) { null }
            runOnUiThread {
                if (p.isNullOrEmpty()) {
                    toast("连接失败，请确认电脑已开投屏接收且手机开了 USB 调试")
                    b.lbstat.text = "连接失败"
                } else {
                    b.lbstat.text = "USB adb $p"
                    requestProj()
                }
            }
        }.start()
    }

    private fun requestProj() {
        rememberCastTarget()
        val mgr = getSystemService(MEDIA_PROJECTION_SERVICE) as MediaProjectionManager
        proj.launch(mgr.createScreenCaptureIntent())
    }

    private fun showDeviceSheet() {
        PcDeviceSheet.show(this) { e -> applyRecent(e) }
    }

    private fun applyRecent(e: PcHistoryEntry) {
        selectPeer(e.toPeer())
    }

    private fun selectPeer(p: Peer) {
        pendingMode = "tcp"
        pendingIp = p.ip
        pendingHttp = when {
            p.http > 0 -> p.http
            p.tcp > 0 && p.tcp != Proto.TCP_PORT -> p.tcp
            else -> SendPorts.HTTP
        }
        pendingPort = pendingHttp
        fillPeer(p)
        val ix = peers.indexOfFirst { it.ip == p.ip }
        if (ix >= 0) b.lpeers.setItemChecked(ix, true)
        b.lbstat.text = "已选 ${p.name}  ${p.ip}:${pendingHttp}"
    }

    private fun rememberCastTarget() {
        resolvePendingFromUi()
        if (pendingIp.isEmpty()) return
        val saved = PcHistory.load(this)
        val name = peers.find { it.ip == pendingIp }?.name
            ?: saved.find { it.host == pendingIp }?.name
            ?: pendingIp
        val http = if (pendingHttp > 0) pendingHttp else SendPorts.HTTP
        val tcp = if (pendingPort > 0) pendingPort else SendPorts.HTTP
        val alias = saved.find { it.host == pendingIp && it.http == http }?.alias ?: ""
        PcHistory.remember(
            this,
            PcHistoryEntry(pendingIp, http, name, alias, "", tcp, System.currentTimeMillis()),
        )
    }

    private fun resolvePendingFromUi() {
        if (pendingIp.isNotEmpty()) return
        val t = b.eip.text?.toString()?.trim().orEmpty()
        if (t.isEmpty()) return
        val sp = t.split(":")
        pendingIp = sp[0].trim()
        if (sp.size >= 2) pendingHttp = sp[1].trim().toIntOrNull() ?: SendPorts.HTTP
    }

    private fun fillPeer(p: Peer) {
        val s = if (p.http > 0 && p.http != SendPorts.HTTP) "${p.ip}:${p.http}" else p.ip
        fillIp(s)
    }

    private fun fillIp(ip: String) {
        if (ip.isEmpty()) return
        b.eip.setText(ip)
        getSharedPreferences("skcast", MODE_PRIVATE).edit().putString("ip", ip).apply()
    }

    private fun loadIp() {
        val saved = PcHistory.load(this)
        if (saved.isNotEmpty()) {
            applyRecent(saved[0])
            return
        }
        val ip = getSharedPreferences("skcast", MODE_PRIVATE).getString("ip", "") ?: ""
        if (ip.isNotEmpty()) b.eip.setText(ip)
    }

    private fun handleUsb(intent: Intent?) {
        if (intent?.action == UsbManager.ACTION_USB_ACCESSORY_ATTACHED) {
            toast("USB 配件已连接")
            waitUsb = true
            startUsb()
        }
    }

    private fun toast(s: String) = Toast.makeText(this, s, Toast.LENGTH_SHORT).show()

    companion object {
        const val ACTION_USB_PERM = "com.whj.screenkit.USB_PERM"
    }
}
