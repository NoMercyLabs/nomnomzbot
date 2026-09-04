// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.designsystem

import java.io.File
import kotlin.test.Test
import kotlin.test.fail

// S-UX-6b: a Row of several `Modifier.weight(...)` cells each right-aligned (`TextAlign.End`) is a genuine
// data table — a label+control pair (the common false positive a raw weight() count turns up) never uses
// `TextAlign.End` on more than one cell, because there is only one value, not a row of numeric columns. Two
// real tables shipped this way (AnalyticsScreen's daily-trends row and viewer-list row, five and four columns)
// and both squeezed unreadably at Compact (phone) width until S-UX-6b gave them a stacked "label value" card
// layout gated on `windowSize.isCompact`.
//
// This enumerates the real source tree for that TextAlign.End-cluster shape (not a hand-typed list of the
// files fixed today), so a new multi-column table added tomorrow without a Compact path is caught the same
// way the two existing ones were.
class CompactMultiColumnRowGuardTest {

    private fun featureSources(): List<File> =
        File("src/commonMain/kotlin/bot/nomnomz/dashboard")
            .walkTopDown()
            .filter { it.isFile && it.extension == "kt" }
            .toList()

    @Test
    fun no_multi_column_data_row_ships_without_a_compact_layout_in_its_file() {
        val offenders: MutableList<String> = mutableListOf()

        featureSources().forEach { file ->
            val lines: List<String> = file.readText().split("\n")
            lines.forEachIndexed { index, line ->
                if (line.trim() != "Row(") return@forEachIndexed
                val indent: Int = line.length - line.trimStart().length

                val body: StringBuilder = StringBuilder()
                for (j in index + 1 until minOf(index + 40, lines.size)) {
                    val current: String = lines[j]
                    val closes: Boolean =
                        current.isNotBlank() &&
                            (current.length - current.trimStart().length) <= indent &&
                            current.trimStart().startsWith("}")
                    if (closes) break
                    body.append(current).append('\n')
                }

                val block: String = body.toString()
                val rightAlignedWeightedCells: Int =
                    Regex("""Modifier\.weight\([^)]*\)[^)]*textAlign\s*=\s*TextAlign\.End""").findAll(block).count() +
                        Regex("""textAlign\s*=\s*TextAlign\.End[^)]*\)[^)]*\.weight\(""").findAll(block).count()

                if (rightAlignedWeightedCells >= 2 && !file.readText().contains("windowSize")) {
                    offenders += "${file.name}:${index + 1}"
                }
            }
        }

        if (offenders.isNotEmpty()) {
            fail(
                "these Rows look like multi-column data tables (2+ right-aligned weighted cells) with no " +
                    "`windowSize` reference anywhere in the file, so nothing narrows them at Compact width: " +
                    "$offenders"
            )
        }
    }

    @Test
    fun analytics_daily_and_viewer_rows_actually_route_through_a_compact_layout() {
        // Direct pin on the two tables this slice fixed: confirms the Compact branch isn't a dead `if` that
        // never routes anywhere (e.g. a stray `windowSize` import with the branch itself removed).
        val analytics: File = File("src/commonMain/kotlin/bot/nomnomz/dashboard/feature/analytics/ui/AnalyticsScreen.kt")
        val source: String = analytics.readText()

        // Word-boundary matches, not bare `contains`: a plain substring check also passes for a
        // renamed symbol that merely KEEPS the token as a suffix (`MutatedCompactDailyRow(`), so the
        // guard reads green through a rename it should catch. Proven by mutating this file.
        fun callsComposable(name: String): Boolean =
            Regex("""\b$name\(""").containsMatchIn(source)

        if (!source.contains("windowSize.isCompact")) {
            fail("AnalyticsScreen no longer branches on the size class for its daily-trends/viewer tables")
        }
        if (!callsComposable("CompactDailyRow")) {
            fail("AnalyticsScreen's Compact branch no longer routes to CompactDailyRow — the daily table would squeeze again")
        }
        if (!callsComposable("CompactViewerRow")) {
            fail("AnalyticsScreen's Compact branch no longer routes to CompactViewerRow — the viewer table would squeeze again")
        }
    }

