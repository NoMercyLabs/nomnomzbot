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

import kotlinx.serialization.Serializable

// The typed stream facade — the channel's broadcast metadata (title, category/game, tags) the Settings page
// reads and edits, plus the read-only live context (is-live, viewer count, language) it surfaces alongside.
// Stream info is a single object (backend `StatusResponseDto<StreamInfoDto>`), so both the read and the
// combined update unwrap getEnvelope/putEnvelope's `data: T`. State holders depend on this interface and fake
// it in tests without HTTP.
//
// Backend routes (StreamController):
//   GET /api/v1/channels/{channelId}/stream  →  StatusResponseDto<StreamInfoDto>
//   PUT /api/v1/channels/{channelId}/stream  ←  UpdateStreamRequest  →  StatusResponseDto<StreamInfoDto>
//
// The PUT is the combined update: it resolves the game name → Twitch game id server-side, pushes the change
// through Helix (a real but non-destructive write), and echoes the saved StreamInfoDto back. The controller
// also exposes granular PATCH title/game/tags routes, but the single PUT covers the whole editable surface,
// so the Settings page uses it.
interface StreamApi {
    /** The channel's current stream info — editable metadata plus read-only live context. */
    suspend fun info(channelId: String): ApiResult<StreamInfo>

    /** Persist [update]; the backend echoes the saved stream info back (with the canonicalized game name). */
    suspend fun update(channelId: String, update: StreamInfoUpdate): ApiResult<StreamInfo>

    /**
     * Twitch game/category autocomplete (`GET /stream/categories?query=…`, backend `SearchCategories`). Powers
     * the category picker that replaces the raw free-text game id — a match carries the canonical [Category.name]
     * the stream update writes and the [Category.id] the schedule segment writes. A blank query yields no rows.
     */
    suspend fun searchCategories(channelId: String, query: String): ApiResult<List<Category>>
}

class RestStreamApi(private val client: ApiClient) : StreamApi {
    override suspend fun info(channelId: String): ApiResult<StreamInfo> =
        client.getEnvelope("api/v1/channels/$channelId/stream")

    override suspend fun update(
        channelId: String,
        update: StreamInfoUpdate,
    ): ApiResult<StreamInfo> = client.putEnvelope("api/v1/channels/$channelId/stream", update)

    override suspend fun searchCategories(
        channelId: String,
        query: String,
    ): ApiResult<List<Category>> =
        // StatusResponseDto<List<CategoryDto>> envelope — getEnvelope unwraps the `data` list.
        client.getEnvelope(
            "api/v1/channels/$channelId/stream/categories?query=${query.encodeQuery()}"
        )
}

/**
 * One Twitch game/category match (backend `CategoryDto`): [id] is the Twitch category id the schedule segment's
 * `categoryId` write consumes, [name] is the canonical game name the stream-info update writes, and [boxArtUrl]
 * is the cover art (template url with `{width}`/`{height}` placeholders) the picker row can show.
 */
@Serializable
data class Category(
    val id: String = "",
    val name: String = "",
    val boxArtUrl: String? = null,
)

/**
 * The channel's stream info (backend `StreamInfoDto`). Field names mirror the DTO camelCase exactly. The first
 * three fields ([title], [gameName], [tags]) are the editable broadcast metadata the form seeds from; the rest
 * are read-only live context the page surfaces but never writes ([isLive], [viewerCount], [language]). [title]
 * and [gameName] are nullable because a brand-new channel may have neither set yet.
 */
@Serializable
data class StreamInfo(
    val title: String? = null,
    val gameName: String? = null,
    val tags: List<String> = emptyList(),
    val isLive: Boolean = false,
    val viewerCount: Int = 0,
    val language: String? = null,
)

// The stream update request (backend `UpdateStreamRequest`). Every field is nullable: the backend treats null
// as "leave unchanged", so a partial edit only sends the fields that moved. `explicitNulls = false` on the
// shared Json means absent fields are omitted from the wire body, not sent as JSON null. Field names mirror the
// DTO camelCase exactly (ApiClient never renames). [gameName] is the human game name — the backend resolves it
// to a Twitch game id; an empty [tags] list clears the channel's tags.
@Serializable
data class StreamInfoUpdate(
    val title: String? = null,
    val gameName: String? = null,
    val tags: List<String>? = null,
)
