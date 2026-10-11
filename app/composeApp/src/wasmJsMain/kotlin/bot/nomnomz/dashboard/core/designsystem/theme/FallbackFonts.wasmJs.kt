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
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.key
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
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
// A face is read only after its script was demanded (FallbackFontDemand), so the first visit stays small.
@OptIn(ExperimentalResourceApi::class)
@Composable
internal actual fun PreloadFallbackFonts(colorEmoji: Boolean): Int {
    var loaded: Int by remember { mutableIntStateOf(0) }
    val resolver: FontFamily.Resolver = LocalFontFamilyResolver.current
    val environment: ResourceEnvironment = rememberResourceEnvironment()
    val demanded: Set<FallbackScript> by FallbackFontDemand.App.scripts.collectAsState()
    for (script: FallbackScript in demanded) {
        val face: FontResource = script.face(colorEmoji)
        key(face) {
            LaunchedEffect(face) {
                val bytes: ByteArray = readFallbackFont { getFontResourceBytes(environment, face) } ?: return@LaunchedEffect
                resolver.preload(FontFamily(Font(identity = "nnz-fallback-${face.hashCode()}", data = bytes)))
                loaded++
            }
        }
    }
    return loaded
}
