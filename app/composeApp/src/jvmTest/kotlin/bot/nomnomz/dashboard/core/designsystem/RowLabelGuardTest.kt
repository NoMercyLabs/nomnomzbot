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
// assigns straight into a rendered label slot is blank-capable the moment its source field can be
// null or "" — and every remaining raw assignment below is a candidate for the same defect.
//
// This is a STRUCTURAL scan (regex over the source tree), never a hand-maintained list of sites — four
// prior guards in this codebase were defeated by hand-maintained lists that silently went stale.
//
// GUARD FIX (2026-08-25): the original single regex counted the WRONG population and its number did
// not move when two files were fully fixed (see history below). Root cause, found by re-deriving the
// regex from the actual fixed diffs instead of guessing:
//   (a) It never matched `text = X.name` at all — the #1 real rendering site, Compose `Text`'s own
//       content parameter — because its LHS whitelist was only title/label/name/headline. Every row's
//       actual on-screen `Text(text = command.name, ...)` was invisible to the guard from day one.
//   (b) It DID match `name = command.name` inside `CommandEditor.edit(command)` / `RewardEditor.edit()`
//       / `PickListEditor.edit()` etc. — companion-object factories that seed an EDITABLE dialog field's
//       default value from the summary. That is a legitimate self-passthrough into a same-named
//       constructor parameter, not a read-only rendered label a user cannot act around; it was never
//       the defect the owner reported, and no destructive action is guarded by it.
// Both defects lived in the same regex, on the SAME matched lines, so fixing (a) without also fixing
// (b) would keep counting non-defects as if they were the still-broken population.
//
// FIX CHOSEN: split into two disjoint, structurally-detected buckets — rendered vs. form-seed — rather
// than hand-classifying sites file by file (that IS the hand-maintained list this guard exists to
// avoid). The two buckets are told apart mechanically: `rawFormSeedAssignment` matches only the
// self-passthrough shape where the LHS parameter name is IDENTICAL to the RHS field name
// (`name = x.name`, `title = x.title`) — the signature of a constructor argument being seeded from its
// own like-named source field. `rawRenderedAssignment` matches every other case where a UI-facing
// parameter (`text`, `title`, `label`, `headline`, `name`, `contentDescription`) is assigned straight
// from a differently-named domain field (`text = x.name`, `label = it.displayName`) — that mismatch is
// exactly what a real Text/GlyphButton/ConfirmDialog/PickerOption call site looks like.
// `rawRenderedAssignmentBaseline` is the number that must burn down as screens get fixed — it is the
// TRUE population the owner reported. `rawFormSeedAssignmentBaseline` is tracked too (never silently
// dropped) but is NOT expected to shrink — those sites are dialog-form defaults, not rendered labels.
class RowLabelGuardTest {

    // Captured 2026-08-25 by re-deriving both regexes from the real diffs in fa9391a3 (PickListsScreen)
    // and 8c349816 (CommandsScreen, RewardsScreen), then re-scanning the whole feature tree with the
    // corrected regex. GiveawaysScreen.kt was fixed in THIS pass (giveaway.title row/edit/delete/close/
    // draw + code-pool name/delete) and its count is the one that actually moved this time.
    private val rawRenderedAssignmentBaseline: Map<String, Int> =
        mapOf(
            "admin/ui/AdminIamTab.kt" to 2,
            "admin/ui/AdminScreen.kt" to 4,
            "admin/ui/AdminTenantsTab.kt" to 2,
            "analytics/ui/AnalyticsScreen.kt" to 2,
            "assets/ui/AssetsScreen.kt" to 2,
            "automation/ui/AutomationScreen.kt" to 2,
            "bundles/ui/BundlesScreen.kt" to 4,
            "chat/ui/ChatScreen.kt" to 1,
            "codescripts/state/CodeScriptsController.kt" to 1,
            "codescripts/ui/CodeScriptsScreen.kt" to 3,
            "commands/ui/CommandsScreen.kt" to 2,
            "community/ui/CommunityScreen.kt" to 1,
            "connect/ui/ConnectScreen.kt" to 2,
            "customevents/ui/CustomEventsScreen.kt" to 4,
            "discord/ui/DiscordScreen.kt" to 1,
            "economy/state/EconomyController.kt" to 2,
            "economy/ui/EconomyScreen.kt" to 3,
            "features/ui/FeaturesScreen.kt" to 1,
            "federation/ui/FederationScreen.kt" to 1,
            "games/ui/GamesScreen.kt" to 1,
            "giveaways/ui/GiveawaysScreen.kt" to 2,
            "home/state/HomeController.kt" to 1,
            "language/ui/LanguagePicker.kt" to 2,
            "liveops/state/ScheduleController.kt" to 1,
            "liveops/ui/ScheduleScreen.kt" to 1,
            "mediashare/ui/MediaShareScreen.kt" to 1,
            "moderation/state/ModerationController.kt" to 1,
            "moderation/ui/ModerationScreen.kt" to 1,
            "obs/ui/ObsScreen.kt" to 2,
            "participant/ui/LeaderboardsScreen.kt" to 1,
            "participant/ui/ParticipantShell.kt" to 3,
            "participant/ui/PointsAndStoreScreen.kt" to 2,
            "pipelines/state/PipelinesController.kt" to 9,
            "pipelines/ui/PipelinesScreen.kt" to 3,
            "roles/ui/RolesScreen.kt" to 1,
            "settings/ui/SettingsScreen.kt" to 1,
            "setup/ui/SetupWizardScreen.kt" to 1,
            "shell/ui/ShellScreen.kt" to 2,
            "sound/ui/SoundScreen.kt" to 2,
            "timers/ui/TimersScreen.kt" to 1,
            "tts/ui/TtsScreen.kt" to 2,
            "webhooks/ui/WebhooksScreen.kt" to 3,
            "widgets/ui/WidgetGalleryReview.kt" to 2,
            "widgets/ui/WidgetSettingsForms.kt" to 2,
            "widgets/ui/WidgetsScreen.kt" to 2,
        )

