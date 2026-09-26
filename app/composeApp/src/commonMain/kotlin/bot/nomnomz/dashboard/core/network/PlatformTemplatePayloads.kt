// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.network

// The kind-shaped `payloadJson` bodies of installable platform templates — shared by the admin authoring
// forms (which write them) and the channel pages (which read them to show what an install will do). Each
// mirrors its backend `<Kind>TemplatePayload` record.

import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json

/** The installable template kinds — match the backend `PlatformContentKinds` values verbatim. */
object PlatformTemplateKinds {
    const val EventResponse: String = "event_response"
    const val Timer: String = "timer"
    const val Reward: String = "reward"
    const val PickList: String = "pick_list"
}

/** An `event_response` template (backend `EventResponseTemplatePayload`). */
@Serializable
data class EventResponseTemplatePayload(
    val eventType: String = "",
    val responseType: String = ChatMessage,
    val message: String? = null,
    val metadata: Map<String, String> = emptyMap(),
    val isEnabled: Boolean = true,
) {
    /** A pipeline-type response binds one of the installing channel's own pipelines at install time. */
    val runsPipeline: Boolean get() = responseType == Pipeline

    companion object {
        const val ChatMessage: String = "chat_message"
        const val Pipeline: String = "pipeline"
        val ResponseTypes: List<String> = listOf(ChatMessage, "overlay", Pipeline, "none")
    }
}

/** A `timer` template (backend `TimerTemplatePayload`). A timer with no messages runs a pipeline the installing
 * channel picks at install time. */
@Serializable
data class TimerTemplatePayload(
    val name: String = "",
    val messages: List<String> = emptyList(),
    val intervalMinutes: Int = 30,
    val minChatActivity: Int = 0,
    val fireOnce: Boolean = false,
    val isEnabled: Boolean = true,
) {
    val runsPipelineOnly: Boolean get() = messages.isEmpty()
}

/** A `reward` template (backend `RewardTemplatePayload`): a channel-point reward definition. Install creates it on
 * the installing channel's Twitch; a pipeline to run on redemption is optional and picked at install time. */
@Serializable
data class RewardTemplatePayload(
    val title: String = "",
    val cost: Int = 0,
    val prompt: String? = null,
    val response: String? = null,
    val isUserInputRequired: Boolean = false,
    val backgroundColor: String? = null,
    val maxPerStream: Int? = null,
    val maxPerUserPerStream: Int? = null,
    val globalCooldownSeconds: Int? = null,
    val timerDurationSeconds: Int? = null,
)

/** A `pick_list` template (backend `PickListTemplatePayload`). The name is the `{list.pick.<name>}` key, kept
 * verbatim on install. */
@Serializable
data class PickListTemplatePayload(
    val name: String = "",
    val description: String? = null,
    val items: List<String> = emptyList(),
)

/** The one JSON configuration every template payload is read and written with. */
val PlatformTemplateJson: Json = Json {
    ignoreUnknownKeys = true
    encodeDefaults = true
    explicitNulls = false
}

/** Reads an `event_response` payload; null when the JSON is not that shape. */
fun PlatformTemplate.eventResponsePayload(): EventResponseTemplatePayload? =
    runCatching {
        PlatformTemplateJson.decodeFromString(EventResponseTemplatePayload.serializer(), payloadJson)
    }.getOrNull()

/** Reads a `timer` payload; null when the JSON is not that shape. */
fun PlatformTemplate.timerPayload(): TimerTemplatePayload? =
    runCatching { PlatformTemplateJson.decodeFromString(TimerTemplatePayload.serializer(), payloadJson) }.getOrNull()

/** Reads a `reward` payload; null when the JSON is not that shape. */
fun PlatformTemplate.rewardPayload(): RewardTemplatePayload? =
    runCatching { PlatformTemplateJson.decodeFromString(RewardTemplatePayload.serializer(), payloadJson) }.getOrNull()

/** Reads a `pick_list` payload; null when the JSON is not that shape. */
fun PlatformTemplate.pickListPayload(): PickListTemplatePayload? =
    runCatching { PlatformTemplateJson.decodeFromString(PickListTemplatePayload.serializer(), payloadJson) }.getOrNull()
