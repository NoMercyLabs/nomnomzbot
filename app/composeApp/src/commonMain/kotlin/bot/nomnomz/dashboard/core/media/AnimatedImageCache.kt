// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.media

import androidx.compose.ui.graphics.ImageBitmap
import io.ktor.client.HttpClient
import io.ktor.client.request.get
import io.ktor.client.statement.HttpResponse
import io.ktor.client.statement.readRawBytes
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock

// One composited frame of an animated image: the ready-to-draw RGBA frame and how long to hold it.
data class AnimatedFrame(val image: ImageBitmap, val durationMillis: Int)

// Decodes an animated image's encoded bytes into its composited frames through the platform Skia `Codec`, or
// returns null when the source is not animated / cannot be decoded. Implemented once per Skia-backed target
// (jvm + wasmJs); `coil-gif` is Android-only, so the desktop and web builds decode animation here instead.
expect fun decodeAnimatedFrames(bytes: ByteArray): List<AnimatedFrame>?

// Fetches + decodes animated emote / badge / cheermote images once per URL and caches the outcome, so a chat
// feed that repeats an emote never re-downloads or re-decodes it. Sources that turn out static (or fail to
// decode) are remembered as such so they are probed only once. Bounded by [MAX_ENTRIES] with insertion-order
// eviction to cap the retained frame bitmaps.
object AnimatedImageCache {
    private const val MAX_ENTRIES: Int = 256
    private const val MIN_FRAME_MILLIS: Int = 20

    // A plain, unauthenticated client for third-party emote CDNs (7TV / Twitch / BTTV / FFZ) — deliberately
    // separate from the app's authed API client. The engine is the single one on each target's classpath
    // (CIO on desktop, Fetch on web), so the no-argument factory resolves it.
    private val client: HttpClient by lazy { HttpClient() }
    private val mutex: Mutex = Mutex()
    private val framesByUrl: LinkedHashMap<String, List<AnimatedFrame>> = LinkedHashMap()
    private val staticUrls: HashSet<String> = HashSet()

    // Returns the animated frames for [url], or null when the source is static / undecodable. Suspends off the
    // UI frame (network + decode) and is safe to call from composition; repeated calls for the same URL are
    // served from the cache.
    suspend fun framesFor(url: String): List<AnimatedFrame>? {
        mutex.withLock {
            framesByUrl[url]?.let { return it }
            if (url in staticUrls) return null
        }

        val decoded: List<AnimatedFrame>? =
            try {
                val response: HttpResponse = client.get(url)
                val bytes: ByteArray = response.readRawBytes()
                decodeAnimatedFrames(bytes)
            } catch (t: Throwable) {
                null
            }

        val normalized: List<AnimatedFrame>? =
            decoded
                ?.takeIf { it.size > 1 }
                ?.map { frame ->
                    if (frame.durationMillis < MIN_FRAME_MILLIS) {
                        frame.copy(durationMillis = MIN_FRAME_MILLIS)
                    } else {
                        frame
                    }
                }

        mutex.withLock {
            if (normalized != null) {
                framesByUrl[url] = normalized
                if (framesByUrl.size > MAX_ENTRIES) {
                    val oldest: String = framesByUrl.keys.first()
                    framesByUrl.remove(oldest)
                }
            } else {
                staticUrls.add(url)
            }
        }
        return normalized
    }
}
