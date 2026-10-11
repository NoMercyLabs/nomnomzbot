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

import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow

// The scripts Inter cannot draw, one per fallback face. The app loads a face only after its script has shown
// up in text the operator sees, so the first visit carries Inter alone. Inter already covers Latin, extended
// Latin, Vietnamese and basic Cyrillic (checked against its cmap), so those never ask for a face.
internal enum class FallbackScript(private val ranges: List<IntRange>) {
    // Noto Sans: Greek, Devanagari, small-capital phonetic letters, extended Cyrillic and similar.
    ExtendedLatin(
        listOf(
            0x0370..0x03FF, 0x0460..0x052F, 0x0900..0x097F, 0x1D00..0x1DBF, 0x1F00..0x1FFF, 0x2C60..0x2C7F,
            0xA720..0xA7FF,
        ),
    ),
    Arabic(listOf(0x0600..0x06FF, 0x0750..0x077F, 0x08A0..0x08FF, 0xFB50..0xFDFF, 0xFE70..0xFEFF)),
    Thai(listOf(0x0E00..0x0E7F)),
    Hangul(listOf(0x1100..0x11FF, 0x3130..0x318F, 0xA960..0xA97F, 0xAC00..0xD7FF)),
    // Han, kana, Bopomofo and the fullwidth forms (Ｉ) all live in Noto Sans SC.
    Han(
        listOf(
            0x2E80..0x2FDF, 0x3000..0x303F, 0x3040..0x30FF, 0x3100..0x312F, 0x31F0..0x31FF, 0x3200..0x33FF,
            0x3400..0x4DBF, 0x4E00..0x9FFF, 0xF900..0xFAFF, 0xFF00..0xFFEF, 0x20000..0x2FA1F,
        ),
    ),
    Math(listOf(0x2100..0x214F, 0x1D400..0x1D7FF)),
    CanadianAboriginal(listOf(0x1400..0x167F, 0x18B0..0x18FF)),
    Cherokee(listOf(0x13A0..0x13FF, 0xAB70..0xABBF)),
    Emoji(listOf(0x2600..0x27BF, 0x1F000..0x1FAFF)),
    ;

    private fun covers(codePoint: Int): Boolean = ranges.any { range: IntRange -> codePoint in range }

    companion object {
        // Below this nothing is Inter-uncovered except the scripts above, so Latin chat never pays the scan.
        private const val FIRST_FALLBACK_CODE_POINT: Int = 0x0370

        /** The scripts [text] needs a fallback face for. Reads surrogate pairs as one code point. */
        fun scriptsIn(text: String): Set<FallbackScript> {
            val found: MutableSet<FallbackScript> = mutableSetOf()
            var index: Int = 0
            while (index < text.length) {
                val codePoint: Int = codePointAt(text, index)
                index += if (codePoint > 0xFFFF) 2 else 1
                if (codePoint < FIRST_FALLBACK_CODE_POINT) continue
                entries.firstOrNull { script: FallbackScript -> script.covers(codePoint) }?.let { found += it }
                if (found.size == entries.size) break
            }
            return found
        }

        /**
         * The scripts a JSON [body] needs. The server escapes non-ASCII as `\uXXXX`, so the escapes are decoded
         * first; an escaped backslash stays a literal backslash and asks for nothing.
         */
        fun scriptsInJson(body: String): Set<FallbackScript> {
            if (body.none { char: Char -> char == '\\' || char.code >= FIRST_FALLBACK_CODE_POINT }) return emptySet()
            return scriptsIn(decodeUnicodeEscapes(body))
        }

        private fun codePointAt(text: String, index: Int): Int {
            val high: Char = text[index]
            val low: Char? = text.getOrNull(index + 1)
            if (high.isHighSurrogate() && low != null && low.isLowSurrogate()) {
                return 0x10000 + ((high.code - 0xD800) shl 10) + (low.code - 0xDC00)
            }
            return high.code
        }

        private fun decodeUnicodeEscapes(body: String): String {
            val decoded: StringBuilder = StringBuilder(body.length)
            var index: Int = 0
            while (index < body.length) {
                val char: Char = body[index]
                if (char != '\\' || index + 1 >= body.length) {
                    decoded.append(char)
                    index++
                    continue
                }
                val next: Char = body[index + 1]
                val unit: Int? = if (next == 'u') body.substring(index + 2, minOf(index + 6, body.length)).toIntOrNull(16) else null
                if (unit != null && index + 6 <= body.length) {
                    decoded.append(unit.toChar())
                    index += 6
                } else {
                    // Any other escape, including `\\`, is skipped as a pair so its second char is never misread.
                    index += 2
                }
            }
            return decoded.toString()
        }
    }
}

// Every script seen so far. It only grows: a face that was asked for stays loaded.
internal class FallbackFontDemand {
    private val seen: MutableStateFlow<Set<FallbackScript>> = MutableStateFlow(emptySet())
    val scripts: StateFlow<Set<FallbackScript>> = seen

    fun observe(text: String) = add(FallbackScript.scriptsIn(text))

    fun observeJson(body: String) = add(FallbackScript.scriptsInJson(body))

    private fun add(found: Set<FallbackScript>) {
        if (found.isEmpty()) return
        seen.value = seen.value + found
    }

    companion object {
        /** The one demand the running app shares between the network layer, the hubs, the text fields and the theme. */
        val App: FallbackFontDemand = FallbackFontDemand()
    }
}
