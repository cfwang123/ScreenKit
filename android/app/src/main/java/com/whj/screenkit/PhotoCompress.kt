package com.whj.screenkit

import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.net.Uri
import androidx.core.content.FileProvider
import java.io.File
import java.io.FileOutputStream
import kotlin.math.max
import kotlin.math.min

/** 按电脑下发的拍照参数缩放、压缩后再上传。 */
object PhotoCompress {
    fun prepare(ctx: Context, src: Uri, prefs: Prefs): Uri? {
        val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        ctx.contentResolver.openInputStream(src)?.use {
            BitmapFactory.decodeStream(it, null, bounds)
        } ?: return null
        if (bounds.outWidth <= 0 || bounds.outHeight <= 0) return null
        val sample = if (prefs.photoLimitSize && prefs.photoMaxPx > 0)
            calcSample(bounds.outWidth, bounds.outHeight, prefs.photoMaxPx)
        else 1
        val dec = BitmapFactory.Options().apply {
            inSampleSize = sample
            inPreferredConfig = Bitmap.Config.ARGB_8888
        }
        var bmp = ctx.contentResolver.openInputStream(src)?.use {
            BitmapFactory.decodeStream(it, null, dec)
        } ?: return null
        if (prefs.photoLimitSize && prefs.photoMaxPx > 0) {
            val max = prefs.photoMaxPx
            val w = bmp.width
            val h = bmp.height
            if (w > max || h > max) {
                val s = min(max.toFloat() / w, max.toFloat() / h)
                val nw = max(1, (w * s).toInt())
                val nh = max(1, (h * s).toInt())
                val scaled = Bitmap.createScaledBitmap(bmp, nw, nh, true)
                if (scaled != bmp) bmp.recycle()
                bmp = scaled
            }
        }
        val png = prefs.photoFormat == "png"
        val ext = if (png) "png" else "jpg"
        val out = File(ctx.cacheDir, "cam/up_${System.currentTimeMillis()}.$ext")
        out.parentFile?.mkdirs()
        try {
            FileOutputStream(out).use { os ->
                if (png) bmp.compress(Bitmap.CompressFormat.PNG, 100, os)
                else bmp.compress(Bitmap.CompressFormat.JPEG, prefs.photoQuality.coerceIn(1, 100), os)
            }
        } finally {
            bmp.recycle()
        }
        return FileProvider.getUriForFile(ctx, "${ctx.packageName}.fileprovider", out)
    }

    fun outName(prefs: Prefs): String {
        val ext = if (prefs.photoFormat == "png") "png" else "jpg"
        return "photo_${System.currentTimeMillis()}.$ext"
    }

    private fun calcSample(w: Int, h: Int, maxPx: Int): Int {
        var sample = 1
        var cw = w
        var ch = h
        while (cw > maxPx * 2 || ch > maxPx * 2) {
            sample *= 2
            cw /= 2
            ch /= 2
        }
        return sample.coerceAtLeast(1)
    }
}
