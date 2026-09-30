// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.admin.ui

import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.height
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.ButtonVariant
import bot.nomnomz.dashboard.core.designsystem.component.Dialog
import bot.nomnomz.dashboard.core.designsystem.component.DialogFooter
import bot.nomnomz.dashboard.core.designsystem.component.DialogTitle
import bot.nomnomz.dashboard.core.designsystem.component.InlineError
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ErasurePreviewCategory
import bot.nomnomz.dashboard.feature.admin.state.AdminState
import bot.nomnomz.dashboard.feature.mydata.ui.erasureCategoryLine
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.admin_cancel
import nomnomzbot.composeapp.generated.resources.admin_erasure_retry_blast_radius_check_failed
import nomnomzbot.composeapp.generated.resources.admin_erasure_retry_blast_radius_checking
import nomnomzbot.composeapp.generated.resources.admin_erasure_retry_blast_radius_none
import nomnomzbot.composeapp.generated.resources.admin_erasure_retry_blast_radius_summary
import nomnomzbot.composeapp.generated.resources.admin_erasure_retry_body
import nomnomzbot.composeapp.generated.resources.admin_erasure_retry_category_profile
import nomnomzbot.composeapp.generated.resources.admin_erasure_retry_category_sessions_one
import nomnomzbot.composeapp.generated.resources.admin_erasure_retry_category_sessions_other
import nomnomzbot.composeapp.generated.resources.admin_erasure_retry_confirm
import nomnomzbot.composeapp.generated.resources.admin_erasure_retry_error
import nomnomzbot.composeapp.generated.resources.admin_erasure_retry_error_completed
import nomnomzbot.composeapp.generated.resources.admin_erasure_retry_error_in_progress
import nomnomzbot.composeapp.generated.resources.admin_erasure_retry_error_not_retryable
import nomnomzbot.composeapp.generated.resources.admin_erasure_retry_running
import nomnomzbot.composeapp.generated.resources.admin_erasure_retry_title
import org.jetbrains.compose.resources.stringResource

/**
 * The "Re-run erasure" confirm for one failed request. It states the consequence (the subject's data is
 * erased and their keys destroyed, for good) and the subject's real, backend-counted blast radius before the
 * destructive button can be pressed. An unknown blast radius — still loading or failed to load — keeps the
 * button disabled. A refusal or a second failure is shown here and the dialog stays open.
 */
@Composable
internal fun ErasureRetryDialog(
    state: AdminState,
    subjectLabel: String,
    onConfirm: () -> Unit,
    onDismiss: () -> Unit,
) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current

    Dialog(onDismissRequest = onDismiss) {
        DialogTitle(text = stringResource(Res.string.admin_erasure_retry_title))
        Spacer(modifier = Modifier.height(spacing.s2))
        Text(
            text = stringResource(Res.string.admin_erasure_retry_body, subjectLabel),
            style = typography.sm,
            color = tokens.foreground,
        )
        Spacer(modifier = Modifier.height(spacing.s2))
        Text(text = blastRadiusText(state), style = typography.sm, color = tokens.mutedForeground)
        state.erasureRetryError?.let {
            Spacer(modifier = Modifier.height(spacing.s2))
            InlineError(message = retryErrorText(it))
        }
        Spacer(modifier = Modifier.height(spacing.s4))
        DialogFooter {
            TextButton(onClick = onDismiss) { Text(text = stringResource(Res.string.admin_cancel)) }
            Button(
                variant = ButtonVariant.Destructive,
                onClick = onConfirm,
                enabled = state.erasureRetryPreview != null && !state.erasureRetryRunning,
                loading = state.erasureRetryRunning,
            ) {
                Text(
                    text = stringResource(
                        if (state.erasureRetryRunning) Res.string.admin_erasure_retry_running else Res.string.admin_erasure_retry_confirm,
                    ),
                )
            }
        }
    }
}

@Composable
private fun blastRadiusText(state: AdminState): String {
    if (state.erasureRetryPreviewFailed) return stringResource(Res.string.admin_erasure_retry_blast_radius_check_failed)
    val preview = state.erasureRetryPreview ?: return stringResource(Res.string.admin_erasure_retry_blast_radius_checking)
    val lines: List<String> = preview.categories.map { operatorCategoryLine(it) }
    if (lines.isEmpty()) return stringResource(Res.string.admin_erasure_retry_blast_radius_none)
    return stringResource(Res.string.admin_erasure_retry_blast_radius_summary) + lines.joinToString(separator = "") { "\n• $it" }
}

// The subject's own confirm speaks to "you"; an operator reads about someone else, so the two second-person
// lines get third-person wording here and every other category keeps the shared line.
@Composable
private fun operatorCategoryLine(category: ErasurePreviewCategory): String =
    when (category.categoryKey) {
        "gdpr_erasure_category_profile" -> stringResource(Res.string.admin_erasure_retry_category_profile)
        "gdpr_erasure_category_sessions" ->
            stringResource(
                if (category.rowCount == 1) {
                    Res.string.admin_erasure_retry_category_sessions_one
                } else {
                    Res.string.admin_erasure_retry_category_sessions_other
                },
                category.rowCount,
            )
        else -> erasureCategoryLine(category)
    }

// The refusals the server names get their own sentence; anything else (the erasure failing again, a lost
// permission) carries the server's own reason so the operator is never left with a bare "error".
@Composable
private fun retryErrorText(error: ApiError): String =
    when (error.code) {
        "ERASURE_ALREADY_COMPLETED" -> stringResource(Res.string.admin_erasure_retry_error_completed)
        "ERASURE_IN_PROGRESS" -> stringResource(Res.string.admin_erasure_retry_error_in_progress)
        "ERASURE_NOT_RETRYABLE" -> stringResource(Res.string.admin_erasure_retry_error_not_retryable)
        else -> stringResource(Res.string.admin_erasure_retry_error, error.message)
    }
