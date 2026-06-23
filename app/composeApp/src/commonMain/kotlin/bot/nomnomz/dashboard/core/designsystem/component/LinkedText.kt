// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.designsystem.component

import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.LinkAnnotation
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.TextLinkStyles
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.text.withLink
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens

// The `LinkedText` design-system component: a body line where any embedded `http(s)://` URL is rendered as
// a clickable link that opens in the system browser, and the surrounding prose stays plain. Cross-target
// (jvm desktop + wasmJs web) — the click goes through Compose's own `LinkAnnotation.Url`, which the runtime
// routes to the platform `UriHandler` (the same browser-opening seam OAuthLauncher uses), so this needs no
// expect/actual. Link colour is the accent token; never a raw hex.

/**
 * Render [text] with every URL it contains turned into a clickable accent link (opens externally on click);
 * non-URL spans render in [color] at [style]. When the line has no URL this is just a styled [Text].
 */
@Composable
fun LinkedText(
    text: String,
    style: TextStyle,
    color: androidx.compose.ui.graphics.Color,
    modifier: Modifier = Modifier,
) {
    val tokens = LocalTokens.current

    val linkStyles: TextLinkStyles =
        TextLinkStyles(
            style = SpanStyle(color = tokens.primary, textDecoration = TextDecoration.Underline),
        )

    val annotated: AnnotatedString =
        buildAnnotatedString {
            var cursor = 0
            for (match: UrlMatch in findUrls(text)) {
                if (match.start > cursor) append(text.substring(cursor, match.start))
                withLink(LinkAnnotation.Url(url = match.url, styles = linkStyles)) {
                    append(match.url)
                }
                cursor = match.end
            }
            if (cursor < text.length) append(text.substring(cursor))
        }

    Text(text = annotated, style = style, color = color, modifier = modifier)
}

/** One URL span found inside a line: its [url] and its `[start, end)` character range in the source text. */
private data class UrlMatch(val url: String, val start: Int, val end: Int)

// A deliberately small, dependency-free URL scanner: find each `http://` / `https://` run and consume up to
// the next whitespace, trimming a trailing sentence punctuation char so "…/callback." links the URL, not the
// period. Good enough for the wizard's instruction lines (which embed plain, space-delimited URLs); not a
// general RFC-3986 parser, by design (YAGNI).
private fun findUrls(text: String): List<UrlMatch> {
    val matches: MutableList<UrlMatch> = mutableListOf()
    var searchFrom = 0
    while (searchFrom < text.length) {
        val start: Int = indexOfScheme(text, searchFrom)
        if (start < 0) break
        var end: Int = start
        while (end < text.length && !text[end].isWhitespace()) end++
        // Don't swallow a trailing sentence terminator that abuts the URL.
        while (end > start && text[end - 1] in TRAILING_PUNCTUATION) end--
        matches.add(UrlMatch(url = text.substring(start, end), start = start, end = end))
        searchFrom = end
    }
    return matches
}

// The index of the next `http://` or `https://` at or after [from], or -1 when none remains.
private fun indexOfScheme(text: String, from: Int): Int {
    val https: Int = text.indexOf("https://", from)
    val http: Int = text.indexOf("http://", from)
    return when {
        https < 0 -> http
        http < 0 -> https
        else -> minOf(https, http)
    }
}

private val TRAILING_PUNCTUATION: Set<Char> = setOf('.', ',', ';', ':', ')', ']', '}', '!', '?', '"', '\'')
