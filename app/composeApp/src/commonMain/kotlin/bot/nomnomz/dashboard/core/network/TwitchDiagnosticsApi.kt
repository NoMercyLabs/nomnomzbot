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

// The typed Twitch-diagnostics facade — the dashboard's window onto "which Twitch permissions the streamer
// token is missing" and the one-click additive re-grant.
//
// Backend routes (TwitchDiagnosticsController, Authorization: Bearer):
//   GET  /api/v1/twitch/diagnostics/missing-scopes  →  StatusResponseDto<MissingScopesDto>
//   POST /api/v1/twitch/diagnostics/regrant         →  StatusResponseDto<ScopeRegrantStartDto>
//
// The re-grant returns a Device Code Flow handle requesting (granted ∪ missing); the client shows the user
// code + verification URL and polls the NORMAL streamer device poll (AuthApi.pollDeviceLogin) — on approval the
// widened grant reconciles server-side and the gaps clear. No new poll endpoint, no manual back-fill.
interface TwitchDiagnosticsApi {
    /** The channel's outstanding Twitch scope gaps; an empty list means the token holds everything offered. */
    suspend fun missingScopes(): ApiResult<MissingScopes>

    /** Start the additive re-grant (granted ∪ missing) as a streamer device authorization. */
    suspend fun startRegrant(): ApiResult<ScopeRegrantStart>

    /** This channel's registered EventSub subscriptions (from the registry, no Twitch call). */
    suspend fun subscriptions(channelId: String): ApiResult<List<EventSubSubscription>>

    /** Reconcile this channel's registry against Twitch's actual subscription list. */
    suspend fun reconcile(channelId: String): ApiResult<EventSubReconcileReport>
}

class RestTwitchDiagnosticsApi(private val client: ApiClient) : TwitchDiagnosticsApi {
    override suspend fun missingScopes(): ApiResult<MissingScopes> =
        client.getEnvelope("api/v1/twitch/diagnostics/missing-scopes")

    override suspend fun startRegrant(): ApiResult<ScopeRegrantStart> =
        client.postEnvelope("api/v1/twitch/diagnostics/regrant")

    override suspend fun subscriptions(channelId: String): ApiResult<List<EventSubSubscription>> =
        when (val r: ApiResult<PaginatedEnvelope<EventSubSubscription>> =
            client.getDirect("api/v1/channels/$channelId/eventsub/subscriptions?page=1&pageSize=100")) {
            is ApiResult.Failure -> ApiResult.Failure(r.error)
            is ApiResult.Ok -> ApiResult.Ok(r.value.data)
        }

    override suspend fun reconcile(channelId: String): ApiResult<EventSubReconcileReport> =
        client.postEnvelope("api/v1/channels/$channelId/eventsub/reconcile")
}

/**
 * The channel's outstanding Twitch scope gaps (`MissingScopesDto`). [scopes] is the deduplicated set the
 * streamer token is missing; empty when the connection holds everything every offered feature needs.
 */
@Serializable
data class MissingScopes(
    val connectionStatus: String = "",
    val scopes: List<MissingScope> = emptyList(),
)

/**
 * One missing-scope row (`MissingScopeDto`): the absent scope, the feature(s) it blocks, whether a real Helix
 * call already surfaced it, and whether the streamer was already told in chat.
 */
@Serializable
data class MissingScope(
    val scope: String,
    val features: List<String> = emptyList(),
    val detectedAtRuntime: Boolean = false,
    val chatNotified: Boolean = false,
)

/**
 * A started additive re-grant (`ScopeRegrantStartDto`): the Device Code Flow handle the client shows + polls,
 * plus the exact scope set requested (granted ∪ missing) so the existing grant is never dropped.
 */
@Serializable
data class ScopeRegrantStart(
    val deviceCode: String,
    val userCode: String,
    val verificationUri: String,
    val interval: Int = 5,
    val expiresIn: Int = 1800,
    val requestedScopes: List<String> = emptyList(),
)

/** One registry row (`EventSubSubscriptionDto`): the registered topic, its status, and any last error. */
@Serializable
data class EventSubSubscription(
    val id: String,
    val eventType: String,
    val version: String = "",
    val transport: String = "",
    val status: String,
    val enabled: Boolean,
    val cost: Int? = null,
    val twitchSubscriptionId: String? = null,
    val lastError: String? = null,
    val createdAt: String = "",
)

/** The outcome of a registry reconcile pass (`EventSubReconcileReportDto`). */
@Serializable
data class EventSubReconcileReport(
    val created: Int = 0,
    val revoked: Int = 0,
    val repaired: Int = 0,
    val unchanged: Int = 0,
    val errors: List<String> = emptyList(),
)
