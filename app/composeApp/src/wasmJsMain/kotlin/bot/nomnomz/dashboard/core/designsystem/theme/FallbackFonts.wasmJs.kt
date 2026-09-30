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
import androidx.compose.runtime.getValue
import androidx.compose.runtime.key
import androidx.compose.ui.platform.LocalFontFamilyResolver
import androidx.compose.ui.text.font.Font
import androidx.compose.ui.text.font.FontFamily
import org.jetbrains.compose.resources.ExperimentalResourceApi
import org.jetbrains.compose.resources.FontResource
import org.jetbrains.compose.resources.preloadFont

// Skia in the browser has no system fonts. The one per-glyph fallback Compose 1.9 offers there is the font
// resolver's preload: a preloaded face joins the fallback provider, and any glyph the styled face lacks is
// looked up in it.
@OptIn(ExperimentalResourceApi::class)
@Composable
internal actual fun PreloadFallbackFonts(colorEmoji: Boolean) {
    val resolver: FontFamily.Resolver = LocalFontFamilyResolver.current
    for (face: FontResource in fallbackFontFaces(colorEmoji)) {
        key(face) {
            val font: Font? by preloadFont(face)
            LaunchedEffect(font) { font?.let { loaded: Font -> resolver.preload(FontFamily(loaded)) } }
        }
    }
}
