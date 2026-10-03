// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.widgets.ui

import java.io.File
import kotlin.test.Test
import kotlin.test.fail

/**
 * Sleak's scarce-accent rule on the Overlays screen: full-chroma accent marks the ONE most important task of
 * an overlay row. The row spent `tokens.primary` on Settings, Edit code, Test, Versions, Rename, Clone, the
 * capture-window action and two status lines; a gallery card spent the Default (filled accent) badge on the
 * first-party trust tier. Edit code stays the row's single accent (authoring is what an operator returns to
 * an overlay for); everything else is quiet.
 *
 * A source guard, deliberately: the accent is a colour argument inside the composable, not reachable through
 * semantics, and `TrustTierBadge` is private. It counts accent uses structurally per composable function
 * rather than naming call sites, so a new control cannot quietly add a second accent.
 */
class WidgetsScreenAccentScarcityGuardTest {
    private val source: File =
        File("src/commonMain/kotlin/bot/nomnomz/dashboard/feature/widgets/ui/WidgetsScreen.kt")

    private fun lines(): List<String> {
        if (!source.exists()) fail("WidgetsScreen.kt not found at ${source.absolutePath}; update this guard's path")
        return source.readLines()
    }

    // The code lines (1-based number, text) of one top-level function, comments excluded.
    private fun functionCode(all: List<String>, signature: String): List<Pair<Int, String>> {
        val start: Int = all.indexOfFirst { it.startsWith(signature) }
        if (start < 0) fail("function '$signature' not found in ${source.name}; update this guard")
        val end: Int = (start + 1 until all.size).firstOrNull { all[it] == "}" } ?: all.lastIndex
        return (start..end).map { (it + 1) to all[it] }.filterNot { (_, text) -> text.trim().startsWith("//") }
    }

    @Test
    fun an_overlay_row_spends_accent_on_at_most_one_control() {
        val all: List<String> = lines()
        val accentLines: List<Int> =
            listOf("private fun WidgetRow(", "private fun WidgetRowInfo(", "private fun WidgetRowActions(")
                .flatMap { signature -> functionCode(all, signature) }
                .filter { (_, text) -> text.contains("tokens.primary") }
                .map { (number, _) -> number }
        if (accentLines.size > 1) {
            fail(
                "An overlay row must spend full accent on one control at most (Sleak: scarce accent); " +
                    "${source.name} uses tokens.primary at lines $accentLines"
            )
        }
    }

    @Test
    fun a_version_row_rollback_is_not_an_accent_button() {
        val offenders: List<Int> =
            functionCode(lines(), "private fun WidgetVersionRow(")
                .filter { (_, text) -> text.contains("if (enabled) tokens.primary") }
                .map { (number, _) -> number }
        if (offenders.isNotEmpty()) {
            fail("Every version row repeats Rollback; it must not take the accent. Offending lines $offenders")
        }
    }

    @Test
    fun the_first_party_trust_badge_is_not_the_filled_accent_variant() {
        val badge: String =
            functionCode(lines(), "private fun TrustTierBadge(")
                .joinToString("\n") { (_, text) -> text }
        if (Regex("\"first_party\"\\s*->\\s*BadgeVariant\\.Default").containsMatchIn(badge)) {
            fail(
                "Every gallery item is first-party, so a Default badge paints an accent pill on every card; " +
                    "a status takes Secondary or Outline."
            )
        }
    }
}
