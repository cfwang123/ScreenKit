package com.whj.screenkit

import android.content.Context
import android.content.res.ColorStateList
import android.graphics.Color
import androidx.core.content.ContextCompat
import com.google.android.material.button.MaterialButton

object CastUi {
    fun applyCastButton(btn: MaterialButton, casting: Boolean, ctx: Context) {
        val accent = ContextCompat.getColor(ctx, R.color.accent)
        val accentDark = ContextCompat.getColor(ctx, R.color.accent_dark)
        val surface = ContextCompat.getColor(ctx, R.color.surface)
        val accentSoft = ContextCompat.getColor(ctx, R.color.accent_soft)
        val strokeOut = Color.parseColor("#E2E8F0")
        val d = ctx.resources.displayMetrics.density
        if (casting) {
            btn.text = ctx.getString(R.string.menu_cast_active)
            btn.backgroundTintList = ColorStateList.valueOf(accentSoft)
            btn.strokeColor = ColorStateList.valueOf(accent)
            btn.strokeWidth = (2f * d).toInt().coerceAtLeast(1)
            btn.setTextColor(accentDark)
        } else {
            btn.text = ctx.getString(R.string.menu_goto_cast)
            btn.backgroundTintList = ColorStateList.valueOf(surface)
            btn.strokeColor = ColorStateList.valueOf(strokeOut)
            btn.strokeWidth = (1f * d).toInt().coerceAtLeast(1)
            btn.setTextColor(accent)
        }
    }
}
