package com.whj.screenkit

import android.Manifest
import android.app.PendingIntent
import android.app.admin.DevicePolicyManager
import android.content.BroadcastReceiver
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.PackageManager
import android.hardware.usb.UsbManager
import android.media.projection.MediaProjectionManager
import android.os.Build
import android.os.Bundle
import android.view.View
import android.widget.AdapterView
import android.widget.ArrayAdapter
import android.widget.ListView
import android.widget.Toast
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AlertDialog
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import androidx.lifecycle.Lifecycle
import com.google.android.material.dialog.MaterialAlertDialogBuilder
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
    private var errDlg: AlertDialog? = null
    private var pendingErr: String? = null

    private val proj = registerForActivityResult(ActivityResultContracts.StartActivityForResult()) { r ->
        if (r.resultCode != RESULT_OK || r.data == null) {
            showCastErr("未授权截屏，无法开始投屏")
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
        applyModeUi()
    }

    private val perm = registerForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) { }

    private val rec = object : BroadcastReceiver() {
        override fun onReceive(context: Context?, intent: Intent?) {
            val msg = intent?.getStringExtra("msg") ?: ""
            val err = intent?.getBooleanExtra("err", false) == true
            b.lbstat.text = msg
            applyModeUi()
            if (err) showCastErr(msg)
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
        b.bstart.setOnClickListener { startFromTarget() }
        b.bmanual.setOnClickListener { startManual() }
        b.busb.setOnClickListener { startUsb() }
        b.busbadb.setOnClickListener { startUsbAdb() }
        b.bstop.setOnClickListener {
            cancelUsbWait()
            startService(Intent(this, CastService::class.java).setAction(CastService.ACTION_STOP))
        }
        b.boff.setOnClickListener { askScreenOff() }
        askPerm()
        loadIp()
        if (handleUsb(intent, leave = true)) return
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
        applyModeUi()
    }

    override fun onSupportNavigateUp(): Boolean {
        finish()
        return true
    }

    private var usbUiOn = false
    private val usbUiTick = object : Runnable {
        override fun run() {
            if (!usbUiOn) return
            refreshUsbLine()
            b.root.postDelayed(this, 1000)
        }
    }

    override fun onResume() {
        super.onResume()
        usbUiOn = true
        b.root.removeCallbacks(usbUiTick)
        b.root.post(usbUiTick)
        val skip = intent?.getBooleanExtra("scst_go", false) == true ||
            intent?.getBooleanExtra("scst_usb", false) == true ||
            intent?.getBooleanExtra("scst_usb_pat", false) == true ||
            intent?.getBooleanExtra("scst_adb", false) == true ||
            intent?.getBooleanExtra("scst_adb_probe", false) == true ||
            intent?.getBooleanExtra("scst_wifi_probe", false) == true
        if (!skip) applyModeUi()
        pendingErr?.let { showCastErr(it) }
    }

    override fun onPause() {
        usbUiOn = false
        b.root.removeCallbacks(usbUiTick)
        super.onPause()
    }

    private fun refreshUsbLine() {
        val usb = getSystemService(USB_SERVICE) as UsbManager
        val on = usb.accessoryList?.isNotEmpty() == true
        b.lbUsbacc.text = when {
            on -> "USB配件：已连接"
            waitUsb -> "USB配件：等待中"
            else -> "USB配件：未连接"
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        handleUsb(intent, leave = false)
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
        if (intent?.getBooleanExtra("scst_screen_off", false) == true) {
            b.eip.post { startScreenOff(force = true) }
            return
        }
        if (intent?.getBooleanExtra("scst_adb", false) == true) {
            b.eip.post { startUsbAdb() }
            return
        }
        if (intent?.getBooleanExtra("scst_adb_probe", false) == true) {
            b.eip.post { runAdbProbeOnly() }
            return
        }
        if (intent?.getBooleanExtra("scst_wifi_probe", false) == true) {
            val ip0 = intent?.getStringExtra("scst_ip")?.trim().orEmpty()
            if (ip0.isNotEmpty()) fillIp(ip0)
            b.eip.post { runWifiProbeOnly() }
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
        errDlg?.dismiss()
        errDlg = null
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
                b.lpeers.visibility = if (list.isEmpty()) View.GONE else View.VISIBLE
                b.lpeers.adapter = ArrayAdapter(
                    this,
                    R.layout.item_cast_peer,
                    list.map { it.toString() },
                )
                if (list.isEmpty()) {
                    if (!castingNow()) b.lbstat.text = "没有搜到电脑，请填写 IP"
                    return@runOnUiThread
                }
                if (list.size == 1) {
                    b.lpeers.setItemChecked(0, true)
                    selectPeer(list[0])
                } else {
                    val last = pendingIp.ifEmpty { b.eip.text?.toString()?.trim().orEmpty() }
                    val ix = list.indexOfFirst { it.ip == last || last.startsWith("${it.ip}:") }
                    if (ix >= 0) {
                        b.lpeers.setItemChecked(ix, true)
                        selectPeer(list[ix])
                    } else if (!castingNow()) {
                        b.lpeers.clearChoices()
                        b.lbstat.text = "搜到 ${list.size} 台，点一台"
                    }
                }
            }
        }.start()
    }

    private fun startFromTarget() {
        val ix = b.lpeers.checkedItemPosition
        if (b.lpeers.visibility == View.VISIBLE && ix >= 0 && ix < peers.size) {
            val typed = b.eip.text?.toString()?.trim().orEmpty()
            val p = peers[ix]
            val shown = if (p.http > 0 && p.http != SendPorts.HTTP) "${p.ip}:${p.http}" else p.ip
            if (typed.isEmpty() || typed == shown || typed == p.ip) {
                selectPeer(p)
                startLan()
                return
            }
        }
        startManual()
    }

    private fun castingNow(): Boolean {
        val st = b.lbstat.text?.toString().orEmpty()
        if (st.contains("失败") || st.contains("已停止") || st.contains("已断开") || st.contains("未授权"))
            return false
        return st.contains("投屏中") || st.contains("正在启动") || st.contains("USB 测试")
    }

    private fun showCastErr(msg: String) {
        val text = msg.trim().ifEmpty { "投屏失败" }
        if (isFinishing || isDestroyed) return
        b.lbstat.text = text
        applyModeUi()
        if (!lifecycle.currentState.isAtLeast(Lifecycle.State.STARTED)) {
            pendingErr = text
            return
        }
        pendingErr = null
        errDlg?.dismiss()
        val dlg = MaterialAlertDialogBuilder(this)
            .setTitle("投屏失败")
            .setMessage(text)
            .setPositiveButton(android.R.string.ok, null)
            .create()
        dlg.setOnDismissListener { if (errDlg === dlg) errDlg = null }
        errDlg = dlg
        dlg.show()
    }

    private fun applyModeUi() {
        val on = castingNow()
        b.bstart.isEnabled = !on && !waitUsb
        b.bstop.visibility = if (on || waitUsb) View.VISIBLE else View.GONE
        b.boff.visibility = if (on) View.VISIBLE else View.GONE
        if (on) b.bstop.text = "停止投屏"
        else if (waitUsb) b.bstop.text = "取消等待"
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
            applyModeUi()
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
            applyModeUi()
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

    private fun cancelUsbWait() {
        waitUsb = false
        waitPat = false
        if (pendingMode == "usb" || pendingMode == "usb-lan" || pendingMode == "usb-adb")
            pendingMode = "tcp"
        val st = b.lbstat.text?.toString().orEmpty()
        if (st.contains("等待 USB") || st.contains("USB 配件"))
            b.lbstat.text = "已停止"
        refreshUsbLine()
        applyModeUi()
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
                showCastErr("没有 USB 配件")
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
                    showCastErr("连接失败，请确认电脑已开投屏接收且手机开了 USB 调试")
                } else {
                    b.lbstat.text = "USB adb $p"
                    requestProj()
                }
            }
        }.start()
    }

    private fun runAdbProbeOnly() {
        b.lbstat.text = "adb 探测…"
        Thread {
            val p = try { UsbLoop.probe() } catch (_: Exception) { null }
            val msg = if (p.isNullOrEmpty()) {
                "adb probe fail ${UsbLoop.lastErr}"
            } else {
                "adb probe ok $p:${SendPorts.HTTP}"
            }
            android.util.Log.i("scst", msg)
            runOnUiThread {
                b.lbstat.text = msg
                toast(msg)
            }
        }.start()
    }

    private fun runWifiProbeOnly() {
        resolvePendingFromUi()
        var ip = pendingIp
        var port = if (pendingHttp > 0) pendingHttp else SendPorts.HTTP
        val t = b.eip.text?.toString()?.trim().orEmpty()
        if (t.isNotEmpty()) {
            val sp = t.split(":")
            ip = sp[0].trim()
            if (sp.size >= 2) port = sp[1].trim().toIntOrNull() ?: SendPorts.HTTP
        }
        if (ip.isEmpty()) {
            toast("请输入电脑 IP")
            return
        }
        b.lbstat.text = "WiFi 探测 $ip:$port…"
        Thread {
            val msg = CastWifiProbe.run(ip, port)
            android.util.Log.i("scst", msg)
            runOnUiThread {
                b.lbstat.text = msg
                toast(msg)
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
        if (!castingNow()) b.lbstat.text = "准备投屏"
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
        val name = p.name.trim()
        if (name.isNotEmpty() && !name.equals(p.ip, ignoreCase = true))
            b.lbTarget.text = "投到 $name · $s"
    }

    private fun fillIp(ip: String) {
        if (ip.isEmpty()) return
        b.eip.setText(ip)
        b.lbTarget.text = "投到 $ip"
        getSharedPreferences("skcast", MODE_PRIVATE).edit().putString("ip", ip).apply()
    }

    private fun loadIp() {
        val saved = PcHistory.load(this)
        if (saved.isNotEmpty()) {
            applyRecent(saved[0])
            return
        }
        val ip = getSharedPreferences("skcast", MODE_PRIVATE).getString("ip", "") ?: ""
        if (ip.isNotEmpty()) fillIp(ip)
        else b.lbTarget.text = "还没有选择电脑"
    }

    private fun handleUsb(intent: Intent?, leave: Boolean): Boolean {
        if (intent?.action != UsbManager.ACTION_USB_ACCESSORY_ATTACHED) return false
        refreshUsbLine()
        if (waitUsb) {
            toast("USB 配件已连接")
            startUsb()
            return false
        }
        toast("USB 配件已连接，点 USB 开始投屏")
        if (!leave) {
            b.lbstat.text = "USB配件已连接，点 USB 开始投屏"
            return false
        }
        finish()
        return true
    }

    private val adminAsk = registerForActivityResult(ActivityResultContracts.StartActivityForResult()) {
        if (it.resultCode == RESULT_OK) startScreenOff(force = false)
        else toast("未允许设备管理，无法熄屏")
    }

    private fun askScreenOff() {
        if (!ScreenOff.adminOn(this)) {
            val i = Intent(DevicePolicyManager.ACTION_ADD_DEVICE_ADMIN)
                .putExtra(DevicePolicyManager.EXTRA_DEVICE_ADMIN, ComponentName(this, AdminRecv::class.java))
                .putExtra(DevicePolicyManager.EXTRA_ADD_EXPLANATION, "熄屏后继续投屏。按电源键亮屏并退出熄屏投屏。")
            adminAsk.launch(i)
            return
        }
        startScreenOff(force = false)
    }

    private fun startScreenOff(force: Boolean) {
        val st = b.lbstat.text?.toString().orEmpty()
        if (!force && !CastService.isCastingMsg(st) && !CastService.isCastingMsg(CastService.statMsg)) {
            toast("请先开始投屏")
            return
        }
        startService(Intent(this, CastService::class.java).setAction(CastService.ACTION_SCREEN_OFF))
    }

    private fun toast(s: String) = Toast.makeText(this, s, Toast.LENGTH_SHORT).show()

    companion object {
        const val ACTION_USB_PERM = "com.whj.screenkit.USB_PERM"
    }
}
