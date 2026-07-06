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

import androidx.compose.foundation.Image
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.layout.ContentScale
import coil3.compose.AsyncImage
import kotlinx.coroutines.delay

// A remote image that PLAYS animated GIF / WebP / APNG on the Skia-backed dashboard targets (desktop + web).
//
// coil3's `AsyncImage` only decodes the FIRST frame off-Android — the `coil-gif` decoder is an Android-only
// artifact and coil-core's non-Android `Image` has no animated form (coil issue #2347, open), so animated
// emotes/cheermotes render frozen. This wraps `AsyncImage` and, in parallel, probes the URL for multiple
// frames through the platform Skia `Codec` ([AnimatedImageCache]). When the source is animated it cycles the
// decoded frames on their own per-frame timing; otherwise — static source, decode failure, or still loading —
// it stays on the battle-tested `AsyncImage` path, so a non-animated or failed image renders exactly as it
// does today (no regression, no per-call-site change beyond the swap).
@Composable
fun AnimatedNetworkImage(
    url: String,
    contentDescription: String?,
    modifier: Modifier = Modifier,
    contentScale: ContentScale = ContentScale.Fit,
) {
    var frames: List<AnimatedFrame>? by remember(url) { mutableStateOf(null) }
    LaunchedEffect(url) { frames = AnimatedImageCache.framesFor(url) }

    val animated: List<AnimatedFrame>? = frames
    if (animated != null && animated.size > 1) {
        var index: Int by remember(animated) { mutableStateOf(0) }
        LaunchedEffect(animated) {
            while (true) {
                delay(animated[index].durationMillis.toLong())
                index = (index + 1) % animated.size
            }
        }
        Image(
            bitmap = animated[index].image,
            contentDescription = contentDescription,
            modifier = modifier,
            contentScale = contentScale,
        )
    } else {
        AsyncImage(
            model = url,
            contentDescription = contentDescription,
            modifier = modifier,
            contentScale = contentScale,
        )
    }
}
