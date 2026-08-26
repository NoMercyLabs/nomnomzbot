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

/**
 * The resource-picker kinds the backend's `IPipelineOptionProvider` registry supplies (S-RICH-PICKERS,
 * mirrors `PipelineActionFieldKind`'s resource-reference members — `Text`/`Number`/`Boolean`/`Enum`/
 * `ResourceId` are NOT picker kinds and stay off this enum). [wireName] is the exact path segment
 * `GET pipelines/options/{kind}` expects and the exact string a backend-discovered [PipelineActionFieldRemote]
 * carries as its `kind`. The step form's [bot.nomnomz.dashboard.feature.pipelines.ui] field dispatcher and this
 * slice's coverage test both enumerate [entries] — a new picker kind added here without a matching render
 * branch fails that guard by name instead of silently falling back to a bare id box.
 */
enum class PickerKind(val wireName: String) {
    DiscordChannel("discord_channel"),
    DiscordRole("discord_role"),
    TwitchUser("twitch_user"),
    Reward("reward"),
    Widget("widget"),
    Voice("voice"),
    SoundClip("sound_clip"),
    Asset("asset"),
    ;

    companion object {
        fun fromWireName(wireName: String): PickerKind? = entries.firstOrNull { it.wireName == wireName }
    }
}

/**
 * One item a resource-picker field can offer (backend `PipelineOption`): the [value] actually stored on the
 * action's parameter, the human [label], the [secondaryText] that genuinely identifies it beyond the label
 * (cost, locale, duration, …), an optional [imageUrl], and — when the source marked it unselectable — the
 * [reason] why. The backend enum `PipelineOptionState` serializes as a bare int (0 = Selectable, 1 =
 * Unavailable) since no string-enum converter is registered API-wide; [isSelectable] instead relies on the
 * documented backend invariant that [reason] is populated if and only if the option is unavailable, so this
 * DTO never has to guess the wire shape of the state enum.
 */
@Serializable
data class PipelineOptionDto(
    val value: String = "",
    val label: String = "",
    val secondaryText: String? = null,
    val imageUrl: String? = null,
    val reason: String? = null,
) {
    val isSelectable: Boolean get() = reason.isNullOrBlank()
}

/**
 * The result of resolving a picker kind's option list (backend `PipelineOptionListResult`).
 * [sourceAvailable] distinguishes a genuinely empty list (the tenant has zero rewards — `sourceAvailable =
 * true`, `items = []`) from a source that could not be read at all (Discord not linked, the guild call failed
 * — `sourceAvailable = false`, [unavailableReason] populated) — the picker renders a DIFFERENT message for
 * each rather than collapsing both into "you have none" (truthful-data rule).
 */
@Serializable
data class PipelineOptionListResultDto(
    val sourceAvailable: Boolean = true,
    val unavailableReason: String? = null,
    val items: List<PipelineOptionDto> = emptyList(),
    val totalCount: Int = 0,
)

/**
 * The option-supply side of the pipeline builder's resource pickers (backend `PipelineOptionsController`,
 * `GET api/v1/pipelines/options/{kind}`). Flat, tenant-scoped by `X-Channel-Id` like every other pipeline
 * read — never nested under `channels/{channelId}` (that controller is a separate, non-channel-route
 * controller on the backend).
 */
interface PipelineOptionsApi {
    /** The option page for one [PickerKind], optionally filtered by [search] (label/secondary-text match). */
    suspend fun getOptions(kind: PickerKind, search: String? = null): ApiResult<PipelineOptionListResultDto>
}

class RestPipelineOptionsApi(private val client: ApiClient) : PipelineOptionsApi {
    override suspend fun getOptions(kind: PickerKind, search: String?): ApiResult<PipelineOptionListResultDto> {
        val query: String = search?.takeIf { it.isNotBlank() }?.let { "?search=${it.encodeQuery()}" }.orEmpty()
        return client.getEnvelope("api/v1/pipelines/options/${kind.wireName}$query")
    }
}
