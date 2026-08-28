// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.eventresponses

import java.io.File
import kotlin.test.Test
import kotlin.test.assertTrue
import kotlin.test.fail

// S-EVENTRESPONSE-NO-CREATE settled the model: an event response is a fixed, seeded catalogue entry —
// never user-created or user-deletable. The dashboard's control for it must say "reset", never "delete",
// in every language file, because the backend operation it drives (POST .../reset) never removes the row.
// This is a data-file lint on strings.xml (English + Dutch), mirroring StringResourceEscapingTest's
// walk-every-language-file approach, so a future contributor can't silently reintroduce "Delete" wording
// for this resource in one locale without the other.
class EventResponseResetWordingTest {

    // Matches an orphaned "delete"-named key (event_responses_delete_action, event_responses_dialog_delete, …) —
    // these named the old destructive action and must not exist any more, in either language file.
    private val deleteKeyPattern = Regex("""name="(event_responses[a-zA-Z0-9_]*delete[a-zA-Z0-9_]*)"""")

    // The actual on-screen action labels (button text / dialog title) for the reset control — these must name
    // the action as "reset", never "delete", since the operation never removes the row. Deliberately narrower
    // than a whole-file text scan: prose elsewhere (e.g. the confirm message) legitimately says "never deleted"
    // as a NEGATION, which a blind substring ban on "delete"/"verwijder" would misfire on.
    private val actionLabelKeys =
        setOf("event_responses_dialog_reset", "event_responses_reset_confirm_title")
    private val deleteWordPattern = Regex("delete|verwijder", RegexOption.IGNORE_CASE)

    @Test
    fun no_orphaned_delete_named_key_exists_for_event_responses() {
        val offenders: MutableList<String> = mutableListOf()

        resourcesRoot()
            .walkTopDown()
            .filter { it.isFile && it.name == "strings.xml" }
            .forEach { file ->
                file.readLines().forEachIndexed { index, line ->
                    if (deleteKeyPattern.containsMatchIn(line)) {
                        offenders.add(
                            "${file.parentFile.name}/strings.xml:${index + 1}: ${line.trim()}"
                        )
                    }
                }
            }

        if (offenders.isNotEmpty()) {
            fail(
                "A \"delete\"-named event-response string key still exists — event responses are a fixed " +
                    "seeded catalogue (S-EVENTRESPONSE-NO-CREATE): the row is never removed, only reset to its " +
                    "default in place, so no key should be named after a delete action any more. Offenders:\n" +
                    offenders.joinToString("\n")
            )
        }
    }

    @Test
    fun the_reset_action_labels_say_reset_not_delete_in_every_language() {
        val offenders: MutableList<String> = mutableListOf()

        resourcesRoot()
            .walkTopDown()
            .filter { it.isFile && it.name == "strings.xml" }
            .forEach { file ->
                file.readLines().forEachIndexed { index, line ->
                    val nameMatch = Regex("""name="([a-zA-Z0-9_]+)"""").find(line) ?: return@forEachIndexed
                    if (nameMatch.groupValues[1] in actionLabelKeys && deleteWordPattern.containsMatchIn(line)) {
                        offenders.add(
                            "${file.parentFile.name}/strings.xml:${index + 1}: ${line.trim()}"
                        )
                    }
                }
            }

        if (offenders.isNotEmpty()) {
            fail(
                "An event-response reset action label says \"delete\"/\"verwijder\" instead of \"reset\" — " +
                    "the operation never removes the row. Offenders:\n" + offenders.joinToString("\n")
            )
        }
    }

    @Test
    fun the_reset_dialog_string_exists_in_every_language() {
        val languageDirs: List<File> =
            resourcesRoot().listFiles { f -> f.isDirectory && f.name.startsWith("values") }?.toList()
                ?: emptyList()
        assertTrue(languageDirs.isNotEmpty(), "expected at least one values*/strings.xml directory")

        languageDirs.forEach { dir ->
            val strings = File(dir, "strings.xml")
            assertTrue(
                strings.readText().contains("event_responses_dialog_reset"),
                "${dir.name}/strings.xml is missing event_responses_dialog_reset",
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
        throw IllegalStateException("Could not locate app/composeApp/src/commonMain/composeResources")
    }
}