    // Editor/state-controller constructor form-seeds — an `edit(summary)` factory populating an
    // EDITABLE field's default value (e.g. `name = command.name` in CommandEditor.edit()). Never
    // expected to shrink; tracked so a genuinely new rendered site can never hide inside this bucket.
    private val rawFormSeedAssignmentBaseline: Map<String, Int> =
        mapOf(
            "analytics/ui/AnalyticsScreen.kt" to 1,
            "commands/ui/CommandsScreen.kt" to 1,
            "community/state/CommunityController.kt" to 1,
            "connect/ui/ConnectScreen.kt" to 1,
            "giveaways/ui/GiveawaysScreen.kt" to 1,
            "home/state/HomeController.kt" to 2,
            "moderation/state/ModerationController.kt" to 1,
            "picklists/ui/PickListsScreen.kt" to 1,
            "pipelines/state/PipelinesController.kt" to 2,
            "pipelines/ui/PipelinesScreen.kt" to 1,
            "rewards/ui/RewardsScreen.kt" to 1,
            "shell/ui/ShellScreen.kt" to 2,
            "tts/state/TtsController.kt" to 1,
            "widgets/ui/WidgetSettingsForms.kt" to 5,
        )

    // A UI-facing parameter assigned straight from a DIFFERENTLY-named domain field — the shape of a
    // real Text/GlyphButton/ConfirmDialog/PickerOption call site (`text = command.name`,
    // `label = it.displayName`). Matched and classified together with the form-seed regex below so the
    // two buckets stay disjoint by construction — see [classify].
    private val rawLabelAssignment: Regex =
        Regex("""\b(text|title|label|headline|name|contentDescription)\s*=\s*[A-Za-z0-9_.]+\.(name|title|label|displayName)\b""")

    @Test
    fun rendered_row_labels_route_through_resolveRowLabel_not_a_raw_data_field() {
        val root: File = featureRoot()
        val renderedOffenders: MutableList<String> = mutableListOf()
        val formSeedOffenders: MutableList<String> = mutableListOf()
        root.walkTopDown().filter { it.isFile && it.extension == "kt" }.forEach { file ->
            val rel: String = file.relativeTo(root).path.replace('\\', '/')
            val text: String = file.readText()
            var renderedCount = 0
            var formSeedCount = 0
            rawLabelAssignment.findAll(text).forEach { match ->
                if (match.groupValues[1] == match.groupValues[2]) formSeedCount++ else renderedCount++
            }
            val renderedAllowed: Int = rawRenderedAssignmentBaseline[rel] ?: 0
            val formSeedAllowed: Int = rawFormSeedAssignmentBaseline[rel] ?: 0
            if (renderedCount > renderedAllowed) {
                renderedOffenders += "$rel: $renderedCount raw rendered label assignment(s), baseline allows $renderedAllowed"
            }
            if (formSeedCount > formSeedAllowed) {
                formSeedOffenders += "$rel: $formSeedCount raw form-seed assignment(s), baseline allows $formSeedAllowed"
            }
        }
        if (renderedOffenders.isNotEmpty() || formSeedOffenders.isNotEmpty()) {
            fail(
                "New row label assigned straight from a data field — a row whose source name is null/blank " +
                    "must never render an empty label. Route it through " +
                    "bot.nomnomz.dashboard.core.designsystem.resolveRowLabel() instead. If you fixed a RENDERED " +
                    "site, LOWER its number in rawRenderedAssignmentBaseline (never raise). New form-seed sites " +
                    "must be added to rawFormSeedAssignmentBaseline explicitly — they are never auto-allowed.\n" +
                    "Rendered offenders:\n" + renderedOffenders.joinToString("\n") +
                    "\nForm-seed offenders:\n" + formSeedOffenders.joinToString("\n")
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
