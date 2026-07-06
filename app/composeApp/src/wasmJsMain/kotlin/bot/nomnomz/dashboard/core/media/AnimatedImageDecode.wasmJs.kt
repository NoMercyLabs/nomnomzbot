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

import androidx.compose.ui.graphics.toComposeImageBitmap
import org.jetbrains.skia.AnimationFrameInfo
import org.jetbrains.skia.Bitmap
import org.jetbrains.skia.Codec
import org.jetbrains.skia.Data
import org.jetbrains.skia.Image
import org.jetbrains.skia.ImageInfo

// Web (wasmJs) animated-image decode via Skia. Compose Multiplatform's web target runs on Skiko compiled to
// WebAssembly, so the same `org.jetbrains.skia` codec surface used on desktop is available here; the decode is
// byte-for-byte identical, which is why it lives per-target rather than shared (the two Skiko source sets have
// no common intermediate here). `Codec.readPixels(bitmap, frame)` composites each frame for us.
actual fun decodeAnimatedFrames(bytes: ByteArray): List<AnimatedFrame>? {
    val codec: Codec =
        try {
            Codec.makeFromData(Data.makeFromBytes(bytes))
        } catch (t: Throwable) {
            return null
        }
    return try {
        val frameCount: Int = codec.frameCount
        if (frameCount <= 1) return null

        val info: ImageInfo = codec.imageInfo
        val timings: Array<AnimationFrameInfo> = codec.framesInfo
        val result: ArrayList<AnimatedFrame> = ArrayList(frameCount)
        for (i in 0 until frameCount) {
            val bitmap: Bitmap = Bitmap()
            bitmap.allocPixels(info)
            codec.readPixels(bitmap, i)
            bitmap.setImmutable()
            val image: Image = Image.makeFromBitmap(bitmap)
            val duration: Int = timings.getOrNull(i)?.duration ?: 0
            result.add(AnimatedFrame(image.toComposeImageBitmap(), duration))
        }
        result
    } catch (t: Throwable) {
        null
    } finally {
        codec.close()
    }
}
