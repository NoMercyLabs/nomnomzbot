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
 * Sleak's scarce-accent rule: full-chroma accent marks the ONE most important task on a surface. It was
 * being spent on every reorder arrow and every add-block glyph at every depth of the pipeline tree — 19
 * call sites — which is the same as spending it nowhere, because nothing stood out any more. Found by
 * looking at the rendered editor, not by reading the source.
 *
 * Structural, deliberately: it scans the source for the shape rather than naming the sites, because a
 * guard that checks a hand-written list is not a guard — four earlier guards in this codebase were
 * defeated exactly that way.
 */
class PipelineAccentScarcityGuardTest {

    // Reorder and add-block controls are secondary by definition: they rearrange or extend what is
    // already there. Scoped to GlyphButton — the tree's own inline controls. The page-level "create a
    // pipeline" TextButton also draws an AddGlyph and DOES keep accent: it is the one primary action of
    // that surface, which is exactly what accent is for. Banning it there would be the opposite error.
    private val secondaryGlyphs: List<String> = listOf("ArrowUpGlyph", "ArrowDownGlyph", "AddGlyph")

    @Test
    fun a_secondary_tree_control_never_spends_accent() {
        val source: File = File("src/commonMain/kotlin/bot/nomnomz/dashboard/feature/pipelines/ui/PipelinesScreen.kt")
        if (!source.exists()) fail("PipelinesScreen.kt not found at ${source.absolutePath}; update this guard's path")

        val lines: List<String> = source.readLines()
        val offenders: MutableList<String> = mutableListOf()

        lines.forEachIndexed { index, line ->
            val glyph: String = secondaryGlyphs.firstOrNull { line.contains(it) } ?: return@forEachIndexed
            val isTreeControl: Boolean =
                line.contains("GlyphButton(") ||
                    (index >= 1 && lines[index - 1].contains("GlyphButton("))
            if (!isTreeControl) return@forEachIndexed
            // Single-line call: the tint rides on the same line.
            if (line.contains("tint = tokens.primary")) {
                offenders += "${source.name}:${index + 1} — $glyph carries accent"
                return@forEachIndexed
            }
            // Multi-line call: scan forward to the closing paren of this GlyphButton/AppIcon call.
            var cursor: Int = index + 1
            while (cursor < lines.size && cursor <= index + 10) {
                val body: String = lines[cursor].trim()
                if (body.startsWith(")")) break
                if (body == "tint = tokens.primary,") {
                    offenders += "${source.name}:${cursor + 1} — $glyph carries accent"
                    break
                }
                cursor++
            }
        }

        if (offenders.isNotEmpty()) {
            fail(
                "Accent is the page's scarcest signal and must mark the single most important task, not " +
                    "every reorder arrow at every nesting depth. Offenders:/n" + offenders.joinToString("\n")
            )
        }
    }
}
