package com.whj.screenkit

import android.content.Context

object CastDiscover {
    fun scan(ctx: Context, timeoutMs: Int = 4000): List<Peer> {
        return Discover.scan(ctx, timeoutMs).map {
            Peer(it.name, it.host, it.port, "pc", it.port)
        }
    }
}
