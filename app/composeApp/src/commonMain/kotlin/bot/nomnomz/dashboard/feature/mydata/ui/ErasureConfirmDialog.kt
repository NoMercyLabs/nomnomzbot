// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.mydata.ui

import androidx.compose.runtime.Composable
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.network.ErasurePreview
import bot.nomnomz.dashboard.core.network.ErasurePreviewCategory
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.gdpr_erasure_category_chat_messages_one
import nomnomzbot.composeapp.generated.resources.gdpr_erasure_category_chat_messages_other
import nomnomzbot.composeapp.generated.resources.gdpr_erasure_category_connections_one
import nomnomzbot.composeapp.generated.resources.gdpr_erasure_category_connections_other
import nomnomzbot.composeapp.generated.resources.gdpr_erasure_category_consents_one
import nomnomzbot.composeapp.generated.resources.gdpr_erasure_category_consents_other
import nomnomzbot.composeapp.generated.resources.gdpr_erasure_category_keys_one
import nomnomzbot.composeapp.generated.resources.gdpr_erasure_category_keys_other
import nomnomzbot.composeapp.generated.resources.gdpr_erasure_category_profile
import nomnomzbot.composeapp.generated.resources.gdpr_erasure_category_records_one
import nomnomzbot.composeapp.generated.resources.gdpr_erasure_category_records_other
import nomnomzbot.composeapp.generated.resources.gdpr_erasure_category_refresh_tokens_one
import nomnomzbot.composeapp.generated.resources.gdpr_erasure_category_refresh_tokens_other
import nomnomzbot.composeapp.generated.resources.gdpr_erasure_category_services_one
import nomnomzbot.composeapp.generated.resources.gdpr_erasure_category_services_other
import nomnomzbot.composeapp.generated.resources.gdpr_erasure_category_sessions_one
import nomnomzbot.composeapp.generated.resources.gdpr_erasure_category_sessions_other
import nomnomzbot.composeapp.generated.resources.gdpr_erasure_category_unknown
import nomnomzbot.composeapp.generated.resources.gdpr_erasure_category_viewer_data_one
import nomnomzbot.composeapp.generated.resources.gdpr_erasure_category_viewer_data_other
import nomnomzbot.composeapp.generated.resources.mydata_cancel
import nomnomzbot.composeapp.generated.resources.mydata_erase_blast_radius_check_failed
import nomnomzbot.composeapp.generated.resources.mydata_erase_blast_radius_checking
import nomnomzbot.composeapp.generated.resources.mydata_erase_blast_radius_none
import nomnomzbot.composeapp.generated.resources.mydata_erase_blast_radius_summary
import nomnomzbot.composeapp.generated.resources.mydata_erase_confirm
import nomnomzbot.composeapp.generated.resources.mydata_erase_confirm_message
import nomnomzbot.composeapp.generated.resources.mydata_erase_confirm_title

/**
 * The erasure confirm dialog's counted-preview state (S-CONSEQ). Erasure is the least forgiving destructive
 * action in the product — it is irreversible and crypto-shreds the subject's keys — so the four states are
 * kept strictly distinct: [Loading] withholds the confirm (an unknown blast radius must never be confirmable),
 * [Loaded] renders the backend's real counts (or the explicit "nothing" sentence for a genuine zero), and
 * [Failed] renders its own "could not check" message. A failed lookup is never collapsed into a zero.
 */
sealed interface ErasurePreviewLoadState {
    data object Loading : ErasurePreviewLoadState

    data class Loaded(val preview: ErasurePreview) : ErasurePreviewLoadState

    data object Failed : ErasurePreviewLoadState
}

/**
 * The GDPR erasure confirm dialog — the one place the caller confirms an irreversible erasure of their own
 * data. Renders the real, backend-counted blast radius BEFORE the save. Every number comes from [preview];
 * nothing is counted or guessed client-side.
 *
 * Unlike the pipeline delete, a FAILED lookup also withholds the confirm: erasure is irreversible and
 * crypto-shreds keys, so proceeding on an unknown blast radius is not a trade the user should be offered.
 */
