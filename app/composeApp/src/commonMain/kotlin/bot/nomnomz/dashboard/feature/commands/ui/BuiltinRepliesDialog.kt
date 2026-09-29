// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.commands.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import bot.nomnomz.dashboard.core.designsystem.component.Badge
import bot.nomnomz.dashboard.core.designsystem.component.BadgeVariant
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.ButtonSize
import bot.nomnomz.dashboard.core.designsystem.component.ButtonVariant
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.Dialog
import bot.nomnomz.dashboard.core.designsystem.component.DialogDescription
import bot.nomnomz.dashboard.core.designsystem.component.DialogFooter
import bot.nomnomz.dashboard.core.designsystem.component.DialogTitle
import bot.nomnomz.dashboard.core.designsystem.component.InlineError
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Spinner
import bot.nomnomz.dashboard.core.designsystem.component.Textarea
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.i18n.resolveSchemaString
import bot.nomnomz.dashboard.core.network.BuiltinReply
import bot.nomnomz.dashboard.core.network.BuiltinReplyGroup
import bot.nomnomz.dashboard.feature.commands.state.BOT_REPLIES_GROUP
import bot.nomnomz.dashboard.feature.commands.state.BuiltinRepliesController
import bot.nomnomz.dashboard.feature.commands.state.BuiltinRepliesState
import bot.nomnomz.dashboard.feature.commands.state.ReplyEdit
import bot.nomnomz.dashboard.feature.commands.state.previewReply
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.builtin_replies_cancel
import nomnomzbot.composeapp.generated.resources.builtin_replies_close
import nomnomzbot.composeapp.generated.resources.builtin_replies_edit
import nomnomzbot.composeapp.generated.resources.builtin_replies_field
import nomnomzbot.composeapp.generated.resources.builtin_replies_intro
import nomnomzbot.composeapp.generated.resources.builtin_replies_locked
import nomnomzbot.composeapp.generated.resources.builtin_replies_overridden
import nomnomzbot.composeapp.generated.resources.builtin_replies_preview
import nomnomzbot.composeapp.generated.resources.builtin_replies_reset
import nomnomzbot.composeapp.generated.resources.builtin_replies_reset_confirm
import nomnomzbot.composeapp.generated.resources.builtin_replies_reset_message
import nomnomzbot.composeapp.generated.resources.builtin_replies_reset_title
import nomnomzbot.composeapp.generated.resources.builtin_replies_save
import nomnomzbot.composeapp.generated.resources.builtin_replies_title_bot
import nomnomzbot.composeapp.generated.resources.builtin_replies_title_command
import nomnomzbot.composeapp.generated.resources.builtin_replies_variables
import nomnomzbot.composeapp.generated.resources.builtin_replies_variations
import org.jetbrains.compose.resources.stringResource

/**
 * The built-in reply editor (commands-pipelines.md §11): every reply slot of one built-in (or the bot's own
 * lines) with the text the bot sends now. Rows stay quiet — a ghost "Edit" per slot; the open slot's "Save
 * reply" is the one primary action of the flow, and "Reset to default" is a confirmed destructive-ghost action
 * shown only when the channel has its own text. Rows are separated, not nested in rounded cards, so no radius
 * sits inside the dialog's own.
 */
