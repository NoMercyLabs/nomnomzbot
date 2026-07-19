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

// A focused facade over the per-channel settings routes (`/channels/{channelId}/settings/*`), kept separate
// from [ChannelsApi] (which owns the channel lifecycle + roster) on purpose: a new channel setting adds a
// method HERE without touching the twenty-plus test fakes that implement [ChannelsApi]. Today it carries the
// built-in-command personality tone; the onboarding "basics" (prefix / language / timezone) land here next.
//
// Backend (ChannelsController):
//   GET /api/v1/channels/{channelId}/settings/personality → StatusResponseDto<ChannelPersonalityDto>
//   PUT /api/v1/channels/{channelId}/settings/personality → StatusResponseDto<ChannelPersonalityDto>
interface ChannelSettingsApi {
    /** The channel's current built-in-command personality tone plus every selectable tone (for a picker). */
    suspend fun getPersonality(channelId: String): ApiResult<ChannelPersonality>

    /** Set the channel's personality [tone] (one of [ChannelPersonality.available]); echoes the saved value. */
    suspend fun setPersonality(channelId: String, tone: String): ApiResult<ChannelPersonality>

    /** The channel's onboarding basics — command prefix, default locale, auto-join, timezone. */
    suspend fun getBasics(channelId: String): ApiResult<ChannelBasics>

    /** Save the channel's basics ([body]); only non-null fields change. Echoes the saved values. */
    suspend fun updateBasics(channelId: String, body: UpdateBasicsBody): ApiResult<ChannelBasics>
}

class RestChannelSettingsApi(private val client: ApiClient) : ChannelSettingsApi {
    override suspend fun getPersonality(channelId: String): ApiResult<ChannelPersonality> =
        client.getEnvelope("api/v1/channels/$channelId/settings/personality")

    override suspend fun setPersonality(channelId: String, tone: String): ApiResult<ChannelPersonality> =
        client.putEnvelope(
            "api/v1/channels/$channelId/settings/personality",
            SetPersonalityBody(personality = tone),
        )

    override suspend fun getBasics(channelId: String): ApiResult<ChannelBasics> =
        client.getEnvelope("api/v1/channels/$channelId/settings/basics")

    override suspend fun updateBasics(channelId: String, body: UpdateBasicsBody): ApiResult<ChannelBasics> =
        client.putEnvelope("api/v1/channels/$channelId/settings/basics", body)
}

/**
 * A channel's built-in-command personality tone (`ChannelPersonalityDto`). [personality] is the canonical tone
 * token (`informative` / `friendly` / `sassy` / `hype` / `chill`); [available] lists every selectable tone in
 * catalogue order, so a picker needs no second call. Defaults to the backend's default tone (`informative`).
 */
@Serializable
data class ChannelPersonality(
    val personality: String = "informative",
    val available: List<String> = emptyList(),
)

/** Body for setting a channel's personality tone (`SetChannelPersonalityRequest`). */
@Serializable
data class SetPersonalityBody(val personality: String)

/**
 * A channel's onboarding "basics" (`ChannelBasicsDto`): the command [prefix] (e.g. `!`), the default [locale]
 * (BCP-47, nullable), the [autoJoin] toggle, and the streamer's [timezone] (IANA, nullable). Prefills the
 * Settings "Bot basics" card and the onboarding basics step.
 */
@Serializable
data class ChannelBasics(
    val prefix: String = "!",
    val locale: String? = null,
    val autoJoin: Boolean = true,
    val timezone: String? = null,
)

/**
 * Body for updating a channel's basics (`UpdateChannelSettingsDto`). Every field is nullable — only the
 * non-null ones are applied server-side, so a partial save (e.g. prefix only) leaves the rest untouched.
 */
@Serializable
data class UpdateBasicsBody(
    val prefix: String? = null,
    val locale: String? = null,
    val autoJoin: Boolean? = null,
    val timezone: String? = null,
)
