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
import androidx.compose.runtime.Immutable
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.sp
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.inter
import org.jetbrains.compose.resources.Font

// The fixed type scale (frontend-design-system.md §1.3). Feature code reads
// `Typography.*` — no inline `TextStyle`.
@Immutable
data class Typography(
    val xs: TextStyle = TextStyle(fontSize = 12.sp, lineHeight = 16.sp, fontWeight = FontWeight.Normal),
    val sm: TextStyle = TextStyle(fontSize = 14.sp, lineHeight = 20.sp, fontWeight = FontWeight.Normal),
    val base: TextStyle = TextStyle(fontSize = 16.sp, lineHeight = 24.sp, fontWeight = FontWeight.Normal),
    val lg: TextStyle = TextStyle(fontSize = 18.sp, lineHeight = 28.sp, fontWeight = FontWeight.Normal),
    val xl: TextStyle = TextStyle(fontSize = 20.sp, lineHeight = 28.sp, fontWeight = FontWeight.Medium),
    val xl2: TextStyle = TextStyle(fontSize = 24.sp, lineHeight = 32.sp, fontWeight = FontWeight.SemiBold),
    val xl3: TextStyle = TextStyle(fontSize = 30.sp, lineHeight = 36.sp, fontWeight = FontWeight.SemiBold),
    val xl4: TextStyle = TextStyle(fontSize = 36.sp, lineHeight = 40.sp, fontWeight = FontWeight.Bold),
)

internal val DefaultTypography: Typography = Typography()

// The bundled type face: Inter, the design system's sans (§1.3). Inter covers Latin only; every other script
// and emoji comes from the faces in FallbackFonts.kt, which the theme root registers as per-glyph fallbacks.
@Composable
fun appTypography(): Typography {
    val family: FontFamily =
        FontFamily(
            Font(Res.font.inter, FontWeight.Normal),
            Font(Res.font.inter, FontWeight.Medium),
            Font(Res.font.inter, FontWeight.SemiBold),
            Font(Res.font.inter, FontWeight.Bold),
        )
    return Typography(
        xs = DefaultTypography.xs.copy(fontFamily = family),
        sm = DefaultTypography.sm.copy(fontFamily = family),
        base = DefaultTypography.base.copy(fontFamily = family),
        lg = DefaultTypography.lg.copy(fontFamily = family),
        xl = DefaultTypography.xl.copy(fontFamily = family),
        xl2 = DefaultTypography.xl2.copy(fontFamily = family),
        xl3 = DefaultTypography.xl3.copy(fontFamily = family),
        xl4 = DefaultTypography.xl4.copy(fontFamily = family),
    )
}
