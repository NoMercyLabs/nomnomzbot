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
