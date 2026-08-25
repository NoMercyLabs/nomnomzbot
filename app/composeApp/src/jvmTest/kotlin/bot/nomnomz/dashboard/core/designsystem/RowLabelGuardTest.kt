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

// Owner report: "some ui rows have no name but do have values and actions." A row a data field
// assigns straight into a title/label/name/headline slot is blank-capable the moment its source field
// can be null or "" — and every remaining raw assignment below is a candidate for the same defect.
//
// This is a STRUCTURAL scan (regex over the source tree), never a hand-maintained list of sites — four
// prior guards in this codebase were defeated by hand-maintained lists that silently went stale.
// The baseline is the burn-down: each remaining raw assignment, counted per file. Fixing a site means
// routing it through resolveRowLabel() and LOWERING that file's number — never raising one. A brand
// new raw assignment (a new blank-capable site skipping the shared mechanism) fails the build.
class RowLabelGuardTest {

    // Captured 2026-08-25 after wiring PickListsScreen.kt's rendered rows through resolveRowLabel().
    // PickListsScreen.kt's remaining 1 is PickListEditor.edit()'s form-seed (not a rendered row label).
    //
    // CommandsScreen.kt and RewardsScreen.kt are also fully wired (row text, semantics, action labels,
    // and the delete ConfirmDialog all resolve through resolveRowLabel()); each file's remaining 1 is
    // its own edit()-form-seed constructor (CommandEditor.edit() / RewardEditor.edit()) — the same
    // non-rendered-row exception as PickListsScreen.kt above, not an unaddressed site.
    private val rawAssignmentBaseline: Map<String, Int> =
        mapOf(
            "commands/ui/CommandsScreen.kt" to 1,
            "analytics/ui/AnalyticsScreen.kt" to 1,
            "codescripts/state/CodeScriptsController.kt" to 1,
            "economy/state/EconomyController.kt" to 2,
            "community/state/CommunityController.kt" to 1,
            "connect/ui/ConnectScreen.kt" to 1,
            "automation/ui/AutomationScreen.kt" to 1,
            "giveaways/ui/GiveawaysScreen.kt" to 2,
            "home/state/HomeController.kt" to 3,
            "widgets/ui/WidgetSettingsForms.kt" to 5,
            "pipelines/ui/PipelinesScreen.kt" to 1,
            "liveops/state/ScheduleController.kt" to 1,
            "moderation/state/ModerationController.kt" to 2,
            "picklists/ui/PickListsScreen.kt" to 1,
            "rewards/ui/RewardsScreen.kt" to 1,
            "tts/state/TtsController.kt" to 1,
            "pipelines/state/PipelinesController.kt" to 11,
            "roles/ui/RolesScreen.kt" to 1,
            "participant/ui/ParticipantShell.kt" to 2,
            "shell/ui/ShellScreen.kt" to 2,
        )

    // (title|label|name|headline) = <expr>.(name|title|label|displayName) — a row-label field assigned
    // straight from a data field's own name/title/label/displayName, bypassing resolveRowLabel().
    private val rawRowLabelAssignment: Regex =
        Regex("""\b(title|label|name|headline)\s*=\s*[A-Za-z0-9_.]+\.(name|title|label|displayName)\b""")

    @Test
    fun row_labels_route_through_resolveRowLabel_not_a_raw_data_field() {
        val root: File = featureRoot()
        val offenders: MutableList<String> = mutableListOf()
        root.walkTopDown().filter { it.isFile && it.extension == "kt" }.forEach { file ->
            val rel: String = file.relativeTo(root).path.replace('\\', '/')
            val text: String = file.readText()
            val count: Int = rawRowLabelAssignment.findAll(text).count()
            val allowed: Int = rawAssignmentBaseline[rel] ?: 0
            if (count > allowed) {
                offenders += "$rel: $count raw row-label assignment(s), baseline allows $allowed"
            }
        }
        if (offenders.isNotEmpty()) {
            fail(
                "New row label assigned straight from a data field — a row whose source name is null/blank " +
                    "must never render an empty label. Route it through " +
                    "bot.nomnomz.dashboard.core.designsystem.resolveRowLabel() instead. If you fixed a site, " +
                    "LOWER its number in rawAssignmentBaseline (never raise). Offenders:\n" +
                    offenders.joinToString("\n")
            )
        }
    }

    private fun featureRoot(): File {
        var dir: File? = File(System.getProperty("user.dir"))
        while (dir != null) {
            val candidate =
                File(dir, "app/composeApp/src/commonMain/kotlin/bot/nomnomz/dashboard/feature")
            if (candidate.isDirectory) return candidate
            dir = dir.parentFile
        }
        fail("Could not locate feature source from ${System.getProperty("user.dir")}")
    }
}
