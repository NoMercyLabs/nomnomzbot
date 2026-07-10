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

// Splits a chat text run into text spans and Unicode-emoji spans, and maps each emoji cluster to its Twemoji
// image filename. This is what fixes the "tofu box" render on the web/Wasm build: Compose has no colour-emoji
// font there, so a raw 🤣 renders as □. Twitch's own chat (and FrankerFaceZ) solve this the same way — they
// swap each Unicode emoji for a Twemoji <img>. We tokenize the string, hand the text spans to Compose `Text`,
// and render each emoji span as a small inline image from the FFZ Twemoji CDN.
//
// Pure logic (no Compose) so it is unit-testable off-device; the rendering half lives in `EmojiText`.

// The jsDelivr-hosted jdecked/twemoji image set — the maintained Twemoji fork, pinned to a complete current
// release. Files are keyed by the emoji's code point(s) in lowercase hex joined by '-' (see [twemojiCodePoints]);
// e.g. 🤣 (U+1F923) → `.../72x72/1f923.png`. Chosen over the older FFZ mirror (~v13.1) which 404s every Unicode
// 14.0+ emoji — including 🫠 (U+1FAE0), keycaps, heart-on-fire. Same filename scheme, CORS-enabled.
const val TWEMOJI_CDN_BASE: String =
    "https://cdn.jsdelivr.net/gh/jdecked/twemoji@16.0.1/assets/72x72"

/** The Twemoji image URL for a code-point filename (e.g. `"1f923"` → `".../twemoji/1f923.png"`). */
fun twemojiUrl(codepoints: String): String = "$TWEMOJI_CDN_BASE/$codepoints.png"

// One span of a tokenized chat run: either plain text (rendered as `Text`) or a single emoji cluster (rendered as
// its Twemoji image). [EmojiSpan.raw] keeps the original characters (used as the image's alt text and for copy);
// [EmojiSpan.codepoints] is the Twemoji filename stem.
sealed interface EmojiSpan {
    data class Text(val text: String) : EmojiSpan

    data class Emoji(val raw: String, val codepoints: String) : EmojiSpan {
        val imageUrl: String
            get() = twemojiUrl(codepoints)
    }
}

// Splits [text] into an ordered list of [EmojiSpan]s: maximal non-emoji runs stay [EmojiSpan.Text], each emoji
// cluster (a base plus its variation selectors / skin-tone modifiers / ZWJ-joined parts / keycap combiner, or a
// regional-indicator flag pair) becomes one [EmojiSpan.Emoji]. Plain text with no emoji yields a single Text
// span; an empty string yields no spans.
fun tokenizeEmoji(text: String): List<EmojiSpan> {
    if (text.isEmpty()) return emptyList()

    val codePoints: IntArray = text.toCodePointArray()
    val spans: MutableList<EmojiSpan> = ArrayList()
    val pending: StringBuilder = StringBuilder()

    var i = 0
    while (i < codePoints.size) {
        val clusterEnd: Int = matchEmojiCluster(codePoints, i)
        if (clusterEnd > i) {
            if (pending.isNotEmpty()) {
                spans.add(EmojiSpan.Text(pending.toString()))
                pending.setLength(0)
            }
            val raw: StringBuilder = StringBuilder()
            for (k in i until clusterEnd) codePoints[k].appendCodePointTo(raw)
            val cluster: IntArray = codePoints.copyOfRange(i, clusterEnd)
            spans.add(EmojiSpan.Emoji(raw = raw.toString(), codepoints = twemojiCodePoints(cluster)))
            i = clusterEnd
        } else {
            codePoints[i].appendCodePointTo(pending)
            i++
        }
    }
    if (pending.isNotEmpty()) spans.add(EmojiSpan.Text(pending.toString()))
    return spans
}

// The Twemoji filename stem for a resolved cluster — mirrors twemoji.js `grabTheRightIcon`/`toCodePoint`: join the
// code points as lowercase hex with '-', but drop the U+FE0F emoji-variation selector UNLESS the cluster is a ZWJ
// sequence (Twemoji keeps FE0F inside ZWJ sequences — e.g. 🏳️‍🌈 → `1f3f3-fe0f-200d-1f308` — but strips it from
// simple emoji — ❤️ → `2764`). U+FE0E (text-presentation selector) is never part of a Twemoji filename.
internal fun twemojiCodePoints(cluster: IntArray): String {
    val hasZeroWidthJoiner: Boolean = cluster.any { it == ZERO_WIDTH_JOINER }
    return cluster
        .filter { cp -> cp != TEXT_VARIATION_SELECTOR && (hasZeroWidthJoiner || cp != EMOJI_VARIATION_SELECTOR) }
        .joinToString("-") { it.toString(16) }
}

