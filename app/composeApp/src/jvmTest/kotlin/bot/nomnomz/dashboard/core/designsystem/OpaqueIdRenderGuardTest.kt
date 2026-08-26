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
import kotlin.test.assertTrue
import kotlin.test.fail

// S-RICH-PICKERS-guard: a bare ULID, Discord snowflake, or raw backend id must never be the visible
// text of a user-facing label — a row/option a human cannot read the name of is exactly the "raw text
// box where a picker belongs" defect this project keeps re-introducing. This guard is STRUCTURAL: it
// regex-scans the real feature source tree for a UI-facing parameter (text/title/label/headline/
// contentDescription) assigned straight from an id-shaped field (`.id`, `...Id`, `.snowflake`,
// `.guildId`, `.ulid`) and fails loudly on anything it cannot classify as one of the two known-safe
// shapes:
//   1. Routed through `resolveRowLabel(...)` — the shared fallback mechanism that hashes the id into a
//      short discriminator code instead of ever rendering it raw.
//   2. A last-resort `.ifBlank { it.value }` / `.ifBlank { option.value }` style fallback inside the
//      rich `ResourcePickerField`/`ResourcePickerRow` machinery itself, where the id is the STORED
//      selection value (not a rendered name) and only ever shown if the backend sent a blank label —
//      a defensive fallback, not the primary rendering path.
// Every other match is a genuine violation: a raw id rendered as if it were a name.
class OpaqueIdRenderGuardTest {

    // UI-facing parameters that place text directly on screen.
    private val uiFacingParam: String = "(text|title|label|headline|contentDescription)"

    // The real matcher: LHS is a UI-facing param, RHS is a bare id-shaped field access (no function
    // wrapping it — a wrapped call like `resolveRowLabel(x.id, ...)` never matches this shape because
    // the RHS here must be the WHOLE assignment, i.e. immediately followed by a comma/paren/newline).
    private val rawIdAssignment: Regex =
        Regex(
            """\b$uiFacingParam\s*=\s*[A-Za-z0-9_]+(\.[A-Za-z0-9_]+)*\.(id|Id|snowflake|guildId|channelId|roleId|userId|ulid)\b\s*[,)\n]""",
        )

    // Known-safe last-resort fallback inside the rich picker itself: `label.ifBlank { ... value }` /
    // `.ifBlank { option.value }` — the id is a stored selection key shown only when the backend's
    // own label is blank, never the primary rendering path. Tracked explicitly, never silently grown.
    private val ifBlankValueFallbackBaseline: Map<String, Int> =
        mapOf(
            "core/designsystem/component/ResourcePickerField.kt" to 2,
        )

    @Test
    fun no_ui_facing_text_renders_a_raw_id_field_directly() {
        val root: File = featureAndDesignSystemRoot()
        val offenders: MutableList<String> = mutableListOf()
        root.walkTopDown().filter { it.isFile && it.extension == "kt" }.forEach { file ->
            val rel: String = file.relativeTo(root).path.replace('\\', '/')
            val text: String = file.readText()

            val rawMatches: List<MatchResult> = rawIdAssignment.findAll(text).toList()
            if (rawMatches.isEmpty()) return@forEach

            // Every raw match must be explainable as the known-safe ifBlank{...value} fallback shape
            // (checked by scanning the surrounding ~40 chars for ".ifBlank" immediately before the
            // matched id-field access) — anything else is unclassified and FAILS LOUD rather than being
            // silently skipped.
            var unexplainedCount = 0
            rawMatches.forEach { match ->
                val windowStart: Int = maxOf(0, match.range.first - 60)
                val window: String = text.substring(windowStart, match.range.first)
                val isIfBlankValueFallback: Boolean = window.contains(".ifBlank")
                if (!isIfBlankValueFallback) unexplainedCount++
            }

            if (unexplainedCount > 0) {
                offenders += "$rel: $unexplainedCount raw id-as-label render(s)"
            }

            val ifBlankCount: Int = rawMatches.size - unexplainedCount
            val ifBlankAllowed: Int = ifBlankValueFallbackBaseline[rel] ?: 0
            if (ifBlankCount > ifBlankAllowed) {
                offenders += "$rel: $ifBlankCount ifBlank-value fallback(s), baseline allows $ifBlankAllowed (new one must be added to the baseline explicitly)"
            }
        }
        if (offenders.isNotEmpty()) {
            fail(
                "A UI-facing label rendered a raw id field directly — a user must never see a bare " +
                    "ULID/snowflake/backend id as a name. Route it through " +
                    "bot.nomnomz.dashboard.core.designsystem.resolveRowLabel() or the rich picker option " +
                    "shape instead.\n" + offenders.joinToString("\n"),
            )
        }
    }

    // Proves the guard can actually fail: a deliberately-constructed source snippet containing a raw
    // id-as-label render must be caught by the same regex the real scan uses — a guard never seen red
    // is not a guard.
    @Test
    fun guard_regex_catches_a_deliberate_raw_id_render() {
        val deliberateViolation =
            """
            Text(
                text = command.id,
                style = typography.sm,
            )
            """.trimIndent()
        val matches: List<MatchResult> = rawIdAssignment.findAll(deliberateViolation).toList()
        assertTrue(matches.isNotEmpty(), "guard regex failed to catch a deliberate raw `text = command.id` render")

        val safeEquivalent =
            """
            Text(
                text = resolveRowLabel(command.name, typeLabel = "Command", discriminatorSource = command.id),
                style = typography.sm,
            )
            """.trimIndent()
        val safeMatches: List<MatchResult> = rawIdAssignment.findAll(safeEquivalent).toList()
        assertTrue(safeMatches.isEmpty(), "guard regex false-positived on a resolveRowLabel()-wrapped id")
    }

    private fun featureAndDesignSystemRoot(): File {
        var dir: File? = File(System.getProperty("user.dir"))
        while (dir != null) {
            val candidate = File(dir, "app/composeApp/src/commonMain/kotlin/bot/nomnomz/dashboard")
            if (candidate.isDirectory) return candidate
            dir = dir.parentFile
        }
        fail("Could not locate dashboard source from ${System.getProperty("user.dir")}")
    }
}
