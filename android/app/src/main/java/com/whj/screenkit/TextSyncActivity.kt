package com.whj.screenkit

import android.os.Bundle
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import com.whj.screenkit.databinding.ActivityTextSyncBinding
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

class TextSyncActivity : AppCompatActivity() {
    private lateinit var bind: ActivityTextSyncBinding
    private lateinit var prefs: Prefs
    private var api: Api? = null
    private var since = 0L
    private val job = Job()
    private val io = CoroutineScope(Dispatchers.Main + job)

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        bind = ActivityTextSyncBinding.inflate(layoutInflater)
        setContentView(bind.root)
        supportActionBar?.title = "文本同步"
        prefs = Prefs(this)
        if (prefs.lastHost.isEmpty()) {
            toast("尚未连接电脑")
            finish()
            return
        }
        api = Api(prefs.lastHost, prefs.lastPort, prefs.deviceId, prefs.token, prefs.deviceName)
        bind.emsg.keyListener = null
        bind.emsg.setTextIsSelectable(true)
        bind.emsg.showSoftInputOnFocus = false
        bind.bsend.setOnClickListener { send() }
        io.launch {
            val a = api ?: return@launch
            while (isActive) {
                try {
                    val neu = withContext(Dispatchers.IO) { a.pullText(since) }
                    if (neu.isNotEmpty()) {
                        val m = neu.maxByOrNull { it.id } ?: neu.last()
                        if (m.id > since) since = m.id
                        if (bind.emsg.text?.toString() != m.text)
                            bind.emsg.setText(m.text)
                    }
                } catch (_: Exception) {
                }
                delay(1000)
            }
        }
    }

    override fun onDestroy() {
        super.onDestroy()
        job.cancel()
    }

    private fun send() {
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
            else bind.etext.setText("")
        }
    }

    private fun toast(s: String) {
        Toast.makeText(this, s, Toast.LENGTH_SHORT).show()
    }
}
