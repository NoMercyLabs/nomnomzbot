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

// Two entry fields sharing a Row is the layout that actually breaks at Compact. A label with a small control
// beside it is fine at any width — that is most of what a weighted-child count finds, and counting is why
// this was first waved off — but two AppTextFields splitting a dialog leave each around 150 dp, too narrow to
// read the text or show a field's error underneath.
//
// Five of them existed across the app. They are all on the FieldPair primitive now, which stacks at Compact,
// and this keeps the sixth from being written by hand.
class SideBySideFieldGuardTest {

    private val entryField: Regex =
        Regex("""\b(AppTextField|OutlinedTextField|TextField|NumberField|SearchField|PickerField)\(""")

    private fun featureSources(): List<File> =
        File("src/commonMain/kotlin/bot/nomnomz/dashboard")
            .walkTopDown()
            .filter { it.isFile && it.extension == "kt" }
            .toList()

    @Test
    fun no_screen_puts_two_entry_fields_side_by_side_without_a_compact_layout() {
        val offenders: MutableList<String> = mutableListOf()

        featureSources().forEach { file ->
            val lines: List<String> = file.readText().split("\n")
            lines.forEachIndexed { index, line ->
                if (line.trim() != "Row(") return@forEachIndexed
                val indent: Int = line.length - line.trimStart().length

                // Collect this Row's block: up to its closing brace at the Row's own indent.
                val body: StringBuilder = StringBuilder()
                for (j in index + 1 until minOf(index + 90, lines.size)) {
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
                val weighted: Int = block.split("Modifier.weight(").size - 1
                val hasCompactBranch: Boolean =
                    block.contains("windowSize") || block.contains("WindowSizeClass")

                if (fields >= 2 && weighted >= 2 && !hasCompactBranch) {
                    offenders += "${file.name}:${index + 1}"
                }
            }
        }

        if (offenders.isNotEmpty()) {
            fail(
                "these Rows put two or more entry fields side by side with no Compact layout: $offenders — " +
                    "use the FieldPair primitive (core/designsystem/component/FieldPair.kt), which stacks " +
                    "them on a narrow screen"
            )
        }
    }

    @Test
    fun the_primitive_actually_branches_on_the_size_class() {
        // A FieldPair that always laid out as a Row would silence the guard above while changing nothing —
        // the whole app would look converted and still squeeze on every phone.
        val primitive: File =
            File("src/commonMain/kotlin/bot/nomnomz/dashboard/core/designsystem/component/FieldPair.kt")
        val source: String = primitive.readText()

        if (!source.contains("windowSize.isCompact")) {
            fail("FieldPair no longer branches on the size class — every call site silently stops stacking")
        }
        if (!source.contains("Column(")) {
            fail("FieldPair has no Column branch, so it cannot be stacking anything on a narrow screen")
        }
    }
}
