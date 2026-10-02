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

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.ButtonSize
import bot.nomnomz.dashboard.core.designsystem.component.ButtonVariant
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.Dialog
import bot.nomnomz.dashboard.core.designsystem.component.DialogFooter
import bot.nomnomz.dashboard.core.designsystem.component.DialogTitle
import bot.nomnomz.dashboard.core.designsystem.component.InlineError
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Spinner
import bot.nomnomz.dashboard.core.designsystem.component.Textarea
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.BuiltinReplyDefault
import bot.nomnomz.dashboard.feature.admin.state.BuiltinReplyDefaultEdit
import bot.nomnomz.dashboard.feature.admin.state.PlatformDefaultsController
import bot.nomnomz.dashboard.feature.admin.state.PlatformDefaultsState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.platform_defaults_apply
import nomnomzbot.composeapp.generated.resources.platform_defaults_cancel
import nomnomzbot.composeapp.generated.resources.platform_defaults_change
import nomnomzbot.composeapp.generated.resources.platform_defaults_reply_counts_overridable
import nomnomzbot.composeapp.generated.resources.platform_defaults_reply_edit_title
import nomnomzbot.composeapp.generated.resources.platform_defaults_reply_platform
import nomnomzbot.composeapp.generated.resources.platform_defaults_reply_shipped
import nomnomzbot.composeapp.generated.resources.platform_defaults_reply_shipped_fallback
import nomnomzbot.composeapp.generated.resources.platform_defaults_reply_template
import nomnomzbot.composeapp.generated.resources.platform_defaults_reply_template_required
import nomnomzbot.composeapp.generated.resources.platform_defaults_reply_title
import nomnomzbot.composeapp.generated.resources.platform_defaults_reply_use_shipped
import org.jetbrains.compose.resources.stringResource

/**
 * The built-in reply defaults: per response slot of every built-in command, the wording the bot answers with
 * (the shipped line, or the platform's replacement). Rows stay neutral; each quiet "Change" opens the editor,
 * whose apply is the one primary action of the flow.
 */
@Composable
internal fun BuiltinReplyDefaultsSection(state: PlatformDefaultsState, controller: PlatformDefaultsController) {
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current

    Column(modifier = Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
        state.repliesError?.let { InlineError(message = it) }
        if (state.repliesLoading) {
            Spinner(color = tokens.primary)
        } else {
            Card(modifier = Modifier.fillMaxWidth()) {
                state.replyDefaults.forEachIndexed { index, row ->
                    BuiltinReplyDefaultRow(row = row, onChange = { controller.openReplyEdit(row.builtinKey, row.slot) })
                    if (index < state.replyDefaults.lastIndex) Separator()
                }
            }
        }
    }
    state.replyEdit?.let { edit ->
        val row: BuiltinReplyDefault = state.replyDefaults.firstOrNull {
            it.builtinKey == edit.builtinKey && it.slot == edit.slot
        } ?: return@let
        BuiltinReplyDefaultEditDialog(row = row, edit = edit, controller = controller)
    }
}

@Composable
private fun BuiltinReplyDefaultRow(row: BuiltinReplyDefault, onChange: () -> Unit) {
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    val platform: String? = row.platformTemplate
    val shipped: String? = row.shippedTemplate

    Row(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
            Text(
                text = stringResource(Res.string.platform_defaults_reply_title, row.builtinKey, row.slot),
                style = typography.sm,
                color = tokens.cardForeground,
            )
            Text(
                text = when {
                    platform != null -> stringResource(Res.string.platform_defaults_reply_platform, platform)
                    shipped != null -> stringResource(Res.string.platform_defaults_reply_shipped, shipped)
                    else -> stringResource(Res.string.platform_defaults_reply_shipped_fallback)
                },
                style = typography.xs,
                color = tokens.cardForeground,
            )
            Text(
                text = stringResource(Res.string.platform_defaults_reply_counts_overridable, row.channelsWithOwnReply),
                style = typography.xs,
                color = tokens.mutedForeground,
            )
        }
        Button(onClick = onChange, variant = ButtonVariant.Outline, size = ButtonSize.Sm) {
            Text(text = stringResource(Res.string.platform_defaults_change), maxLines = 1)
        }
    }
}

/**
 * The editor for one slot's wording, open on the wording the bot uses now. Every edit drops the previous count
 * and a fresh one loads by itself, so the single primary "Apply to N channels" always names the channels this
 * exact wording changes. The quiet reset puts the shipped wording back, which clears the platform override.
 */
@Composable
internal fun BuiltinReplyDefaultEditDialog(
    row: BuiltinReplyDefault,
    edit: BuiltinReplyDefaultEdit,
    controller: PlatformDefaultsController,
) {
    val scope = rememberCoroutineScope()

    AutoBlastRadius(change = edit.change, needed = edit.preview == null && !edit.missingText && !edit.saving) {
        controller.previewReplyEdit()
    }
    Dialog(onDismissRequest = controller::dismissReplyEdit) {
        DialogTitle(text = stringResource(Res.string.platform_defaults_reply_edit_title, row.builtinKey, row.slot))
        Textarea(
            value = edit.template,
            onValueChange = controller::editReplyTemplate,
            label = stringResource(Res.string.platform_defaults_reply_template),
            enabled = !edit.saving,
            isError = edit.missingText,
            errorText = if (edit.missingText) stringResource(Res.string.platform_defaults_reply_template_required) else null,
            modifier = Modifier.fillMaxWidth(),
        )
        if (edit.differsFromShipped) {
            Button(
                onClick = controller::resetReplyToShipped,
                variant = ButtonVariant.Ghost,
                size = ButtonSize.Sm,
                enabled = !edit.saving,
            ) {
                Text(text = stringResource(Res.string.platform_defaults_reply_use_shipped), maxLines = 1)
            }
        }
        if (!edit.missingText) PlatformDefaultBlastRadiusText(preview = edit.preview)
        DialogFooter {
            Button(onClick = controller::dismissReplyEdit, variant = ButtonVariant.Ghost, enabled = !edit.saving) {
                Text(text = stringResource(Res.string.platform_defaults_cancel), maxLines = 1)
            }
            Button(
                onClick = { scope.launch { controller.saveReplyEdit() } },
                enabled = edit.canSave,
                loading = edit.saving,
            ) {
                Text(
                    text = stringResource(Res.string.platform_defaults_apply, edit.preview?.channelsAffected ?: 0),
                    maxLines = 1,
                )
            }
        }
    }
}
