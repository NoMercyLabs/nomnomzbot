// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.resources

import java.io.File
import kotlin.test.Test
import kotlin.test.fail

// Compose Multiplatform string resources do NOT process Android-style backslash escapes: a `\'` or `\"` inside a
// <string> renders the backslash LITERALLY on screen. This was proven against the live build — the heading
// "Top-commando\'s" showed the stray backslash, while the raw-apostrophe "Commando's gebruikt" rendered cleanly.
// Apostrophes and double quotes need no escaping in XML text content, so the only correct form is a bare ' and ".
//
// This guard fails if a render-breaking escape is reintroduced in ANY language file — the exact regression that
// slips in when someone carries an Android habit over, or a translator "helpfully" escapes an apostrophe. It is a
// data-file lint (not a render test): it asserts the defect *cannot* recur, which is what the escapes silently did.
class StringResourceEscapingTest {

    @Test
    fun no_string_resource_contains_a_render_breaking_backslash_escape() {
        val offenders: MutableList<String> =
            resourcesRoot()
                .walkTopDown()
                .filter { it.isFile && it.name == "strings.xml" }
                .flatMap { file ->
                    file.readLines().mapIndexedNotNull { index, line ->
                        if (line.contains("\\'") || line.contains("\\\"")) {
                            "${file.parentFile.name}/strings.xml:${index + 1}: ${line.trim()}"
                        } else {
                            null
                        }
                    }
                }
                .toMutableList()

        if (offenders.isNotEmpty()) {
            fail(
                "String resources must not contain \\' or \\\" — Compose renders the backslash literally on " +
                    "screen. Use a bare ' or \" (no escaping is needed in XML text content). Offenders:\n" +
                    offenders.joinToString("\n")
            )
        }
    }

    // Same working-dir walk-up as ApiContractTest.specFile(): jvmTest may run from the module dir or the repo root.
    private fun resourcesRoot(): File {
        var dir: File? = File(System.getProperty("user.dir"))
        while (dir != null) {
            val candidate = File(dir, "app/composeApp/src/commonMain/composeResources")
            if (candidate.isDirectory) return candidate
            dir = dir.parentFile
        }
        fail("Could not locate composeResources from ${System.getProperty("user.dir")}")
    }
}