    @Test
    fun settings_event_journal_actions_wrap_at_compact() {
        // The Export/Import/Rebuild trio + status line (SettingsScreen.kt, EventJournalSection) has no weight
        // or wrap handling on its own and crowds at narrow widths — folded into this slice per its brief.
        val settings: File = File("src/commonMain/kotlin/bot/nomnomz/dashboard/feature/settings/ui/SettingsScreen.kt")
        val source: String = settings.readText()

        val sectionStart: Int = source.indexOf("private fun EventJournalSection(")
        if (sectionStart < 0) fail("EventJournalSection no longer exists in SettingsScreen.kt")
        val sectionEnd: Int = source.indexOf("\nprivate fun JournalActionButtons(", sectionStart)
        val section: String = source.substring(sectionStart, if (sectionEnd > 0) sectionEnd else source.length)

        if (!section.contains("windowSize.isCompact")) {
            fail("EventJournalSection no longer branches on the size class — the button trio would crowd again on a phone")
        }
        if (!section.contains("FlowRow(")) {
            fail("EventJournalSection's Compact branch no longer wraps the button trio in a FlowRow")
        }
    }

    // A second crowding shape, distinct from the multi-column table above: an entry field sharing a fixed Row
    // with three or more action buttons (Settings' journal export/import/rebuild trio, Admin's feature-flag
    // override enable/disable/clear trio). The field shrinks to a sliver once the buttons claim their space.
    // Enumerated the same way — a Row block with 1+ entry field and 3+ Button/TextButton siblings, with no
    // `windowSize` anywhere in the file, is an offender.
    private val entryField: Regex =
        Regex("""\b(AppTextField|OutlinedTextField|TextField|NumberField|SearchField|PickerField)\(""")
    private val actionButton: Regex = Regex("""\b(Button|TextButton)\(""")

    @Test
    fun no_field_plus_three_or_more_actions_ships_without_a_compact_layout_in_its_file() {
        val offenders: MutableList<String> = mutableListOf()

        featureSources().forEach { file ->
            val lines: List<String> = file.readText().split("\n")
            lines.forEachIndexed { index, line ->
                if (line.trim() != "Row(") return@forEachIndexed
                val indent: Int = line.length - line.trimStart().length

                val body: StringBuilder = StringBuilder()
                for (j in index + 1 until minOf(index + 40, lines.size)) {
                    val current: String = lines[j]
                    val closes: Boolean =
                        current.isNotBlank() &&
                            (current.length - current.trimStart().length) <= indent &&
                            current.trimStart().startsWith("}")
                    if (closes) break
                    body.append(current).append('\n')
                }

                val block: String = body.toString()
                val fields: Int = entryField.findAll(block).count()
                val buttons: Int = actionButton.findAll(block).count()

                if (fields >= 1 && buttons >= 3 && !file.readText().contains("windowSize")) {
                    offenders += "${file.name}:${index + 1}"
                }
            }
        }

        if (offenders.isNotEmpty()) {
            fail(
                "these Rows put an entry field beside 3+ action buttons with no `windowSize` reference " +
                    "anywhere in the file, so nothing wraps them at Compact width: $offenders"
            )
        }
    }

    @Test
    fun admin_feature_flag_override_actions_wrap_at_compact() {
        // Direct pin on AdminScreen's fix: FeatureFlagOverrideRow's id field + Enable/Disable/Clear trio.
        val admin: File = File("src/commonMain/kotlin/bot/nomnomz/dashboard/feature/admin/ui/AdminScreen.kt")
        val source: String = admin.readText()

        val sectionStart: Int = source.indexOf("private fun FeatureFlagOverrideRow(")
        if (sectionStart < 0) fail("FeatureFlagOverrideRow no longer exists in AdminScreen.kt")
        val sectionEnd: Int = source.indexOf("\nprivate fun FeatureFlagOverrideActions(", sectionStart)
        val section: String = source.substring(sectionStart, if (sectionEnd > 0) sectionEnd else source.length)

        if (!section.contains("windowSize.isCompact")) {
            fail("FeatureFlagOverrideRow no longer branches on the size class — the action trio would crowd the field again")
        }
        if (!section.contains("FlowRow(")) {
            fail("FeatureFlagOverrideRow's Compact branch no longer wraps the action trio in a FlowRow")
        }
    }
}
