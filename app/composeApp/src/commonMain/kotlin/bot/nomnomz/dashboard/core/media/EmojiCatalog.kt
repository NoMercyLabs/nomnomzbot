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

import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import nomnomzbot.composeapp.generated.resources.Res
import org.jetbrains.compose.resources.ExperimentalResourceApi

// The standard Unicode-emoji catalogue (the full gemoji shortcode set), bundled as a compact JSON resource and
// loaded once. It lets the chat composer offer normal emoji (`:smile:` → 😀) alongside channel emotes: the picker
// searches by shortcode/keyword and inserting one drops the actual Unicode glyph into the draft, which then
// renders as the real coloured image everywhere through the same Twemoji path the feed uses ([EmojiText]).

// The compact on-disk record (files/emoji_catalog.json): g = glyph, s = shortcodes (aliases), k = extra keywords.
@Serializable
private data class EmojiRecord(val g: String, val s: List<String>, val k: String = "")

// One picker-ready emoji: its glyph (what gets inserted into the draft), all of its `:shortcodes:`, the extra
// search keywords, and the Twemoji image URL — derived from the glyph via the same tokenizer the feed uses, so a
// picked emoji and a typed one render identically.
data class EmojiEntry(
    val glyph: String,
    val shortcodes: List<String>,
    val keywords: String,
    val imageUrl: String,
) {
    val primaryShortcode: String
        get() = shortcodes.firstOrNull() ?: glyph
}

object EmojiCatalog {
    private const val ResourcePath: String = "files/emoji_catalog.json"
    private val decoder: Json = Json { ignoreUnknownKeys = true }

    // Parsed once per process; the resource is ~100 KB and immutable, so a single decode is cached for reuse.
    private var cached: List<EmojiEntry>? = null

    // Load + parse the bundled catalogue. Any record whose glyph the emoji tokenizer can't resolve to a Twemoji
    // image is dropped, so every returned entry is guaranteed to render.
    @OptIn(ExperimentalResourceApi::class)
    suspend fun load(): List<EmojiEntry> {
        cached?.let { return it }

        val bytes: ByteArray = Res.readBytes(ResourcePath)
        val records: List<EmojiRecord> = decoder.decodeFromString(bytes.decodeToString())
        val entries: List<EmojiEntry> =
            records.mapNotNull { record ->
                val imageUrl: String =
                    tokenizeEmoji(record.g)
                        .filterIsInstance<EmojiSpan.Emoji>()
                        .firstOrNull()
                        ?.imageUrl
                        ?: return@mapNotNull null
                EmojiEntry(
                    glyph = record.g,
                    shortcodes = record.s,
                    keywords = record.k,
                    imageUrl = imageUrl,
                )
            }
        cached = entries
        return entries
    }
}

// Filter the emoji catalogue for the composer autocomplete: every emoji whose shortcode or keyword CONTAINS
// [query] (case-insensitive). A shortcode prefix match ranks first, then a shortcode substring, then a keyword
// hit; ties break on the shorter shortcode, then alphabetically. Capped at [limit] so the dropdown stays compact.
fun searchEmoji(
    entries: List<EmojiEntry>,
    query: String,
    limit: Int = 6,
): List<EmojiEntry> {
    if (query.isBlank()) return emptyList()
    val q: String = query.lowercase()

    return entries
        .asSequence()
        .mapNotNull { entry ->
            val prefix: Boolean = entry.shortcodes.any { it.startsWith(q) }
            val containsShort: Boolean = prefix || entry.shortcodes.any { it.contains(q) }
            val matches: Boolean = containsShort || entry.keywords.contains(q)
            if (!matches) {
                null
            } else {
                val rank: Int = if (prefix) 0 else if (containsShort) 1 else 2
                entry to rank
            }
        }
        .sortedWith(
            compareBy<Pair<EmojiEntry, Int>> { it.second }
                .thenBy { it.first.primaryShortcode.length }
                .thenBy { it.first.primaryShortcode },
        )
        .take(limit)
        .map { it.first }
        .toList()
}
