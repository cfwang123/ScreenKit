package com.whj.screenkit

import android.graphics.Color
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.appcompat.app.AlertDialog
import androidx.fragment.app.FragmentActivity
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import com.google.android.material.textfield.TextInputEditText
import com.whj.screenkit.databinding.DialogPcDevicesBinding
import com.whj.screenkit.databinding.ItemPcDeviceRowBinding

/** 居中弹窗：选择 / 管理已保存的电脑（别名、删除）。 */
object PcDeviceSheet {
    fun show(activity: FragmentActivity, onPick: (PcHistoryEntry) -> Unit) {
        val bind = DialogPcDevicesBinding.inflate(activity.layoutInflater)
        val dlg = MaterialAlertDialogBuilder(activity).setView(bind.root).create()
        var items = PcHistory.load(activity)
        lateinit var listAdapter: DeviceAdapter
        listAdapter = DeviceAdapter(items, object : DeviceAdapter.Listener {
            override fun onPick(e: PcHistoryEntry) {
                dlg.dismiss()
                onPick(e)
            }

            override fun onAlias(e: PcHistoryEntry) {
                showAlias(activity, e) {
                    items = PcHistory.load(activity)
                    listAdapter.setItems(items)
                    paintEmpty(bind, items)
                }
            }

            override fun onDelete(e: PcHistoryEntry) {
                MaterialAlertDialogBuilder(activity)
                    .setTitle(R.string.pc_devices_delete)
                    .setMessage(activity.getString(R.string.pc_devices_delete_msg, e.displayTitle()))
                    .setPositiveButton(R.string.pc_devices_delete) { _, _ ->
                        PcHistory.remove(activity, e.host, e.http)
                        items = PcHistory.load(activity)
                        listAdapter.setItems(items)
                        paintEmpty(bind, items)
                    }
                    .setNegativeButton(android.R.string.cancel, null)
                    .show()
            }
        })
        bind.list.layoutManager = LinearLayoutManager(activity)
        bind.list.adapter = listAdapter
        bind.bclose.setOnClickListener { dlg.dismiss() }
        paintEmpty(bind, items)
        dlg.show()
        layoutDialog(activity, dlg)
    }

    private fun layoutDialog(activity: FragmentActivity, dlg: AlertDialog) {
        val dm = activity.resources.displayMetrics
        val maxW = (480f * dm.density).toInt()
        val w = ((dm.widthPixels * 0.9f).toInt()).coerceAtMost(maxW)
        dlg.window?.setLayout(w, ViewGroup.LayoutParams.WRAP_CONTENT)
        dlg.window?.setBackgroundDrawableResource(android.R.color.transparent)
        dlg.window?.decorView?.setBackgroundColor(Color.TRANSPARENT)
    }

    private fun paintEmpty(bind: DialogPcDevicesBinding, items: List<PcHistoryEntry>) {
        val empty = items.isEmpty()
        bind.empty.visibility = if (empty) View.VISIBLE else View.GONE
        bind.list.visibility = if (empty) View.GONE else View.VISIBLE
    }

    private fun showAlias(activity: FragmentActivity, e: PcHistoryEntry, done: () -> Unit) {
        val input = TextInputEditText(activity)
        input.setText(e.alias.ifEmpty { e.name })
        input.setSelection(input.text?.length ?: 0)
        val pad = (16 * activity.resources.displayMetrics.density).toInt()
        input.setPadding(pad, pad / 2, pad, pad / 2)
        MaterialAlertDialogBuilder(activity)
            .setTitle(R.string.pc_devices_alias_title)
            .setView(input)
            .setPositiveButton(android.R.string.ok) { _, _ ->
                val alias = input.text?.toString()?.trim() ?: ""
                PcHistory.setAlias(activity, e.host, e.http, alias)
                done()
            }
            .setNegativeButton(android.R.string.cancel, null)
            .show()
    }

    private class DeviceAdapter(
        private var items: List<PcHistoryEntry>,
        private val listener: Listener,
    ) : RecyclerView.Adapter<DeviceAdapter.Holder>() {
        interface Listener {
            fun onPick(e: PcHistoryEntry)
            fun onAlias(e: PcHistoryEntry)
            fun onDelete(e: PcHistoryEntry)
        }

        fun setItems(list: List<PcHistoryEntry>) {
            items = list
            notifyDataSetChanged()
        }

        override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): Holder {
            val row = ItemPcDeviceRowBinding.inflate(LayoutInflater.from(parent.context), parent, false)
            return Holder(row)
        }

        override fun onBindViewHolder(holder: Holder, position: Int) {
            holder.bind(items[position], listener)
        }

        override fun getItemCount() = items.size

        class Holder(private val row: ItemPcDeviceRowBinding) : RecyclerView.ViewHolder(row.root) {
            fun bind(e: PcHistoryEntry, listener: Listener) {
                row.lbtitle.text = e.displayTitle()
                row.lbsub.text = e.displaySub()
                row.rowMain.setOnClickListener { listener.onPick(e) }
                row.balias.setOnClickListener { listener.onAlias(e) }
                row.bdel.setOnClickListener { listener.onDelete(e) }
            }
        }
    }
}
