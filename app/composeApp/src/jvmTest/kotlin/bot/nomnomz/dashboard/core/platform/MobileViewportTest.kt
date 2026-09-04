// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.platform

import java.io.File
import kotlin.test.Test
import kotlin.test.fail

// The web build IS the mobile build today: a streamer opening the dashboard on their phone gets this page.
// Every failure guarded here is invisible on a desktop browser and breaks the app on a handset, which is the
// worst combination — nobody developing it ever sees the bug.
class MobileViewportTest {

    private val page: File = File("src/wasmJsMain/resources/index.html")

    private fun html(): String = page.readText()

    /**
     * The markup with HTML comments removed. The comments in this page NAME the attributes they warn against
     * ("no user-scalable=no"), so a guard reading the raw text fails on the very note explaining why it exists.
     */
    private fun markup(): String = html().replace(Regex("<!--.*?-->", RegexOption.DOT_MATCHES_ALL), "")

    @Test
    fun the_page_tracks_the_visible_viewport_and_not_the_larger_one() {
        // A percentage height resolves against the viewport WITH the URL bar collapsed, so the bottom strip of
        // the app renders behind the browser chrome and its controls cannot be tapped at all.
        if (!html().contains("height: 100dvh")) {
            fail(
                "index.html must set height: 100dvh (with a 100vh fallback before it) — with a percentage " +
                    "height the bottom of the dashboard hides behind mobile browser chrome"
            )
        }
    }

    @Test
    fun content_is_kept_out_of_the_notch_and_the_home_indicator() {
        val source: String = html()

        if (!source.contains("viewport-fit=cover")) {
            fail("the viewport meta needs viewport-fit=cover for the safe-area insets to resolve to anything")
        }
        if (!source.contains("env(safe-area-inset-bottom)")) {
            fail(
                "without safe-area padding, viewport-fit=cover paints the app UNDER the notch and the home " +
                    "indicator — the top and bottom rows become unreadable and untappable"
            )
        }
    }

    @Test
    fun pinch_zoom_is_not_taken_away_from_the_reader() {
        // Disabling zoom is the single most common accessibility regression on a mobile web app, and it is one
        // attribute away at all times.
        val source: String = markup()
        val banned: List<String> = listOf("user-scalable=no", "user-scalable = no", "maximum-scale=1")

        banned.firstOrNull { source.contains(it) }?.let {
            fail("index.html contains \"$it\" — that blocks pinch-zoom for anyone who needs to enlarge text")
        }
    }

    @Test
    fun dragging_inside_the_app_does_not_drag_the_page() {
        val source: String = html()

        if (!source.contains("overscroll-behavior: none")) {
            fail("without overscroll-behavior: none, scrolling a list rubber-bands the whole page on iOS")
        }
        if (!source.contains("touch-action: none")) {
            fail(
                "the canvas needs touch-action: none, or the browser claims drags for panning and Compose " +
                    "never sees them"
            )
        }
    }
}
