package com.whj.screenkit

import android.content.Intent
import android.net.Uri
import android.os.Bundle
import android.widget.Toast
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.documentfile.provider.DocumentFile
import com.whj.screenkit.databinding.ActivitySettingsBinding

class SettingsActivity : AppCompatActivity() {
    private lateinit var bind: ActivitySettingsBinding
    private lateinit var prefs: Prefs

    private val pickFolder = registerForActivityResult(ActivityResultContracts.OpenDocumentTree()) { uri ->
        if (uri == null) return@registerForActivityResult
        contentResolver.takePersistableUriPermission(
            uri,
            Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_GRANT_WRITE_URI_PERMISSION,
        )
        prefs.folderUri = uri.toString()
        showfolder()
        Toast.makeText(this, "已绑定文件夹", Toast.LENGTH_SHORT).show()
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        bind = ActivitySettingsBinding.inflate(layoutInflater)
        setContentView(bind.root)
        supportActionBar?.title = "参数设置"
        supportActionBar?.setDisplayHomeAsUpEnabled(true)
        prefs = Prefs(this)
        bind.bfolder.setOnClickListener { pickFolder.launch(null) }
        showfolder()
    }

    override fun onSupportNavigateUp(): Boolean {
        finish()
        return true
    }

    private fun showfolder() {
        val s = prefs.folderUri
        if (s.isEmpty()) {
            bind.lbfolder.text = "未绑定"
            return
        }
        val name = try {
            DocumentFile.fromTreeUri(this, Uri.parse(s))?.name
        } catch (_: Exception) {
            null
        }
        bind.lbfolder.text = name?.ifEmpty { s } ?: s
    }
}
