package com.whj.screenkit

import android.os.Bundle
import android.widget.ArrayAdapter
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import com.whj.screenkit.databinding.ActivityPickPcBinding
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

class PickPcActivity : AppCompatActivity() {
    private lateinit var bind: ActivityPickPcBinding
    private lateinit var prefs: Prefs
    private val pcs = ArrayList<PcInfo>()
    private val job = Job()
    private val io = CoroutineScope(Dispatchers.Main + job)

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        bind = ActivityPickPcBinding.inflate(layoutInflater)
        setContentView(bind.root)
        supportActionBar?.title = "选择电脑"
        prefs = Prefs(this)
        bind.bscan.setOnClickListener { scan() }
        bind.bconnect.setOnClickListener { connectmanual() }
        bind.lvpcs.setOnItemClickListener { _, _, pos, _ ->
            if (pos in pcs.indices) connect(pcs[pos])
        }
        scan()
    }

    override fun onDestroy() {
        super.onDestroy()
        job.cancel()
    }

    private fun scan() {
        bind.bscan.isEnabled = false
        io.launch {
            val list = withContext(Dispatchers.IO) { Discover.scan() }
            pcs.clear()
            pcs.addAll(list)
            bind.lvpcs.adapter = ArrayAdapter(
                this@PickPcActivity,
                android.R.layout.simple_list_item_1,
                pcs.map { it.toString() },
            )
            bind.bscan.isEnabled = true
            if (pcs.isEmpty())
                Toast.makeText(this@PickPcActivity, "未发现电脑，可手填 IP", Toast.LENGTH_SHORT).show()
        }
    }

    private fun connectmanual() {
        val raw = bind.eip.text?.toString()?.trim() ?: return
        if (raw.isEmpty()) return
        val host: String
        val port: Int
        val i = raw.lastIndexOf(':')
        if (i > 0) {
            host = raw.substring(0, i).trim()
            port = raw.substring(i + 1).toIntOrNull() ?: 17532
        } else {
            host = raw
            port = 17532
        }
        connect(PcInfo(host, port, host, ""))
    }

    private fun connect(pc: PcInfo) {
        io.launch {
            val err = withContext(Dispatchers.IO) {
                try {
                    val api = Api(pc.host, pc.port, prefs.deviceId, prefs.token, prefs.deviceName)
                    var obj = api.pair()
                    if (obj.optInt("code") != 100) {
                        obj = api.info()
                        if (obj.optInt("code") != 100)
                            return@withContext obj.optString("data", "连接失败")
                    } else {
                        prefs.token = api.token
                        val data = obj.optJSONObject("data")
                        prefs.lastName = data?.optString("name") ?: pc.name
                        prefs.lastPcId = data?.optString("pcId") ?: pc.pcId
                    }
                    prefs.lastHost = pc.host
                    prefs.lastPort = pc.port
                    if (prefs.lastName.isEmpty()) prefs.lastName = pc.name
                    null
                } catch (ex: Exception) {
                    ex.message ?: "连接失败"
                }
            }
            if (err != null) {
                Toast.makeText(this@PickPcActivity, err, Toast.LENGTH_LONG).show()
                return@launch
            }
            setResult(RESULT_OK)
            finish()
        }
    }
}
