// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.consequences

import androidx.compose.runtime.Composable
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.network.BlastRadiusCategory
import bot.nomnomz.dashboard.core.network.BlastRadiusSummary
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.blast_radius_category_giveaway_codes_one
import nomnomzbot.composeapp.generated.resources.blast_radius_category_giveaway_codes_other
import nomnomzbot.composeapp.generated.resources.blast_radius_category_giveaways_one
import nomnomzbot.composeapp.generated.resources.blast_radius_category_giveaways_other
import nomnomzbot.composeapp.generated.resources.blast_radius_category_pipeline_steps_one
import nomnomzbot.composeapp.generated.resources.blast_radius_category_pipeline_steps_other
import nomnomzbot.composeapp.generated.resources.blast_radius_category_redemption_timers_one
import nomnomzbot.composeapp.generated.resources.blast_radius_category_redemption_timers_other
import nomnomzbot.composeapp.generated.resources.blast_radius_category_redemptions_one
import nomnomzbot.composeapp.generated.resources.blast_radius_category_redemptions_other
import nomnomzbot.composeapp.generated.resources.blast_radius_category_unknown
import nomnomzbot.composeapp.generated.resources.blast_radius_category_widget_versions_one
import nomnomzbot.composeapp.generated.resources.blast_radius_category_widget_versions_other
import nomnomzbot.composeapp.generated.resources.blast_radius_check_failed
import nomnomzbot.composeapp.generated.resources.blast_radius_checking
import nomnomzbot.composeapp.generated.resources.blast_radius_minimum_note
import nomnomzbot.composeapp.generated.resources.blast_radius_none
import nomnomzbot.composeapp.generated.resources.blast_radius_sample
import nomnomzbot.composeapp.generated.resources.blast_radius_summary

/**
 * The lookup state of a counted delete preview (S-CONSEQ). The three states are kept strictly distinct:
 * [Loading] withholds the destructive confirm (an unknown blast radius must never be confirmable), [Loaded]
 * renders the backend's real counted dependents — or the explicit "nothing references this" sentence for a
 * genuine zero — and [Failed] renders its OWN "could not check" message and, like the pipeline delete, still
 * lets the confirm proceed so a telemetry outage cannot deadlock a delete the operator still wants.
 *
 * A failed lookup is never collapsed into a zero: showing "0 dependents" for a check that did not run causes
 * exactly the loss this dialog exists to prevent.
 */
sealed interface BlastRadiusLoadState {
    data object Loading : BlastRadiusLoadState

    data class Loaded(val summary: BlastRadiusSummary) : BlastRadiusLoadState

    data object Failed : BlastRadiusLoadState
}

/**
 * The one delete-confirm dialog for every resource whose blast radius the backend counts. Every number comes
 * from [blastRadius]; nothing is counted or guessed client-side.
 *
 * @param title the dialog title for this resource kind.
 * @param message the lead sentence naming what is about to be deleted.
 * @param confirmLabel the destructive affirmative's label.
 * @param dismissLabel the cancel label.
 */
@Composable
fun DeleteBlastRadiusDialog(
    title: String,
    message: String,
    confirmLabel: String,
    dismissLabel: String,
    blastRadius: BlastRadiusLoadState,
    onConfirm: () -> Unit,
    onDismiss: () -> Unit,
) {
    ConfirmDialog(
        title = title,
        message = "$message\n\n${blastRadiusMessage(blastRadius)}",
        confirmLabel = confirmLabel,
        dismissLabel = dismissLabel,
        destructive = true,
        confirmEnabled = blastRadius !is BlastRadiusLoadState.Loading,
        onConfirm = onConfirm,
        onDismiss = onDismiss,
    )
}

@Composable
private fun blastRadiusMessage(state: BlastRadiusLoadState): String =
    when (state) {
        is BlastRadiusLoadState.Loading -> stringResource(Res.string.blast_radius_checking)
        is BlastRadiusLoadState.Failed -> stringResource(Res.string.blast_radius_check_failed)
        is BlastRadiusLoadState.Loaded -> loadedMessage(state.summary)
    }

@Composable
private fun loadedMessage(summary: BlastRadiusSummary): String {
    val lines: List<String> = summary.categories.map { categoryLine(it) }
    val body: String =
        if (lines.isEmpty()) {
            stringResource(Res.string.blast_radius_none)
        } else {
            stringResource(Res.string.blast_radius_summary) + lines.joinToString(separator = "") { "\n• $it" }
        }
    // The minimum note is appended to BOTH the counted and the zero body: a channel that resolves resources
    // from templates or custom code has an unknown remainder either way, and a bare "nothing references this"
    // would be a completeness claim the scan has not earned.
    return if (summary.isMinimum) {
        "$body\n\n${stringResource(Res.string.blast_radius_minimum_note)}"
    } else {
        body
    }
}

// The backend ships a category KEY, a count, and up to a handful of dependent names — never a sentence. An
// unrecognised key renders as an explicit "N of another kind" line rather than being dropped: silently
// omitting a counted category would understate the blast radius.
@Composable
private fun categoryLine(category: BlastRadiusCategory): String {
    val count: Int = category.count
    val counted: String =
        when (category.categoryKey) {
            "blast_radius_category_pipeline_steps" ->
                counted(
                    count,
                    Res.string.blast_radius_category_pipeline_steps_one,
                    Res.string.blast_radius_category_pipeline_steps_other,
                )
            "blast_radius_category_widget_versions" ->
                counted(
                    count,
                    Res.string.blast_radius_category_widget_versions_one,
                    Res.string.blast_radius_category_widget_versions_other,
                )
            "blast_radius_category_redemptions" ->
                counted(
                    count,
                    Res.string.blast_radius_category_redemptions_one,
                    Res.string.blast_radius_category_redemptions_other,
                )
            "blast_radius_category_redemption_timers" ->
                counted(
                    count,
                    Res.string.blast_radius_category_redemption_timers_one,
                    Res.string.blast_radius_category_redemption_timers_other,
                )
            "blast_radius_category_giveaway_codes" ->
                counted(
                    count,
                    Res.string.blast_radius_category_giveaway_codes_one,
                    Res.string.blast_radius_category_giveaway_codes_other,
                )
            "blast_radius_category_giveaways" ->
                counted(
                    count,
                    Res.string.blast_radius_category_giveaways_one,
                    Res.string.blast_radius_category_giveaways_other,
                )
            else -> stringResource(Res.string.blast_radius_category_unknown, count)
        }
    if (category.sample.isEmpty()) return counted
    return counted + stringResource(Res.string.blast_radius_sample, category.sample.joinToString(", "))
}

@Composable
private fun counted(count: Int, one: StringResource, other: StringResource): String =
    stringResource(if (count == 1) one else other, count)
