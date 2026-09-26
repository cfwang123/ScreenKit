package com.whj.screenkit

import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Matrix
import android.media.ExifInterface
import android.net.Uri
import android.util.Log
import androidx.core.content.FileProvider
import java.io.File
import java.io.FileOutputStream
import kotlin.math.max
import kotlin.math.min

/** 按电脑下发的拍照参数缩放、压缩后再上传。 */
object PhotoCompress {
    private const val TAG = "PhotoCompress"

    /** 直接读缓存里的拍照文件。内容 Uri 的流经常不能 mark/reset，decodeStream 会得到 null。 */
    fun prepare(ctx: Context, src: File, prefs: Prefs): Uri? {
        if (!src.isFile || src.length() <= 0L) {
            Log.w(TAG, "empty photo ${src.length()}")
            return null
        }
        val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        BitmapFactory.decodeFile(src.absolutePath, bounds)
        if (bounds.outWidth <= 0 || bounds.outHeight <= 0) {
            Log.w(TAG, "bad photo ${bounds.outWidth}x${bounds.outHeight} len=${src.length()}")
            return null
        }
        val limit = prefs.photoLimitSize && prefs.photoMaxPx > 0
        val maxPx = prefs.photoMaxPx
        var sample = if (limit) calcSample(bounds.outWidth, bounds.outHeight, maxPx) else 1
        var bmp = decode(src, sample)
        var tries = 0
        while (bmp == null && tries < 3) {
            sample = (sample * 2).coerceAtLeast(2)
            bmp = decode(src, sample)
            tries++
        }
        val decoded = bmp
        if (decoded == null) {
            Log.w(TAG, "decode null ${bounds.outWidth}x${bounds.outHeight}")
            return null
        }
        var cur = decoded
        try {
            cur = applyExif(cur, src)
            if (limit) cur = fit(cur, maxPx)
            val png = prefs.photoFormat == "png"
            val ext = if (png) "png" else "jpg"
            val out = File(ctx.cacheDir, "cam/up_${System.currentTimeMillis()}.$ext")
            out.parentFile?.mkdirs()
            val wrote = FileOutputStream(out).use { os ->
                if (png) cur.compress(Bitmap.CompressFormat.PNG, 100, os)
                else cur.compress(Bitmap.CompressFormat.JPEG, prefs.photoQuality.coerceIn(1, 100), os)
            }
            if (!wrote || out.length() <= 0L) {
                Log.w(TAG, "compress failed wrote=$wrote len=${out.length()}")
                return null
            }
            return FileProvider.getUriForFile(ctx, "${ctx.packageName}.fileprovider", out)
        } finally {
            if (!cur.isRecycled) cur.recycle()
        }
    }

    fun outName(prefs: Prefs): String {
        val ext = if (prefs.photoFormat == "png") "png" else "jpg"
        return "photo_${System.currentTimeMillis()}.$ext"
    }

    private fun decode(src: File, sample: Int): Bitmap? {
        val dec = BitmapFactory.Options().apply {
            inSampleSize = sample.coerceAtLeast(1)
            inPreferredConfig = Bitmap.Config.ARGB_8888
        }
        return try {
            BitmapFactory.decodeFile(src.absolutePath, dec)
        } catch (ex: OutOfMemoryError) {
            Log.w(TAG, "oom sample=$sample", ex)
            null
        }
    }

    private fun fit(bmp: Bitmap, maxPx: Int): Bitmap {
        val w = bmp.width
        val h = bmp.height
        if (w <= maxPx && h <= maxPx) return bmp
        val s = min(maxPx.toFloat() / w, maxPx.toFloat() / h)
        val nw = max(1, (w * s).toInt())
        val nh = max(1, (h * s).toInt())
        val scaled = Bitmap.createScaledBitmap(bmp, nw, nh, true)
        if (scaled != bmp) bmp.recycle()
        return scaled
    }

    /** 重编码会丢掉 EXIF，先把方向烘进像素。 */
    private fun applyExif(bmp: Bitmap, src: File): Bitmap {
        val ori = try {
            ExifInterface(src.absolutePath).getAttributeInt(
                ExifInterface.TAG_ORIENTATION,
                ExifInterface.ORIENTATION_NORMAL,
            )
        } catch (ex: Exception) {
            Log.w(TAG, "exif", ex)
            ExifInterface.ORIENTATION_NORMAL
        }
        val m = Matrix()
        when (ori) {
            ExifInterface.ORIENTATION_ROTATE_90 -> m.postRotate(90f)
            ExifInterface.ORIENTATION_ROTATE_180 -> m.postRotate(180f)
            ExifInterface.ORIENTATION_ROTATE_270 -> m.postRotate(270f)
            ExifInterface.ORIENTATION_FLIP_HORIZONTAL -> m.preScale(-1f, 1f)
            ExifInterface.ORIENTATION_FLIP_VERTICAL -> m.preScale(1f, -1f)
            ExifInterface.ORIENTATION_TRANSPOSE -> {
                m.postRotate(90f)
                m.preScale(-1f, 1f)
            }
            ExifInterface.ORIENTATION_TRANSVERSE -> {
                m.postRotate(270f)
                m.preScale(-1f, 1f)
            }
            else -> return bmp
        }
        return try {
            val out = Bitmap.createBitmap(bmp, 0, 0, bmp.width, bmp.height, m, true)
            if (out != bmp) bmp.recycle()
            out
        } catch (ex: OutOfMemoryError) {
            Log.w(TAG, "exif oom", ex)
            bmp
        }
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
