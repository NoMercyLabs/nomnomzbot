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

// The typed features facade — the channel's opt-in feature flags. Each flag represents a named capability
// (e.g. "tts", "song_requests") that the broadcaster can enable or disable independently. Toggling calls
// POST /features/{featureKey}/toggle. The backend returns no body; any 2xx is success.
//
// Backend routes (FeaturesController, all under /channels/{channelId}/features):
//   GET  .../features                     →  StatusResponseDto<List<FeatureStatusDto>>  (all flags)
//   POST .../features/{featureKey}/toggle →  204 No Content                             (flip one flag)
interface FeaturesApi {
    /** The channel's feature flags — every known feature and whether it is currently enabled. */
    suspend fun list(channelId: String): ApiResult<List<FeatureStatus>>

    /** Flip the [featureKey] flag (enable → disable or vice versa). The backend emits 204 on success. */
    suspend fun toggle(channelId: String, featureKey: String): ApiResult<Unit>
}

class RestFeaturesApi(private val client: ApiClient) : FeaturesApi {
    // StatusResponseDto<List<FeatureStatusDto>> — getEnvelope unwraps `.data` giving the list directly.
    override suspend fun list(channelId: String): ApiResult<List<FeatureStatus>> =
        client.getEnvelope("api/v1/channels/$channelId/features")

    // POST with no request body; the backend toggles and returns 204.
    override suspend fun toggle(channelId: String, featureKey: String): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/features/$featureKey/toggle")
}

/**
 * One feature flag (backend `FeatureStatusDto`). [featureKey] is the machine-stable key (e.g. `"custom_code"`);
 * [label] is the human-readable display name; [description] explains what the feature does;
 * [isEnabled] is the current state; [enabledAt] is the timestamp the flag was turned on (null when off);
 * [requiredScopes] lists the Twitch OAuth scopes the backend needs to activate this feature.
 */
@Serializable
data class FeatureStatus(
    val featureKey: String = "",
    val label: String = "",
    val description: String = "",
    val isEnabled: Boolean = false,
    val enabledAt: String? = null,
    val requiredScopes: List<String> = emptyList(),
)
