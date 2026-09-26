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

// Authoring form for the `event_response` platform-template kind: which event (picked from the server's event
// catalogue), how the bot responds, and the chat message. The payload never names a pipeline — a pipeline-type template binds the installing channel's
// own pipeline at install time. The server validates the event and the message placeholders on save.

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import bot.nomnomz.dashboard.core.designsystem.component.RadioGroup
import bot.nomnomz.dashboard.core.designsystem.component.Select
import bot.nomnomz.dashboard.core.designsystem.component.Textarea
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.EventResponseTemplatePayload
import bot.nomnomz.dashboard.core.network.PlatformTemplateJson
import bot.nomnomz.dashboard.feature.eventresponses.ui.toEventLabel
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.admin_content_event_type_label
import nomnomzbot.composeapp.generated.resources.admin_content_event_type_placeholder
import nomnomzbot.composeapp.generated.resources.admin_content_message_label
import nomnomzbot.composeapp.generated.resources.admin_content_response_type_label
import nomnomzbot.composeapp.generated.resources.event_responses_type_chat_message
import nomnomzbot.composeapp.generated.resources.event_responses_type_none
import nomnomzbot.composeapp.generated.resources.event_responses_type_overlay
import nomnomzbot.composeapp.generated.resources.event_responses_type_pipeline
import org.jetbrains.compose.resources.stringResource

/** Serializes an event-response template for the create/draft request. */
internal fun EventResponseTemplatePayload.toPayloadJson(): String =
    PlatformTemplateJson.encodeToString(EventResponseTemplatePayload.serializer(), this)

/** Reads an existing draft back into the form; a payload of another shape starts from an empty form. */
internal fun eventResponsePayloadFrom(payloadJson: String): EventResponseTemplatePayload =
    runCatching {
        PlatformTemplateJson.decodeFromString(EventResponseTemplatePayload.serializer(), payloadJson)
    }.getOrDefault(EventResponseTemplatePayload())

/** True when the form holds enough for the server to accept it. */
internal fun EventResponseTemplatePayload.isComplete(): Boolean =
    eventType.isNotBlank() && (responseType != EventResponseTemplatePayload.ChatMessage || !message.isNullOrBlank())

@Composable
internal fun EventResponsePayloadEditor(
    payload: EventResponseTemplatePayload,
    eventTypes: List<String>,
    onPayloadChange: (EventResponseTemplatePayload) -> Unit,
) {
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    var eventMenuOpen: Boolean by remember { mutableStateOf(false) }
    val eventLabels: Map<String, String> = eventTypes.associateWith { it.toEventLabel() }
    // Resolved outside the label lambda: RadioGroup's label is a plain (T) -> String.
    val typeLabels: Map<String, String> =
        mapOf(
            EventResponseTemplatePayload.ChatMessage to stringResource(Res.string.event_responses_type_chat_message),
            "overlay" to stringResource(Res.string.event_responses_type_overlay),
            EventResponseTemplatePayload.Pipeline to stringResource(Res.string.event_responses_type_pipeline),
            "none" to stringResource(Res.string.event_responses_type_none),
        )

    Column(modifier = Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
        Select(
            value = payload.eventType.takeIf { it in eventTypes },
            options = eventTypes,
            onValueChange = { onPayloadChange(payload.copy(eventType = it)) },
            label = stringResource(Res.string.admin_content_event_type_label),
            optionLabel = { eventLabels[it] ?: it },
            expanded = eventMenuOpen,
            onExpandedChange = { eventMenuOpen = it },
            placeholder = stringResource(Res.string.admin_content_event_type_placeholder),
            modifier = Modifier.fillMaxWidth(),
        )
        Text(
            text = stringResource(Res.string.admin_content_response_type_label),
            style = typography.sm,
            color = tokens.foreground,
        )
        RadioGroup(
            options = EventResponseTemplatePayload.ResponseTypes,
            selected = payload.responseType,
            onSelectedChange = { onPayloadChange(payload.copy(responseType = it)) },
            label = { typeLabels[it] ?: it },
        )
        if (payload.responseType == EventResponseTemplatePayload.ChatMessage) {
            Textarea(
                value = payload.message.orEmpty(),
                onValueChange = { onPayloadChange(payload.copy(message = it)) },
                label = stringResource(Res.string.admin_content_message_label),
                modifier = Modifier.fillMaxWidth(),
            )
        }
    }
}
