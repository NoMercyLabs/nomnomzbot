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
 * A render test that asserts user-visible text MUST pin the locale.
 *
 * Three webhook render tests shipped wrapped only in `NomNomzTheme`, so compose-resources resolved
 * their strings through the MACHINE locale: the assertion looked for contentDescription "Edit" and the
 * screen rendered "Bewerken". They were red on a Dutch machine and would have been green on an English
 * CI runner — the worst shape a test can have, because it accuses whichever developer is not on the
 * build server's locale. Fixed in `0545cb9b`; this stops the class coming back.
 *
 * The scan is structural, and deliberately narrow to stay honest: a file is only an offender if it
 * mounts Compose (`runComposeUiTest`), never mentions `AppEnvironment`, AND asserts a literal that is a
 * real value in `values/strings.xml`. A test asserting its own seeded data ("Ko-fi receipts", a
 * timestamp) needs no locale and is not flagged.
 */
class LocalePinnedRenderTestGuardTest {

    @Test
    fun a_render_test_asserting_localised_text_pins_the_locale() {
        val srcRoot = File("src")
        if (!srcRoot.isDirectory) fail("expected to run from composeApp; src/ not found at ${srcRoot.absolutePath}")

        val englishValues: Set<String> = readStringValues(File("src/commonMain/composeResources/values/strings.xml"))
        if (englishValues.isEmpty()) fail("no strings parsed from values/strings.xml — the guard would pass vacuously")

        val assertion = Regex("""onNodeWithText\("([^"]+)"|onAllNodesWithText\("([^"]+)"|onNodeWithContentDescription\("([^"]+)"""")
        val offenders: MutableList<String> = mutableListOf()

        srcRoot.walkTopDown()
            .filter { it.isFile && it.extension == "kt" && (it.path.contains("jvmTest") || it.path.contains("commonTest")) }
            .forEach { file ->
                val text: String = file.readText()
                if (!text.contains("runComposeUiTest") || text.contains("AppEnvironment")) return@forEach
                val localised: List<String> =
                    assertion.findAll(text)
                        .mapNotNull { m -> m.groupValues.drop(1).firstOrNull { it.isNotEmpty() } }
                        .filter { it in englishValues }
                        .distinct()
                        .toList()
                if (localised.isNotEmpty()) {
                    offenders += "${file.name} asserts ${localised.joinToString()} without AppEnvironment(tag = \"en\")"
                }
            }

        if (offenders.isNotEmpty()) {
            fail(
                "These render tests assert text that comes from strings.xml but never pin the locale, so they " +
                    "pass or fail depending on the developer's machine language:/n" + offenders.joinToString("\n")
            )
        }
    }

    /** Values only — a key never appears on screen, so matching keys would flag the wrong thing. */
    private fun readStringValues(file: File): Set<String> {
        if (!file.isFile) return emptySet()
        return Regex("""<string name="[^"]+">([^<]*)</string>""")
            .findAll(file.readText())
            .map { it.groupValues[1].trim() }
            .filter { it.length >= 3 }
            .toSet()
    }
}
