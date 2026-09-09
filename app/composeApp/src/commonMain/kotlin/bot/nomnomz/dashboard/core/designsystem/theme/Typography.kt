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
import nomnomzbot.composeapp.generated.resources.noto_emoji
import nomnomzbot.composeapp.generated.resources.noto_sans
import nomnomzbot.composeapp.generated.resources.noto_sans_arabic
import nomnomzbot.composeapp.generated.resources.noto_sans_kr
import nomnomzbot.composeapp.generated.resources.noto_sans_sc
import nomnomzbot.composeapp.generated.resources.noto_sans_thai
import nomnomzbot.composeapp.generated.resources.twemoji_color
import org.jetbrains.compose.resources.Font

// The fixed type scale (frontend-design-system.md §1.3). Feature code reads
// `Typography.*` — no inline `TextStyle`. Font defaults to the platform sans here; the
// bundled Inter `FontFamily` token wires in with the resources/font slice.
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

// The bundled type face: Inter (the design-system's intended sans, §1.3) with bundled script-fallback and
// emoji faces so text renders as real glyphs instead of □ tofu — INCLUDING in editable text fields, where
// the inline-image [EmojiText] path cannot reach. Every [Typography] style carries this family, so all app
// text (and the fields that read `typography.*`) shares the same coverage. Skia/Wasm has no system fonts,
// so every fallback only works because its face is bundled here — there is nothing else to fall through to.
//
// Inter (Latin only) covers the app's own UI languages (en, nl), but chat messages and viewer-entered
// content (display names, quotes, custom command text) can be in ANY script regardless of the app's UI
// language. FontFamily resolves per-glyph, in list order: for each character, Skia walks the fonts below
// until one has that glyph, so a mixed-script string (e.g. Latin + Cyrillic in one line) renders correctly
// without the app knowing the language in advance. Coverage was verified against each face's actual cmap
// (fontTools), not assumed from the family name:
// - Noto Sans (variable): Cyrillic, Greek, Vietnamese, Devanagari, extended Latin
// - Noto Sans Arabic: Arabic script (incl. Persian/Urdu extensions)
// - Noto Sans Thai: Thai script
// - Noto Sans SC: Han ideographs (Simplified + Traditional codepoints) + Hiragana/Katakana + Bopomofo —
//   covers Chinese and the Han/Kana portion of Japanese; Han glyph *shapes* default to the Simplified
//   style even for Traditional-only codepoints, so Traditional Chinese/Japanese text stays legible but
//   won't always show the regionally-preferred stroke form
// - Noto Sans KR: Hangul syllables + Jamo — Noto Sans SC does NOT include Hangul, so Korean needs this
//   separate face
// Not covered by this set (a scope call, not an oversight): Armenian, Georgian, Hebrew, and scripts
// outside the six above. All five Noto faces are Google's Noto Sans family, SIL Open Font License 1.1
// (github.com/google/fonts, ofl/notosans*), same redistribution terms as bundling any other open font.
//
// [colorEmoji] picks the emoji face live from the operator's persisted EmojiStyle preference: the color
// (Twemoji COLR) face by default, or the monochrome (Noto Emoji) face as the fallback for a browser/Skia
// build that can't render COLR glyphs. Inter's weights are unchanged either way.
@Composable
fun appTypography(colorEmoji: Boolean): Typography {
    val family: FontFamily =
        FontFamily(
            Font(Res.font.inter, FontWeight.Normal),
            Font(Res.font.inter, FontWeight.Medium),
            Font(Res.font.inter, FontWeight.SemiBold),
            Font(Res.font.inter, FontWeight.Bold),
            Font(Res.font.noto_sans),
            Font(Res.font.noto_sans_arabic),
            Font(Res.font.noto_sans_thai),
            Font(Res.font.noto_sans_sc),
            Font(Res.font.noto_sans_kr),
            Font(if (colorEmoji) Res.font.twemoji_color else Res.font.noto_emoji),
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
