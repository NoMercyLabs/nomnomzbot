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
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.delay
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.noto_emoji
import nomnomzbot.composeapp.generated.resources.noto_sans
import nomnomzbot.composeapp.generated.resources.noto_sans_arabic
import nomnomzbot.composeapp.generated.resources.noto_sans_canadian_aboriginal
import nomnomzbot.composeapp.generated.resources.noto_sans_cherokee
import nomnomzbot.composeapp.generated.resources.noto_sans_kr
import nomnomzbot.composeapp.generated.resources.noto_sans_math
import nomnomzbot.composeapp.generated.resources.noto_sans_sc
import nomnomzbot.composeapp.generated.resources.noto_sans_thai
import nomnomzbot.composeapp.generated.resources.twemoji_color
import org.jetbrains.compose.resources.FontResource

// The faces that draw what Inter cannot: chat, display names and blocked terms arrive in any script, and spam
// bots spell blocked words with look-alike letters (`ᐯＩ𝖤ᗯᴇᖇ𝚂`). Coverage, checked against each file's cmap:
// - Noto Sans: Cyrillic, Greek, Vietnamese, Devanagari, extended Latin, small-capital phonetic letters (ᴇ)
// - Noto Sans Arabic / Thai: those scripts
// - Noto Sans SC: Han, Hiragana/Katakana, Bopomofo, fullwidth forms (Ｉ)
// - Noto Sans KR: Hangul
// - Noto Sans Math: Mathematical Alphanumeric Symbols (𝑻 𝖦 𝚂) and Letterlike symbols
// - Noto Sans Canadian Aboriginal: Unified Canadian Aboriginal Syllabics (ᑎ ᐯ ᗯ ᖇ)
// - Noto Sans Cherokee: Cherokee (Ꮩ)
// The emoji face follows the operator's EmojiStyle preference. All Noto faces are SIL OFL 1.1
// (composeResources/files/licenses/OFL-fonts.txt).
internal fun fallbackFontFaces(colorEmoji: Boolean): List<FontResource> =
    listOf(
        Res.font.noto_sans,
        Res.font.noto_sans_arabic,
        Res.font.noto_sans_thai,
        Res.font.noto_sans_sc,
        Res.font.noto_sans_kr,
        Res.font.noto_sans_math,
        Res.font.noto_sans_canadian_aboriginal,
        Res.font.noto_sans_cherokee,
        if (colorEmoji) Res.font.twemoji_color else Res.font.noto_emoji,
    )

// Registers [fallbackFontFaces] as per-glyph fallbacks. A FontFamily picks ONE face per weight and style and
// never walks its list per glyph, so the faces cannot simply be listed beside Inter.
@Composable
internal expect fun PreloadFallbackFonts(colorEmoji: Boolean)

private val FALLBACK_FONT_RETRY_DELAYS_MS: List<Long> = listOf(2_000, 5_000)

// Reads one fallback face, retrying a transfer that dies part-way (the CJK faces are 10 and 17 MB). Returns
// null when every attempt failed: a missing fallback costs that script's glyphs, never the dashboard. Catches
// Throwable because a failed browser fetch on Kotlin/Wasm is a JsException, which is not an Exception.
internal suspend fun readFallbackFont(read: suspend () -> ByteArray): ByteArray? {
    for (attempt: Int in 0..FALLBACK_FONT_RETRY_DELAYS_MS.size) {
        try {
            return read()
        } catch (cause: Throwable) {
            if (cause is CancellationException) throw cause
        }
        FALLBACK_FONT_RETRY_DELAYS_MS.getOrNull(attempt)?.let { backoff: Long -> delay(backoff) }
    }
    return null
}