@Composable
fun ErasureConfirmDialog(preview: ErasurePreviewLoadState, onConfirm: () -> Unit, onDismiss: () -> Unit) {
    val baseMessage: String = stringResource(Res.string.mydata_erase_confirm_message)
    val blastRadiusLine: String = blastRadiusMessage(preview)

    ConfirmDialog(
        title = stringResource(Res.string.mydata_erase_confirm_title),
        message = "$baseMessage\n\n$blastRadiusLine",
        confirmLabel = stringResource(Res.string.mydata_erase_confirm),
        dismissLabel = stringResource(Res.string.mydata_cancel),
        destructive = true,
        confirmEnabled = preview is ErasurePreviewLoadState.Loaded,
        onConfirm = onConfirm,
        onDismiss = onDismiss,
    )
}

@Composable
private fun blastRadiusMessage(state: ErasurePreviewLoadState): String =
    when (state) {
        is ErasurePreviewLoadState.Loading -> stringResource(Res.string.mydata_erase_blast_radius_checking)
        is ErasurePreviewLoadState.Failed -> stringResource(Res.string.mydata_erase_blast_radius_check_failed)
        is ErasurePreviewLoadState.Loaded -> {
            val lines: List<String> = state.preview.categories.map { categoryLine(it) }
            if (lines.isEmpty()) {
                stringResource(Res.string.mydata_erase_blast_radius_none)
            } else {
                stringResource(Res.string.mydata_erase_blast_radius_summary) +
                    lines.joinToString(separator = "") { "\n• $it" }
            }
        }
    }

// The backend ships a category KEY and a count, never a sentence — the language lives here. An unrecognised
// key renders as an explicit "N rows of another kind" line rather than being dropped: silently omitting a
// counted category would understate the blast radius, which is the exact failure this dialog exists to stop.
@Composable
private fun categoryLine(category: ErasurePreviewCategory): String {
    val count: Int = category.rowCount
    return when (category.categoryKey) {
        "gdpr_erasure_category_profile" -> stringResource(Res.string.gdpr_erasure_category_profile)
        "gdpr_erasure_category_chat_messages" ->
            counted(
                count,
                Res.string.gdpr_erasure_category_chat_messages_one,
                Res.string.gdpr_erasure_category_chat_messages_other,
            )
        "gdpr_erasure_category_records" ->
            counted(
                count,
                Res.string.gdpr_erasure_category_records_one,
                Res.string.gdpr_erasure_category_records_other,
            )
        "gdpr_erasure_category_viewer_data" ->
            counted(
                count,
                Res.string.gdpr_erasure_category_viewer_data_one,
                Res.string.gdpr_erasure_category_viewer_data_other,
            )
        "gdpr_erasure_category_services" ->
            counted(
                count,
                Res.string.gdpr_erasure_category_services_one,
                Res.string.gdpr_erasure_category_services_other,
            )
        "gdpr_erasure_category_connections" ->
            counted(
                count,
                Res.string.gdpr_erasure_category_connections_one,
                Res.string.gdpr_erasure_category_connections_other,
            )
        "gdpr_erasure_category_refresh_tokens" ->
            counted(
                count,
                Res.string.gdpr_erasure_category_refresh_tokens_one,
                Res.string.gdpr_erasure_category_refresh_tokens_other,
            )
        "gdpr_erasure_category_sessions" ->
            counted(
                count,
                Res.string.gdpr_erasure_category_sessions_one,
                Res.string.gdpr_erasure_category_sessions_other,
            )
        "gdpr_erasure_category_consents" ->
            counted(
                count,
                Res.string.gdpr_erasure_category_consents_one,
                Res.string.gdpr_erasure_category_consents_other,
            )
        "gdpr_erasure_category_keys" ->
            counted(
                count,
                Res.string.gdpr_erasure_category_keys_one,
                Res.string.gdpr_erasure_category_keys_other,
            )
        else -> stringResource(Res.string.gdpr_erasure_category_unknown, count)
    }
}

@Composable
private fun counted(count: Int, one: StringResource, other: StringResource): String =
    stringResource(if (count == 1) one else other, count)
