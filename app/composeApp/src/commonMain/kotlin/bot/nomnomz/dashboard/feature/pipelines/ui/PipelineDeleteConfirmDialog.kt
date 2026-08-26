// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.pipelines.ui

import androidx.compose.runtime.Composable
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.network.PipelineBlastRadiusSummary
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.pipelines_blast_radius_chat_triggers_one
import nomnomzbot.composeapp.generated.resources.pipelines_blast_radius_chat_triggers_other
import nomnomzbot.composeapp.generated.resources.pipelines_blast_radius_check_failed
import nomnomzbot.composeapp.generated.resources.pipelines_blast_radius_checking
import nomnomzbot.composeapp.generated.resources.pipelines_blast_radius_commands_one
import nomnomzbot.composeapp.generated.resources.pipelines_blast_radius_commands_other
import nomnomzbot.composeapp.generated.resources.pipelines_blast_radius_event_responses_one
import nomnomzbot.composeapp.generated.resources.pipelines_blast_radius_event_responses_other
import nomnomzbot.composeapp.generated.resources.pipelines_blast_radius_join_and
import nomnomzbot.composeapp.generated.resources.pipelines_blast_radius_join_comma
import nomnomzbot.composeapp.generated.resources.pipelines_blast_radius_none
import nomnomzbot.composeapp.generated.resources.pipelines_blast_radius_summary
import nomnomzbot.composeapp.generated.resources.pipelines_blast_radius_timers_one
import nomnomzbot.composeapp.generated.resources.pipelines_blast_radius_timers_other
import nomnomzbot.composeapp.generated.resources.pipelines_delete_cancel
import nomnomzbot.composeapp.generated.resources.pipelines_delete_confirm
import nomnomzbot.composeapp.generated.resources.pipelines_delete_message
import nomnomzbot.composeapp.generated.resources.pipelines_delete_title

/**
 * The pipeline delete confirm dialog's blast-radius lookup state (S-CONSEQ-b). The dialog renders a distinct
 * sentence for each: [Loading] withholds the destructive confirm (never lets the user act on an unknown
 * radius), [Loaded] renders the backend's real counted dependents (or the explicit "nothing references this"
 * sentence when [PipelineBlastRadiusSummary.totalReferences] is zero), and [Failed] renders its own
 * "could not check" message and — unlike Loading — lets the confirm proceed, so a telemetry failure never
 * deadlocks a delete the operator still wants to make.
 */
sealed interface BlastRadiusLoadState {
    data object Loading : BlastRadiusLoadState

    data class Loaded(val summary: PipelineBlastRadiusSummary) : BlastRadiusLoadState

    data object Failed : BlastRadiusLoadState
}

/**
 * The pipeline DELETE confirm dialog — the one place a destructive pipeline delete is confirmed. Fetches and
 * renders the real, backend-counted blast radius BEFORE the user can confirm (the owner's law: a destructive
 * save announces its consequences before it happens, never after). Every count comes from [blastRadius];
 * nothing is computed or guessed client-side.
 */
@Composable
fun PipelineDeleteConfirmDialog(
    pipelineName: String,
    blastRadius: BlastRadiusLoadState,
    onConfirm: () -> Unit,
    onDismiss: () -> Unit,
) {
    val baseMessage: String = stringResource(Res.string.pipelines_delete_message, pipelineName)
    val blastRadiusLine: String = blastRadiusMessage(blastRadius)

    ConfirmDialog(
        title = stringResource(Res.string.pipelines_delete_title),
        message = "$baseMessage\n\n$blastRadiusLine",
        confirmLabel = stringResource(Res.string.pipelines_delete_confirm),
        dismissLabel = stringResource(Res.string.pipelines_delete_cancel),
        destructive = true,
        // Loading withholds the affirmative — an unknown radius must never be confirmable. A Failed lookup
        // still allows confirming (with its own distinct warning line) so a telemetry outage can't block a
        // delete the operator still wants to make.
        confirmEnabled = blastRadius !is BlastRadiusLoadState.Loading,
        onConfirm = onConfirm,
        onDismiss = onDismiss,
    )
}

@Composable
private fun blastRadiusMessage(state: BlastRadiusLoadState): String =
    when (state) {
        is BlastRadiusLoadState.Loading -> stringResource(Res.string.pipelines_blast_radius_checking)
        is BlastRadiusLoadState.Failed -> stringResource(Res.string.pipelines_blast_radius_check_failed)
        is BlastRadiusLoadState.Loaded -> {
            val summary: PipelineBlastRadiusSummary = state.summary
            if (summary.totalReferences <= 0) {
                stringResource(Res.string.pipelines_blast_radius_none)
            } else {
                val segments: List<String> =
                    listOfNotNull(
                        countedSegment(
                            summary.commandCount,
                            Res.string.pipelines_blast_radius_commands_one,
                            Res.string.pipelines_blast_radius_commands_other,
                        ),
                        countedSegment(
                            summary.chatTriggerCount,
                            Res.string.pipelines_blast_radius_chat_triggers_one,
                            Res.string.pipelines_blast_radius_chat_triggers_other,
                        ),
                        countedSegment(
                            summary.timerCount,
                            Res.string.pipelines_blast_radius_timers_one,
                            Res.string.pipelines_blast_radius_timers_other,
                        ),
                        countedSegment(
                            summary.eventResponseCount,
                            Res.string.pipelines_blast_radius_event_responses_one,
                            Res.string.pipelines_blast_radius_event_responses_other,
                        ),
                    )
                val joined: String =
                    joinBlastRadiusSegments(
                        segments,
                        and = stringResource(Res.string.pipelines_blast_radius_join_and),
                        comma = stringResource(Res.string.pipelines_blast_radius_join_comma),
                    )
                stringResource(Res.string.pipelines_blast_radius_summary, joined)
            }
        }
    }

@Composable
private fun countedSegment(
    count: Int,
    one: StringResource,
    other: StringResource,
): String? {
    if (count <= 0) return null
    return stringResource(if (count == 1) one else other, count)
}

// A natural-language list join ("a, b and c") — the last two segments joined by the localized "and", every
// earlier pair by the localized comma. Never client-computed counts; only the ordering of already-localized
// segment strings.
private fun joinBlastRadiusSegments(segments: List<String>, and: String, comma: String): String =
    when (segments.size) {
        0 -> ""
        1 -> segments[0]
        else -> segments.dropLast(1).joinToString(comma) + and + segments.last()
    }
