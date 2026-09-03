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

import bot.nomnomz.dashboard.feature.moderation.ui.SpamDefenseCopy
import java.io.File
import kotlin.test.Test
import kotlin.test.fail

// The owner's headline requirement for spam defence is that the operator has full control over every
// weight AND that every weight is properly explained. That promise is kept by two tests on opposite
// sides of the API: the backend fails when a knob has no catalogue entry, and this one fails when a
// knob has no copy.
//
// The backend deliberately holds no prose — it sends resource KEYS, because the product ships in
// English and Dutch, and a server that returned sentences would show a Dutch streamer English. So the
// words live here, and here is where they have to be guarded.
class SpamDefenseCopyTest {

    // Every setting needs all three, and the third is the one that gets skipped: a number with a range
    // but no stated consequence is a number nobody can tune honestly.
    private val requiredSuffixes: List<String> = listOf("label", "explanation", "cost")

    @Test
    fun every_spam_setting_has_a_label_an_explanation_and_a_note_on_what_moving_it_costs() {
        val english: Map<String, String> = readStrings("values")
        val settingNames: Set<String> =
            english.keys
                .filter { it.startsWith("spam_setting_") && it.endsWith("_label") }
                .map { it.removePrefix("spam_setting_").removeSuffix("_label") }
                .toSet()

        if (settingNames.isEmpty()) fail("No spam settings copy found at all.")

        val missing: List<String> =
            settingNames.flatMap { name ->
                requiredSuffixes
                    .map { "spam_setting_${name}_$it" }
                    .filter { it !in english.keys }
            }

        if (missing.isNotEmpty()) {
            fail(
                "Every spam-defence weight must say what it does and what moving it costs. Missing:\n" +
                    missing.joinToString("\n")
            )
        }
    }

    @Test
    fun dutch_carries_every_spam_string_english_does() {
        // The failure this catches is the ordinary one: someone adds a knob, writes the English, and
        // ships. A Dutch operator would then be tuning a control labelled with a raw resource key.
        val english: Set<String> = readStrings("values").keys.filter { it.startsWith("spam_") }.toSet()
        val dutch: Set<String> = readStrings("values-nl").keys.filter { it.startsWith("spam_") }.toSet()

        val untranslated: Set<String> = english - dutch
        val orphaned: Set<String> = dutch - english

        if (untranslated.isNotEmpty() || orphaned.isNotEmpty()) {
            fail(
                buildString {
                    if (untranslated.isNotEmpty()) {
                        append("Missing Dutch copy:\n")
                        append(untranslated.sorted().joinToString("\n"))
                        append("\n")
                    }
                    if (orphaned.isNotEmpty()) {
                        append("Dutch strings with no English original:\n")
                        append(orphaned.sorted().joinToString("\n"))
                    }
                }
            )
        }
    }

    @Test
    fun every_invariant_explains_the_guarantee_it_gives() {
        // The five protections that have no switch. They are shown so an operator can see what they get
        // for free rather than having to ask, which only works if the copy exists.
        val english: Map<String, String> = readStrings("values")

        val missing: List<String> =
            listOf("sd0", "sd8", "sd9", "sd11", "sd12")
                .map { "spam_invariant_${it}_guarantee" }
                .filter { it !in english.keys }

        if (missing.isNotEmpty()) fail("Missing invariant copy: ${missing.joinToString()}")
    }

    @Test
    fun no_cost_note_is_too_short_to_be_useful() {
        // A one-word "Higher is stricter." tells an operator nothing about the trade-off they are
        // making. The threshold is deliberately low — it catches placeholders, not style.
        val english: Map<String, String> = readStrings("values")

        val tooShort: List<String> =
            english
                .filterKeys { it.startsWith("spam_setting_") && it.endsWith("_cost") }
                .filterValues { it.length < 40 }
                .map { "${it.key}: \"${it.value}\"" }

        if (tooShort.isNotEmpty()) {
            fail(
                "These cost notes are too short to help anyone decide:\n" + tooShort.joinToString("\n")
            )
        }
    }

    @Test
    fun no_label_leaks_an_engineering_term() {
        // The person reading this page is a streamer, not an engineer. "Skeleton", "SimHash" and
        // "cohort" are our vocabulary, not theirs.
        val jargon: List<String> = listOf("skeleton", "simhash", "cohort", "hamming", "normalize")
        val english: Map<String, String> = readStrings("values")

        val offenders: List<String> =
            english
                .filterKeys { it.startsWith("spam_setting_") && it.endsWith("_label") }
                .filterValues { value -> jargon.any { value.contains(it, ignoreCase = true) } }
                .map { "${it.key}: \"${it.value}\"" }

        if (offenders.isNotEmpty()) {
            fail("Labels must be in the operator's words:\n" + offenders.joinToString("\n"))
        }
    }

    @Test
    fun the_key_to_resource_map_matches_the_string_resources_exactly() {
        // Compose Resources resolves strings statically — there is no lookup-by-name — so a key the
        // backend sends has to be present in an explicit map to render at all. Without this check the
        // failure is silent and ugly: the page shows a raw key to somebody in the middle of a raid.
        val inResources: Set<String> =
            readStrings("values").keys.filter { it.startsWith("spam_") }.toSet()
        val inMap: Set<String> = SpamDefenseCopy.byKey.keys

        val unmapped: Set<String> = inResources - inMap
        val dangling: Set<String> = inMap - inResources

        if (unmapped.isNotEmpty() || dangling.isNotEmpty()) {
            fail(
                buildString {
                    if (unmapped.isNotEmpty()) {
                        append("Strings with no entry in SpamDefenseCopy.byKey:\n")
                        append(unmapped.sorted().joinToString("\n"))
                        append("\n")
                    }
                    if (dangling.isNotEmpty()) {
                        append("Map entries with no string resource:\n")
                        append(dangling.sorted().joinToString("\n"))
                    }
                }
            )
        }
    }

    private fun readStrings(dir: String): Map<String, String> {
        val file = File(resourcesRoot(), "$dir/strings.xml")
        if (!file.isFile) fail("Missing $dir/strings.xml")

        return Regex("<string name=\"([^\"]+)\">(.*?)</string>", RegexOption.DOT_MATCHES_ALL)
            .findAll(file.readText())
            .associate { it.groupValues[1] to it.groupValues[2] }
    }

    // Same working-dir walk-up as StringResourceEscapingTest: jvmTest may run from the module dir or
    // the repo root.
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
