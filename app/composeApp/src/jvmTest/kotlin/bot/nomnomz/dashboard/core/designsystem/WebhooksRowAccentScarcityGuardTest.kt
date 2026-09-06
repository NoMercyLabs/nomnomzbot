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

/**
 * Sleak's scarce-accent rule: full-chroma accent marks the ONE most important task on a surface.
 * `OutboundRow` in the webhooks screen tinted Edit, Test AND Reenable with `tokens.primary` in the same
 * action strip — three equal-weight accents in one row mark nothing, the identical shape found and fixed
 * in the pipeline tree the same day (`PipelineAccentScarcityGuardTest`, 19 call sites). Edit stays the row's
 * one accent (it is what an operator returns to a webhook row for); Test and Reenable read as ordinary
 * ghost buttons — still reachable, still enabled, just not fighting Edit for the eye.
 *
 * Structural, deliberately: it scans each row's `actions` composable block for how many controls inside it
 * carry `tint = tokens.primary`, rather than naming the offending controls by id. A hand-written list of
 * call sites is how four earlier guards in this codebase went stale.
 */
class WebhooksRowAccentScarcityGuardTest {

    @Test
    fun an_action_group_never_spends_accent_on_more_than_one_control() {
        val source: File =
            File("src/commonMain/kotlin/bot/nomnomz/dashboard/feature/webhooks/ui/WebhooksScreen.kt")
        if (!source.exists()) fail("WebhooksScreen.kt not found at ${source.absolutePath}; update this guard's path")

        val lines: List<String> = source.readLines()
        val offenders: MutableList<String> = mutableListOf()

        var index: Int = 0
        while (index < lines.size) {
            val line: String = lines[index]
            if (!line.contains("val actions: @Composable () -> Unit = {")) {
                index++
                continue
            }

            // Walk the block by brace depth until it closes, counting every accent-tinted control inside.
            val blockStartLine: Int = index + 1
            var depth: Int = 1
            var cursor: Int = index + 1
            val accentLines: MutableList<Int> = mutableListOf()
            while (cursor < lines.size && depth > 0) {
                val body: String = lines[cursor]
                depth += body.count { it == '{' } - body.count { it == '}' }
                if (body.trim() == "tint = tokens.primary,") accentLines += cursor + 1
                cursor++
            }

            if (accentLines.size > 1) {
                offenders +=
                    "action group starting at ${source.name}:$blockStartLine spends accent at lines $accentLines"
            }
            index = cursor
        }

        if (offenders.isNotEmpty()) {
            fail(
                "Accent is the row's scarcest signal and must mark the single most important task in its " +
                    "action group, not every control in the strip. Offenders:\n" + offenders.joinToString("\n")
            )
        }
    }
}
