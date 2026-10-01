// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.designsystem.theme

import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.key
import androidx.compose.ui.platform.LocalFontFamilyResolver
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.platform.Font
import org.jetbrains.compose.resources.ExperimentalResourceApi
import org.jetbrains.compose.resources.FontResource
import org.jetbrains.compose.resources.ResourceEnvironment
import org.jetbrains.compose.resources.getFontResourceBytes
import org.jetbrains.compose.resources.rememberResourceEnvironment

// Skia in the browser has no system fonts. The one per-glyph fallback Compose 1.9 offers there is the font
// resolver's preload: a preloaded face joins the fallback provider, and any glyph the styled face lacks is
// looked up in it. Not preloadFont(): its loader lets a failed download escape into the composition, which
// kills the recomposer and freezes the whole dashboard over one missing script.
@OptIn(ExperimentalResourceApi::class)
@Composable
internal actual fun PreloadFallbackFonts(colorEmoji: Boolean) {
    val resolver: FontFamily.Resolver = LocalFontFamilyResolver.current
    val environment: ResourceEnvironment = rememberResourceEnvironment()
    for (face: FontResource in fallbackFontFaces(colorEmoji)) {
        key(face) {
            LaunchedEffect(face) {
                val bytes: ByteArray = readFallbackFont { getFontResourceBytes(environment, face) } ?: return@LaunchedEffect
                resolver.preload(FontFamily(Font(identity = "nnz-fallback-${face.hashCode()}", data = bytes)))
            }
        }
    }
}
