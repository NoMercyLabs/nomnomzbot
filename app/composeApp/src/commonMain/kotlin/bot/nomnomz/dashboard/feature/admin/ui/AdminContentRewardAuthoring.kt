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

// Authoring form for the `reward` platform-template kind: the same channel-point reward fields the channel's own
// reward dialog edits. No Twitch id and no pipeline: install creates the reward on the installing channel's
// Twitch, and that channel may pick one of its own pipelines at install time. The server enforces Twitch's limits.

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
import bot.nomnomz.dashboard.core.designsystem.component.ColorField
import bot.nomnomz.dashboard.core.designsystem.component.Switch
import bot.nomnomz.dashboard.core.designsystem.component.Textarea
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.network.PlatformTemplateJson
import bot.nomnomz.dashboard.core.network.RewardTemplatePayload
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.rewards_dialog_background_color_label
import nomnomzbot.composeapp.generated.resources.rewards_dialog_cooldown_label
import nomnomzbot.composeapp.generated.resources.rewards_dialog_cost_label
import nomnomzbot.composeapp.generated.resources.rewards_dialog_max_per_stream_label
import nomnomzbot.composeapp.generated.resources.rewards_dialog_max_per_user_label
import nomnomzbot.composeapp.generated.resources.rewards_dialog_prompt_label
import nomnomzbot.composeapp.generated.resources.rewards_dialog_require_input_label
import nomnomzbot.composeapp.generated.resources.rewards_dialog_response_label
import nomnomzbot.composeapp.generated.resources.rewards_dialog_timer_label
import nomnomzbot.composeapp.generated.resources.rewards_dialog_title_label
import org.jetbrains.compose.resources.stringResource

/** The form's editable state. Number fields stay text so a half-typed value never snaps back; blank = not set. */
internal data class RewardTemplateFields(
    val title: String = "",
    val cost: String = "",
    val prompt: String = "",
    val response: String = "",
    val isUserInputRequired: Boolean = false,
    val backgroundColor: String = "",
    val maxPerStream: String = "",
    val maxPerUserPerStream: String = "",
    val globalCooldownSeconds: String = "",
    val timerDurationSeconds: String = "",
) {
    fun toPayload(): RewardTemplatePayload =
        RewardTemplatePayload(
            title = title.trim(),
            cost = cost.toIntOrNull() ?: 0,
            prompt = prompt.trim().ifEmpty { null },
            response = response.trim().ifEmpty { null },
            isUserInputRequired = isUserInputRequired,
            backgroundColor = backgroundColor.trim().ifEmpty { null },
            maxPerStream = maxPerStream.toIntOrNull(),
            maxPerUserPerStream = maxPerUserPerStream.toIntOrNull(),
            globalCooldownSeconds = globalCooldownSeconds.toIntOrNull(),
            timerDurationSeconds = timerDurationSeconds.toIntOrNull(),
        )

    fun toPayloadJson(): String = PlatformTemplateJson.encodeToString(RewardTemplatePayload.serializer(), toPayload())

    /** True when the form holds enough for the server to accept it (Twitch: title 1-45 chars, cost at least 1). */
    fun isComplete(): Boolean = title.trim().length in 1..MaxTitleLength && (cost.toIntOrNull() ?: 0) >= 1

    companion object {
        const val MaxTitleLength: Int = 45

        /** Reads an existing draft back into the form; a payload of another shape starts from an empty form. */
        fun fromPayloadJson(payloadJson: String): RewardTemplateFields {
            val payload: RewardTemplatePayload =
                runCatching { PlatformTemplateJson.decodeFromString(RewardTemplatePayload.serializer(), payloadJson) }
                    .getOrNull() ?: return RewardTemplateFields()
            return RewardTemplateFields(
                title = payload.title,
                cost = payload.cost.takeIf { it > 0 }?.toString().orEmpty(),
                prompt = payload.prompt.orEmpty(),
                response = payload.response.orEmpty(),
                isUserInputRequired = payload.isUserInputRequired,
                backgroundColor = payload.backgroundColor.orEmpty(),
                maxPerStream = payload.maxPerStream?.toString().orEmpty(),
                maxPerUserPerStream = payload.maxPerUserPerStream?.toString().orEmpty(),
                globalCooldownSeconds = payload.globalCooldownSeconds?.toString().orEmpty(),
                timerDurationSeconds = payload.timerDurationSeconds?.toString().orEmpty(),
            )
        }
    }
}

@Composable
internal fun RewardPayloadEditor(fields: RewardTemplateFields, onFieldsChange: (RewardTemplateFields) -> Unit) {
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current

    Column(modifier = Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
        AppTextField(
            value = fields.title,
            onValueChange = { onFieldsChange(fields.copy(title = it.take(RewardTemplateFields.MaxTitleLength))) },
            label = stringResource(Res.string.rewards_dialog_title_label),
            modifier = Modifier.fillMaxWidth(),
        )
        NumberField(
            value = fields.cost,
            onValueChange = { onFieldsChange(fields.copy(cost = it)) },
            label = stringResource(Res.string.rewards_dialog_cost_label),
        )
        AppTextField(
            value = fields.prompt,
            onValueChange = { onFieldsChange(fields.copy(prompt = it)) },
            label = stringResource(Res.string.rewards_dialog_prompt_label),
            modifier = Modifier.fillMaxWidth(),
        )
        Textarea(
            value = fields.response,
            onValueChange = { onFieldsChange(fields.copy(response = it)) },
            label = stringResource(Res.string.rewards_dialog_response_label),
            modifier = Modifier.fillMaxWidth(),
        )
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(text = stringResource(Res.string.rewards_dialog_require_input_label), color = tokens.cardForeground)
            Switch(
                checked = fields.isUserInputRequired,
                onCheckedChange = { onFieldsChange(fields.copy(isUserInputRequired = it)) },
            )
        }
        ColorField(
            value = fields.backgroundColor,
            onValueChange = { onFieldsChange(fields.copy(backgroundColor = it)) },
            label = stringResource(Res.string.rewards_dialog_background_color_label),
            modifier = Modifier.fillMaxWidth(),
        )
        NumberField(
            value = fields.maxPerStream,
            onValueChange = { onFieldsChange(fields.copy(maxPerStream = it)) },
            label = stringResource(Res.string.rewards_dialog_max_per_stream_label),
        )
        NumberField(
            value = fields.maxPerUserPerStream,
            onValueChange = { onFieldsChange(fields.copy(maxPerUserPerStream = it)) },
            label = stringResource(Res.string.rewards_dialog_max_per_user_label),
        )
        NumberField(
            value = fields.globalCooldownSeconds,
            onValueChange = { onFieldsChange(fields.copy(globalCooldownSeconds = it)) },
            label = stringResource(Res.string.rewards_dialog_cooldown_label),
        )
        NumberField(
            value = fields.timerDurationSeconds,
            onValueChange = { onFieldsChange(fields.copy(timerDurationSeconds = it)) },
            label = stringResource(Res.string.rewards_dialog_timer_label),
        )
    }
}

@Composable
private fun NumberField(value: String, onValueChange: (String) -> Unit, label: String) {
    AppTextField(
        value = value,
        onValueChange = { onValueChange(it.filter { ch -> ch.isDigit() }) },
        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
        label = label,
        modifier = Modifier.fillMaxWidth(),
    )
}
