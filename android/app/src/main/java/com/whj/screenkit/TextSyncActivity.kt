package com.whj.screenkit

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.BaseAdapter
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import com.whj.screenkit.databinding.ActivityTextSyncBinding
import com.whj.screenkit.databinding.ItemTextMsgBinding
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
    private val msgs = ArrayList<TextMsg>()
    private var since = 0L
    private val job = Job()
    private val io = CoroutineScope(Dispatchers.Main + job)
    private lateinit var adapter: MsgAdapter

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
        adapter = MsgAdapter()
        bind.lvmsgs.adapter = adapter
        bind.bsend.setOnClickListener { send() }
        io.launch {
            val a = api ?: return@launch
            while (isActive) {
                try {
                    val neu = withContext(Dispatchers.IO) { a.pullText(since) }
                    if (neu.isNotEmpty()) {
                        for (m in neu) {
                            msgs.add(0, m)
                            if (m.id > since) since = m.id
                        }
                        adapter.notifyDataSetChanged()
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

    private inner class MsgAdapter : BaseAdapter() {
        override fun getCount() = msgs.size
        override fun getItem(position: Int) = msgs[position]
        override fun getItemId(position: Int) = msgs[position].id
        override fun getView(position: Int, convertView: View?, parent: ViewGroup): View {
            val row = if (convertView != null) {
                ItemTextMsgBinding.bind(convertView)
            } else {
                ItemTextMsgBinding.inflate(LayoutInflater.from(parent.context), parent, false)
            }
            val m = msgs[position]
            row.lbline.text = oneline(m.text)
            row.bcopy.setOnClickListener {
                val cm = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
                cm.setPrimaryClip(ClipData.newPlainText("text", m.text))
                toast("已复制")
            }
            return row.root
        }
    }

    companion object {
        fun oneline(s: String): String {
            val t = s.replace("\r\n", "\n").replace('\r', '\n')
            val i = t.indexOf('\n')
            return if (i >= 0) t.substring(0, i) + "…" else t
        }
    }
}