// Returns the exclusive end index of the emoji cluster starting at [start], or [start] itself when no emoji
// begins there. Order matters: keycap and regional-indicator pairs are recognised before the general path so a
// bare digit or a lone flag half stays text.
private fun matchEmojiCluster(cp: IntArray, start: Int): Int {
    val size: Int = cp.size
    val first: Int = cp[start]

    // Keycap: (0-9 | # | *) FE0F? U+20E3. A digit/# not followed by the keycap combiner is ordinary text.
    if (isKeycapBase(first)) {
        var j = start + 1
        if (j < size && cp[j] == EMOJI_VARIATION_SELECTOR) j++
        if (j < size && cp[j] == KEYCAP_COMBINER) return j + 1
        return start
    }

    // Flag: a pair of regional indicators. A single unpaired indicator is text.
    if (isRegionalIndicator(first)) {
        return if (start + 1 < size && isRegionalIndicator(cp[start + 1])) start + 2 else start
    }

    if (!isEmojiBase(first)) return start

    // Base emoji, then its trailing modifiers, then any ZWJ-joined continuations (each with their own modifiers).
    var j: Int = consumeModifiers(cp, start + 1)
    while (j < size && cp[j] == ZERO_WIDTH_JOINER) {
        val afterJoiner: Int = j + 1
        if (afterJoiner < size && (isEmojiBase(cp[afterJoiner]) || isRegionalIndicator(cp[afterJoiner]))) {
            j = consumeModifiers(cp, afterJoiner + 1)
        } else {
            break // a dangling ZWJ — stop before it so it renders as text.
        }
    }
    return j
}

// Consumes trailing cluster modifiers from [from]: variation selectors, skin-tone modifiers, and tag characters
// (subdivision-flag tags). Returns the first index that is not a modifier.
private fun consumeModifiers(cp: IntArray, from: Int): Int {
    var j: Int = from
    while (j < cp.size) {
        val c: Int = cp[j]
        val isModifier: Boolean =
            c == EMOJI_VARIATION_SELECTOR ||
                c == TEXT_VARIATION_SELECTOR ||
                c in SKIN_TONE_RANGE ||
                c in TAG_RANGE
        if (isModifier) j++ else break
    }
    return j
}

private const val ZERO_WIDTH_JOINER: Int = 0x200D
private const val EMOJI_VARIATION_SELECTOR: Int = 0xFE0F
private const val TEXT_VARIATION_SELECTOR: Int = 0xFE0E
private const val KEYCAP_COMBINER: Int = 0x20E3
private val SKIN_TONE_RANGE: IntRange = 0x1F3FB..0x1F3FF
private val TAG_RANGE: IntRange = 0xE0020..0xE007F

private fun isKeycapBase(cp: Int): Boolean = cp == 0x23 || cp == 0x2A || cp in 0x30..0x39

private fun isRegionalIndicator(cp: Int): Boolean = cp in 0x1F1E6..0x1F1FF

// Whether [cp] can START an emoji cluster. Covers the pictographic blocks that render as colour emoji (and so as
// tofu without an emoji font): the supplementary-plane emoji blocks plus the emoji-carrying BMP symbol ranges.
// Regional indicators are deliberately excluded here — they are handled only as flag pairs by [matchEmojiCluster].
private fun isEmojiBase(cp: Int): Boolean {
    // Supplementary-plane emoji & pictographs (emoticons, symbols, transport, supplemental, extended-A). The
    // regional-indicator sub-range (0x1F1E6..0x1F1FF) sits inside this span but is filtered out first above.
    if (cp in 0x1F000..0x1FAFF) return true
    // BMP symbol & dingbat blocks that carry emoji.
    if (cp in 0x2600..0x27BF) return true // Misc Symbols + Dingbats (☀ ✂ ✅ ❤ …)
    if (cp in 0x2194..0x21AA) return true // arrows with emoji presentation
    if (cp in 0x231A..0x231B) return true // ⌚ ⌛
    if (cp in 0x23E9..0x23F3) return true // ⏩ ⏰ ⏳ …
    if (cp in 0x23F8..0x23FA) return true // ⏸ ⏹ ⏺
    if (cp in 0x25AA..0x25AB) return true // ▪ ▫
    if (cp in 0x25FB..0x25FE) return true // ◻ ◼ …
    if (cp in 0x2B05..0x2B07) return true // ⬅ ⬆ ⬇
    if (cp in 0x2B1B..0x2B1C) return true // ⬛ ⬜
    return when (cp) {
        0x00A9, 0x00AE, 0x203C, 0x2049, 0x2122, 0x2139,
        0x2328, 0x23CF, 0x24C2, 0x25B6, 0x25C0,
        0x2934, 0x2935, 0x2B50, 0x2B55,
        0x3030, 0x303D, 0x3297, 0x3299 -> true
        else -> false
    }
}

// Decodes a UTF-16 [String] into its Unicode code points (kotlin-common has no `codePointAt`), pairing
// surrogates; an unpaired surrogate passes through as its own code unit.
private fun String.toCodePointArray(): IntArray {
    val result: MutableList<Int> = ArrayList(length)
    var i = 0
    while (i < length) {
        val high: Char = this[i]
        if (high.isHighSurrogate() && i + 1 < length && this[i + 1].isLowSurrogate()) {
            val low: Char = this[i + 1]
            result.add(0x10000 + ((high.code - 0xD800) shl 10) + (low.code - 0xDC00))
            i += 2
        } else {
            result.add(high.code)
            i++
        }
    }
    return result.toIntArray()
}

// Re-encodes a code point back into [target] as UTF-16 (one char, or a surrogate pair above the BMP).
private fun Int.appendCodePointTo(target: StringBuilder) {
    if (this <= 0xFFFF) {
        target.append(this.toChar())
    } else {
        val offset: Int = this - 0x10000
        target.append((0xD800 + (offset shr 10)).toChar())
        target.append((0xDC00 + (offset and 0x3FF)).toChar())
    }
}
