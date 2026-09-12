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
import androidx.compose.ui.graphics.toComposeImageBitmap
import org.jetbrains.skia.Image

// Web (wasmJs) still-image decode via Skia — see [decodeAnimatedFrames]'s wasmJs actual for why this lives
// per-target rather than shared (the two Skiko source sets have no common intermediate here).
actual fun decodeStaticImage(bytes: ByteArray): ImageBitmap? =
    try {
        Image.makeFromEncoded(bytes).toComposeImageBitmap()
    } catch (t: Throwable) {
        null
    }
