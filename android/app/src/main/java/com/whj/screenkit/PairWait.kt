package com.whj.screenkit

import android.widget.TextView
import androidx.appcompat.app.AlertDialog
import androidx.appcompat.app.AppCompatActivity
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONObject

/** 配对请求一直等到电脑点允许/拒绝；对话框可取消（中断 HTTP）。成功返回 JSON，取消返回 null。 */
suspend fun pairWithWait(activity: AppCompatActivity, api: Api, label: String): JSONObject? {
    val call = api.newPairCall()
    val view = activity.layoutInflater.inflate(R.layout.dialog_pair_wait, null)
    val msg = if (label.isBlank()) "请在电脑上点击允许配对"
    else "请在电脑上点击允许配对\n$label"
    view.findViewById<TextView>(R.id.lbmsg).text = msg
    val dlg = AlertDialog.Builder(activity)
        .setTitle("等待电脑确认")
        .setView(view)
        .setNegativeButton("取消") { _, _ -> call.cancel() }
        .setOnCancelListener { call.cancel() }
        .setCancelable(true)
        .create()
    dlg.setCanceledOnTouchOutside(false)
    dlg.show()
    try {
        return withContext(Dispatchers.IO) { api.pair(call) }
    } catch (ex: kotlinx.coroutines.CancellationException) {
        call.cancel()
        throw ex
    } catch (ex: Exception) {
        if (call.isCanceled()) return null
        throw ex
    } finally {
        try {
            dlg.dismiss()
        } catch (_: Exception) {
        }
    }
}
