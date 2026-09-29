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
import bot.nomnomz.dashboard.core.designsystem.component.Switch
import bot.nomnomz.dashboard.core.designsystem.component.Textarea
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.EventResponseDefault
import bot.nomnomz.dashboard.feature.admin.state.EventResponseDefaultEdit
import bot.nomnomz.dashboard.feature.admin.state.PlatformDefaultsController
import bot.nomnomz.dashboard.feature.admin.state.PlatformDefaultsState
import bot.nomnomz.dashboard.feature.eventresponses.ui.toEventLabel
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.event_responses_speak_with_tts
import nomnomzbot.composeapp.generated.resources.platform_defaults_apply
import nomnomzbot.composeapp.generated.resources.platform_defaults_cancel
import nomnomzbot.composeapp.generated.resources.platform_defaults_change
import nomnomzbot.composeapp.generated.resources.platform_defaults_check_first
import nomnomzbot.composeapp.generated.resources.platform_defaults_check_impact
import nomnomzbot.composeapp.generated.resources.platform_defaults_event_counts
import nomnomzbot.composeapp.generated.resources.platform_defaults_event_edit_title
import nomnomzbot.composeapp.generated.resources.platform_defaults_event_enabled
import nomnomzbot.composeapp.generated.resources.platform_defaults_event_message
import nomnomzbot.composeapp.generated.resources.platform_defaults_event_message_required
import nomnomzbot.composeapp.generated.resources.platform_defaults_event_off
import nomnomzbot.composeapp.generated.resources.platform_defaults_event_on
import nomnomzbot.composeapp.generated.resources.platform_defaults_event_variables
import org.jetbrains.compose.resources.stringResource

/**
 * The event-response defaults: per event type, whether channels that never saved their own response answer
 * in chat and with which message. Rows stay neutral; each quiet "Change" opens the editor, whose apply is
 * the one primary action of the flow.
 */
@Composable
internal fun EventResponseDefaultsSection(state: PlatformDefaultsState, controller: PlatformDefaultsController) {
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current

    Column(modifier = Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
        state.eventsError?.let { InlineError(message = it) }
        if (state.eventsLoading) {
            Spinner(color = tokens.primary)
        } else {
            Card(modifier = Modifier.fillMaxWidth()) {
                state.eventDefaults.forEachIndexed { index, row ->
                    EventResponseDefaultRow(row = row, onChange = { controller.openEventEdit(row.eventType) })
                    if (index < state.eventDefaults.lastIndex) Separator()
                }
            }
        }
    }
    state.eventEdit?.let { edit ->
        val row: EventResponseDefault = state.eventDefaults.firstOrNull { it.eventType == edit.eventType } ?: return@let
        EventResponseDefaultEditDialog(row = row, edit = edit, controller = controller)
    }
}

@Composable
private fun EventResponseDefaultRow(row: EventResponseDefault, onChange: () -> Unit) {
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    Row(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
            Text(text = row.eventType.toEventLabel(), style = typography.sm, color = tokens.cardForeground)
            Text(
                text = if (row.isEnabled) {
                    stringResource(Res.string.platform_defaults_event_on, row.message.orEmpty())
                } else {
                    stringResource(Res.string.platform_defaults_event_off)
                },
                style = typography.xs,
                color = tokens.cardForeground,
            )
            Text(
                text = stringResource(
                    Res.string.platform_defaults_event_counts,
                    row.channelsFollowing,
                    row.channelsWithOwnResponse,
                ),
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
 * The editor for one event type's default. Changing the switch or the message drops the previous count, so
 * "Check impact" (outline) must run for exactly these values before the single primary "Apply to N channels"
 * arms.
 */
@Composable
internal fun EventResponseDefaultEditDialog(
    row: EventResponseDefault,
    edit: EventResponseDefaultEdit,
    controller: PlatformDefaultsController,
) {
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    val scope = rememberCoroutineScope()
    val busy: Boolean = edit.saving || edit.previewing
    val missingMessage: Boolean = edit.isEnabled && edit.message.isBlank()

    Dialog(onDismissRequest = controller::dismissEventEdit) {
        DialogTitle(text = stringResource(Res.string.platform_defaults_event_edit_title, row.eventType.toEventLabel()))
        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2), verticalAlignment = Alignment.CenterVertically) {
            Switch(checked = edit.isEnabled, onCheckedChange = controller::editEventEnabled, enabled = !busy)
            Text(
                text = stringResource(Res.string.platform_defaults_event_enabled),
                style = typography.sm,
                color = tokens.popoverForeground,
            )
        }
        Textarea(
            value = edit.message,
            onValueChange = controller::editEventMessage,
            label = stringResource(Res.string.platform_defaults_event_message),
            enabled = !busy,
            isError = missingMessage,
            errorText = if (missingMessage) stringResource(Res.string.platform_defaults_event_message_required) else null,
            supportingText = row.variables.takeIf { it.isNotEmpty() }?.let { variables ->
                stringResource(Res.string.platform_defaults_event_variables, variables.joinToString(", ") { "{$it}" })
            },
            modifier = Modifier.fillMaxWidth(),
        )
        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2), verticalAlignment = Alignment.CenterVertically) {
            Switch(checked = edit.speakWithTts, onCheckedChange = controller::editEventSpeakWithTts, enabled = !busy)
            Text(
                text = stringResource(Res.string.event_responses_speak_with_tts),
                style = typography.sm,
                color = tokens.popoverForeground,
            )
        }
        if (edit.preview != null || edit.previewing) {
            PlatformDefaultBlastRadiusText(preview = edit.preview)
        } else {
            Text(
                text = stringResource(Res.string.platform_defaults_check_first),
                style = typography.sm,
                color = tokens.mutedForeground,
            )
        }
        DialogFooter {
            Button(onClick = controller::dismissEventEdit, variant = ButtonVariant.Ghost, enabled = !edit.saving) {
                Text(text = stringResource(Res.string.platform_defaults_cancel), maxLines = 1)
            }
            Button(
                onClick = { scope.launch { controller.previewEventEdit() } },
                variant = ButtonVariant.Outline,
                enabled = !busy && !missingMessage && edit.preview == null,
                loading = edit.previewing,
            ) {
                Text(text = stringResource(Res.string.platform_defaults_check_impact), maxLines = 1)
            }
            Button(
                onClick = { scope.launch { controller.saveEventEdit() } },
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
