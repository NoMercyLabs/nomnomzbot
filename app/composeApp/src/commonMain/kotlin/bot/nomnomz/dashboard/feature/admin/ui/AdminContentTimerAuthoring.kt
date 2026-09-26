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

// Authoring form for the `timer` platform-template kind: the same fields the channel's own timer dialog edits
// (name, message rotation, interval, chat-activity floor, fire once, enabled). A template with no messages runs
// a pipeline that the installing channel picks at install time; the server validates the rest on save.

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.KeyboardType
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.GlyphButton
import bot.nomnomz.dashboard.core.designsystem.component.Switch
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.icon.TrashGlyph
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.PlatformTemplateJson
import bot.nomnomz.dashboard.core.network.TimerTemplatePayload
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.admin_content_timer_pipeline_only_hint
import nomnomzbot.composeapp.generated.resources.timers_dialog_add_message
import nomnomzbot.composeapp.generated.resources.timers_dialog_enabled
import nomnomzbot.composeapp.generated.resources.timers_dialog_fire_once
import nomnomzbot.composeapp.generated.resources.timers_dialog_interval
import nomnomzbot.composeapp.generated.resources.timers_dialog_message
import nomnomzbot.composeapp.generated.resources.timers_dialog_message_remove
import nomnomzbot.composeapp.generated.resources.timers_dialog_messages
import nomnomzbot.composeapp.generated.resources.timers_dialog_min_chat_activity
import nomnomzbot.composeapp.generated.resources.timers_dialog_name
import org.jetbrains.compose.resources.stringResource

/** The form's editable state. Number fields stay text so a half-typed value never snaps back. */
internal data class TimerTemplateFields(
    val name: String = "",
    val messages: List<String> = listOf(""),
    val intervalMinutes: String = "30",
    val minChatActivity: String = "0",
    val fireOnce: Boolean = false,
    val isEnabled: Boolean = true,
) {
    /** The payload the server receives; blank message rows are dropped. */
    fun toPayload(): TimerTemplatePayload =
        TimerTemplatePayload(
            name = name.trim(),
            messages = messages.map { it.trim() }.filter { it.isNotEmpty() },
            intervalMinutes = intervalMinutes.toIntOrNull() ?: 0,
            minChatActivity = minChatActivity.toIntOrNull() ?: 0,
            fireOnce = fireOnce,
            isEnabled = isEnabled,
        )

    fun toPayloadJson(): String = PlatformTemplateJson.encodeToString(TimerTemplatePayload.serializer(), toPayload())

    /** True when the form holds enough for the server to accept it. */
    fun isComplete(): Boolean = name.isNotBlank() && (intervalMinutes.toIntOrNull() ?: 0) in 1..1440

    companion object {
        /** Reads an existing draft back into the form; a payload of another shape starts from an empty form. */
        fun fromPayloadJson(payloadJson: String): TimerTemplateFields {
            val payload: TimerTemplatePayload =
                runCatching { PlatformTemplateJson.decodeFromString(TimerTemplatePayload.serializer(), payloadJson) }
                    .getOrNull() ?: return TimerTemplateFields()
            return TimerTemplateFields(
                name = payload.name,
                messages = payload.messages.ifEmpty { listOf("") },
                intervalMinutes = payload.intervalMinutes.toString(),
                minChatActivity = payload.minChatActivity.toString(),
                fireOnce = payload.fireOnce,
                isEnabled = payload.isEnabled,
            )
        }
    }
}

@Composable
internal fun TimerPayloadEditor(fields: TimerTemplateFields, onFieldsChange: (TimerTemplateFields) -> Unit) {
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    val messageLabel: String = stringResource(Res.string.timers_dialog_message)
    val removeLabel: String = stringResource(Res.string.timers_dialog_message_remove)

    Column(modifier = Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
        AppTextField(
            value = fields.name,
            onValueChange = { onFieldsChange(fields.copy(name = it)) },
            label = stringResource(Res.string.timers_dialog_name),
            modifier = Modifier.fillMaxWidth(),
        )
        Text(
            text = stringResource(Res.string.timers_dialog_messages),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
        fields.messages.forEachIndexed { index, message ->
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(spacing.s2),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                AppTextField(
                    value = message,
                    onValueChange = { updated ->
                        onFieldsChange(fields.copy(messages = fields.messages.toMutableList().also { it[index] = updated }))
                    },
                    label = messageLabel,
                    modifier = Modifier.weight(1f),
                )
                if (fields.messages.size > 1) {
                    GlyphButton(
                        icon = TrashGlyph,
                        label = removeLabel,
                        onClick = { onFieldsChange(fields.copy(messages = fields.messages.filterIndexed { i, _ -> i != index })) },
                        tint = tokens.destructive,
                    )
                }
            }
        }
        TextButton(onClick = { onFieldsChange(fields.copy(messages = fields.messages + "")) }) {
            Text(text = stringResource(Res.string.timers_dialog_add_message), color = tokens.primary)
        }
        if (fields.toPayload().runsPipelineOnly) {
            Text(
                text = stringResource(Res.string.admin_content_timer_pipeline_only_hint),
                style = typography.xs,
                color = tokens.mutedForeground,
            )
        }
        AppTextField(
            value = fields.intervalMinutes,
            onValueChange = { onFieldsChange(fields.copy(intervalMinutes = it.filter { ch -> ch.isDigit() })) },
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
            label = stringResource(Res.string.timers_dialog_interval),
            modifier = Modifier.fillMaxWidth(),
        )
        AppTextField(
            value = fields.minChatActivity,
            onValueChange = { onFieldsChange(fields.copy(minChatActivity = it.filter { ch -> ch.isDigit() })) },
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
            label = stringResource(Res.string.timers_dialog_min_chat_activity),
            modifier = Modifier.fillMaxWidth(),
        )
        SwitchRow(
            label = stringResource(Res.string.timers_dialog_fire_once),
            checked = fields.fireOnce,
            onCheckedChange = { onFieldsChange(fields.copy(fireOnce = it)) },
        )
        SwitchRow(
            label = stringResource(Res.string.timers_dialog_enabled),
            checked = fields.isEnabled,
            onCheckedChange = { onFieldsChange(fields.copy(isEnabled = it)) },
        )
    }
}

@Composable
private fun SwitchRow(label: String, checked: Boolean, onCheckedChange: (Boolean) -> Unit) {
    val tokens = LocalTokens.current
    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(text = label, color = tokens.cardForeground)
        Switch(checked = checked, onCheckedChange = onCheckedChange)
    }
}