@Composable
fun BuiltinRepliesDialog(state: BuiltinRepliesState, controller: BuiltinRepliesController) {
    val openGroup: String = state.openGroup ?: return
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current
    var pendingReset: BuiltinReply? by remember { mutableStateOf(null) }

    Dialog(onDismissRequest = controller::close) {
        DialogTitle(
            text = if (openGroup == BOT_REPLIES_GROUP) {
                stringResource(Res.string.builtin_replies_title_bot)
            } else {
                stringResource(Res.string.builtin_replies_title_command, "!$openGroup")
            },
        )
        DialogDescription(text = stringResource(Res.string.builtin_replies_intro))
        state.error?.let { InlineError(message = it) }
        if (state.loading) {
            Spinner(color = tokens.mutedForeground)
        } else {
            val replies: List<BuiltinReply> = state.visibleGroups.flatMap(BuiltinReplyGroup::replies)
            Column(
                modifier = Modifier.weight(1f, fill = false).verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(spacing.s3),
            ) {
                replies.forEachIndexed { index, reply ->
                    ReplySlotRow(
                        reply = reply,
                        edit = state.editing?.takeIf { it.builtinKey == reply.builtinKey && it.slot == reply.slot },
                        showGroup = openGroup == BOT_REPLIES_GROUP,
                        controller = controller,
                        onReset = { pendingReset = reply },
                    )
                    if (index < replies.lastIndex) Separator()
                }
            }
        }
        DialogFooter {
            Button(onClick = controller::close, variant = ButtonVariant.Outline) {
                Text(text = stringResource(Res.string.builtin_replies_close), maxLines = 1)
            }
        }
    }

    pendingReset?.let { reply ->
        val scope = rememberCoroutineScope()
        ConfirmDialog(
            title = stringResource(Res.string.builtin_replies_reset_title),
            message = stringResource(Res.string.builtin_replies_reset_message, reply.defaultTemplate),
            confirmLabel = stringResource(Res.string.builtin_replies_reset_confirm),
            dismissLabel = stringResource(Res.string.builtin_replies_cancel),
            destructive = true,
            onConfirm = {
                pendingReset = null
                scope.launch { controller.reset(reply) }
            },
            onDismiss = { pendingReset = null },
        )
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun ReplySlotRow(
    reply: BuiltinReply,
    edit: ReplyEdit?,
    showGroup: Boolean,
    controller: BuiltinRepliesController,
    onReset: () -> Unit,
) {
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    val scope = rememberCoroutineScope()
    val label: String = resolveSchemaString(reply.label)

    Column(modifier = Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(spacing.s2),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(
                text = if (showGroup) "${reply.builtinKey} · $label" else label,
                style = typography.sm,
                color = tokens.popoverForeground,
                modifier = Modifier.weight(1f),
            )
            if (reply.isOverridden) {
                Badge(variant = BadgeVariant.Secondary) {
                    Text(text = stringResource(Res.string.builtin_replies_overridden), style = typography.xs)
                }
            }
            if (reply.isOverridden && edit == null) {
                Button(
                    onClick = onReset,
                    variant = ButtonVariant.DestructiveGhost,
                    size = ButtonSize.Sm,
                    modifier = Modifier.testTag("reset-${reply.builtinKey}-${reply.slot}"),
                ) {
                    Text(text = stringResource(Res.string.builtin_replies_reset), maxLines = 1)
                }
            }
            if (!reply.isLocked && edit == null) {
                Button(
                    onClick = { controller.startEdit(reply) },
                    variant = ButtonVariant.Ghost,
                    size = ButtonSize.Sm,
                    modifier = Modifier.testTag("edit-${reply.builtinKey}-${reply.slot}"),
                ) {
                    Text(text = stringResource(Res.string.builtin_replies_edit), maxLines = 1)
                }
            }
        }
        Text(
            text = resolveSchemaString(reply.description),
            style = typography.xs,
            color = tokens.mutedForeground,
        )
        if (edit == null) {
            Text(text = reply.effectiveTemplate, style = typography.sm, color = tokens.popoverForeground)
            if (!reply.isOverridden && reply.toneVariations.size > 1) {
                Text(
                    text = stringResource(Res.string.builtin_replies_variations, reply.toneVariations.size),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                )
            }
            if (reply.isLocked) {
                Text(
                    text = stringResource(Res.string.builtin_replies_locked),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                )
            }
        } else {
            Textarea(
                value = edit.template,
                onValueChange = controller::editTemplate,
                label = stringResource(Res.string.builtin_replies_field),
                enabled = !edit.saving,
                isError = edit.error != null,
                errorText = edit.error,
                minLines = 2,
                modifier = Modifier.fillMaxWidth(),
            )
            if (reply.variables.isNotEmpty()) {
                Text(
                    text = stringResource(Res.string.builtin_replies_variables),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                )
                FlowRow(
                    horizontalArrangement = Arrangement.spacedBy(spacing.s1),
                    verticalArrangement = Arrangement.spacedBy(spacing.s1),
                ) {
                    reply.variables.forEach { variable ->
                        val description: String = resolveSchemaString(variable.description)
                        Badge(
                            variant = BadgeVariant.Outline,
                            onClick = { controller.insertVariable(variable.name) },
                            modifier = Modifier.semantics { contentDescription = description },
                        ) {
                            Text(text = "{${variable.name}}", style = typography.xs)
                        }
                    }
                }
            }
            Text(
                text = stringResource(Res.string.builtin_replies_preview, previewReply(edit.template, reply)),
                style = typography.xs,
                color = tokens.mutedForeground,
            )
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(spacing.s2, Alignment.End),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Button(
                    onClick = controller::cancelEdit,
                    variant = ButtonVariant.Ghost,
                    size = ButtonSize.Sm,
                    enabled = !edit.saving,
                ) {
                    Text(text = stringResource(Res.string.builtin_replies_cancel), maxLines = 1)
                }
                Button(
                    onClick = { scope.launch { controller.save() } },
                    size = ButtonSize.Sm,
                    modifier = Modifier.testTag("save-${reply.builtinKey}-${reply.slot}"),
                    enabled = edit.template.isNotBlank(),
                    loading = edit.saving,
                ) {
                    Text(text = stringResource(Res.string.builtin_replies_save), maxLines = 1)
                }
            }
        }
    }
}
