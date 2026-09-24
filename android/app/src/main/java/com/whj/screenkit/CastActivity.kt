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
    private var pendingPort = Proto.TCP_PORT
    private var waitUsb = false

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
        ContextCompat.startForegroundService(this, i)
        b.lbstat.text = "正在启动…"
    }

    private val perm = registerForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) { }

    private val rec = object : BroadcastReceiver() {
        override fun onReceive(context: Context?, intent: Intent?) {
            b.lbstat.text = intent?.getStringExtra("msg") ?: ""
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        b = ActivityCastBinding.inflate(layoutInflater)
        setContentView(b.root)
        supportActionBar?.title = getString(R.string.label_cast)
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
            b.lbstat.text = "已选 ${p.name}  ${p.ip}"
        }
        b.bscan.setOnClickListener { scan() }
        b.bstart.setOnClickListener { startLan() }
        b.bmanual.setOnClickListener { startManual() }
        b.busb.setOnClickListener { startUsb() }
        b.bstop.setOnClickListener {
            startService(Intent(this, CastService::class.java).setAction(CastService.ACTION_STOP))
        }
        askPerm()
        refreshUsb()
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

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        handleUsb(intent)
        maybeTestIntent(intent)
    }

    private fun maybeTestIntent(intent: Intent?) {
        val ip = intent?.getStringExtra("scst_ip")?.trim().orEmpty()
        if (ip.isEmpty()) return
        b.eip.setText(ip)
        if (intent?.getBooleanExtra("scst_go", false) == true)
            b.eip.post { startManual() }
    }

    private val usbPermRec = object : BroadcastReceiver() {
        override fun onReceive(context: Context?, intent: Intent?) {
            refreshUsb()
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
        b.lbstat.text = "扫描中…"
        Thread {
            val list = try {
                CastDiscover.scan(this)
            } catch (ex: Exception) {
                runOnUiThread { toast("扫描失败: ${ex.message}") }
                emptyList()
            }
            runOnUiThread {
                peers = list
                b.lpeers.adapter = ArrayAdapter(
                    this,
                    R.layout.item_cast_peer,
                    list.map { it.toString() },
                )
                if (list.size == 1) {
                    b.lpeers.setItemChecked(0, true)
                    b.lbstat.text = "已选 ${list[0].name}  ${list[0].ip}"
                } else {
                    b.lpeers.clearChoices()
                    b.lbstat.text = "扫描到 ${list.size} 台，点选一台"
                }
            }
        }.start()
    }

    private fun startLan() {
        val ix = b.lpeers.checkedItemPosition
        val p = if (ix >= 0 && ix < peers.size) peers[ix] else null
        if (p == null) {
            toast("请先扫描并点选一台电脑")
            return
        }
        pendingMode = "tcp"
        pendingIp = p.ip
        pendingPort = p.tcp
        requestProj()
    }

    private fun startManual() {
        val t = b.eip.text?.toString()?.trim().orEmpty()
        if (t.isEmpty()) {
            toast("请输入 IP")
            return
        }
        var ip = t
        var port = Proto.TCP_PORT
        val sp = t.split(":")
        if (sp.size == 2) {
            ip = sp[0]
            port = sp[1].toIntOrNull() ?: Proto.TCP_PORT
        }
        pendingMode = "tcp"
        pendingIp = ip
        pendingPort = port
        requestProj()
    }

    private fun startUsb() {
        android.util.Log.i("scst", "startUsb")
        pendingMode = "usb"
        pendingIp = ""
        pendingPort = Proto.TCP_PORT
        val usb = getSystemService(USB_SERVICE) as UsbManager
        val acc = usb.accessoryList?.firstOrNull()
        if (acc == null) {
            waitUsb = true
            b.lbstat.text = "正在探测 USB 网络…"
            Thread {
                val ip = try { UsbLan.findPc(this) } catch (_: Exception) { null }
                runOnUiThread {
                    if (!ip.isNullOrEmpty()) {
                        pendingMode = "tcp"
                        pendingIp = ip
                        pendingPort = Proto.TCP_PORT
                        waitUsb = false
                        b.lbstat.text = "USB 网络 $ip"
                        requestProj()
                    } else {
                        toast("未检测到 USB 配件或 USB 网络。通知栏把 USB 设为「网络共享」即可，无需 USB 调试")
                        b.lbstat.text = "等待 USB：配件或网络共享"
                    }
                }
            }.start()
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
        requestProj()
    }

    private fun requestProj() {
        val mgr = getSystemService(MEDIA_PROJECTION_SERVICE) as MediaProjectionManager
        proj.launch(mgr.createScreenCaptureIntent())
    }

    private fun refreshUsb() {
        val usb = getSystemService(USB_SERVICE) as UsbManager
        val n = usb.accessoryList?.size ?: 0
        b.lbusb.text = if (n > 0)
            "USB: 配件已连接（无需 USB 调试）"
        else
            "USB: 配件，或通知栏设为「网络共享」（无需 USB 调试）"
    }

    private fun handleUsb(intent: Intent?) {
        if (intent?.action == UsbManager.ACTION_USB_ACCESSORY_ATTACHED) {
            refreshUsb()
            toast("USB 配件已连接")
            if (waitUsb) startUsb()
        }
    }

    private fun toast(s: String) = Toast.makeText(this, s, Toast.LENGTH_SHORT).show()

    companion object {
        const val ACTION_USB_PERM = "com.whj.screenkit.USB_PERM"
    }
}
