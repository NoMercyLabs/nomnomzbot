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

// The typed trust-policy facade (S-OWN23). Trust is its own backend module — the per-channel numbers that
// drive the moderation trust score and the heat decay the escalation ladder reads.
//
// Backend routes (TrustPolicyController):
//   GET /api/v1/channels/{channelId}/trust/policy   →  StatusResponseDto<TrustPolicyDto>   (trust:policy:read)
//   PUT /api/v1/channels/{channelId}/trust/policy   →  StatusResponseDto<TrustPolicyDto>   (trust:policy:manage)
// Both are single-value envelopes, so they read through getEnvelope / putEnvelope.
interface TrustApi {
    /** The channel's trust policy — the backend returns the DEFAULTS (isPinned = false) until it is first saved. */
    suspend fun policy(channelId: String): ApiResult<TrustPolicy>

    /** Replace the whole policy (every value is sent; the backend validates and returns the saved row). */
    suspend fun savePolicy(channelId: String, body: UpdateTrustPolicyBody): ApiResult<TrustPolicy>
}

class RestTrustApi(private val client: ApiClient) : TrustApi {
    override suspend fun policy(channelId: String): ApiResult<TrustPolicy> =
        client.getEnvelope("api/v1/channels/$channelId/trust/policy")

    override suspend fun savePolicy(
        channelId: String,
        body: UpdateTrustPolicyBody,
    ): ApiResult<TrustPolicy> = client.putEnvelope("api/v1/channels/$channelId/trust/policy", body)
}

/**
 * The channel's trust policy (backend `TrustPolicyDto`). The Kotlin defaults are the BACKEND defaults
 * (`Domain/Trust/Entities/TrustPolicy.cs`), so [TRUST_POLICY_DEFAULTS] is what the editor shows as each field's
 * "default" and what a per-field reset restores. [isPinned] is false while the channel has never saved a policy —
 * the editor then says the channel is running on defaults.
 */
@Serializable
data class TrustPolicy(
    val requestCountWeight: Double = 0.25,
    val accountAgeWeight: Double = 0.25,
    val contentAgeWeight: Double = 0.30,
    val contentPopularityWeight: Double = 0.20,
    val requestCountDecay: Double = 0.599,
    val accountAgeDecay: Double = 0.499,
    val contentAgeDecay: Double = 0.999,
    val contentPopularityDecay: Double = 0.0003,
    val notFollowingFactor: Double = 0.75,
    val reputationBoostEnabled: Boolean = true,
    val youTubeQualityPenaltyFactor: Double = 0.75,
    val skipPenalty: Double = 5.0,
    val timeoutPenalty: Double = 10.0,
    val banPenalty: Double = 30.0,
    val untrustedMax: Double = 25.0,
    val lowMax: Double = 50.0,
    val standardMax: Double = 75.0,
    val heatHalfLifeHours: Double = 24.0,
    val heatDeltaBan: Double = 40.0,
    val heatDeltaTimeout: Double = 15.0,
    val heatDeltaReportValidated: Double = 10.0,
    val heatDeltaAutoModDenied: Double = 5.0,
    val heatDeltaFilterHit: Double = 5.0,
    // False while the channel has never saved a policy — every value above is then the shipped default.
    val isPinned: Boolean = false,
)

/** Full-policy upsert (backend `UpdateTrustPolicyRequest`) — every value is replaced; `isPinned` is server-owned. */
@Serializable
data class UpdateTrustPolicyBody(
    val requestCountWeight: Double,
    val accountAgeWeight: Double,
    val contentAgeWeight: Double,
    val contentPopularityWeight: Double,
    val requestCountDecay: Double,
    val accountAgeDecay: Double,
    val contentAgeDecay: Double,
    val contentPopularityDecay: Double,
    val notFollowingFactor: Double,
    val reputationBoostEnabled: Boolean,
    val youTubeQualityPenaltyFactor: Double,
    val skipPenalty: Double,
    val timeoutPenalty: Double,
    val banPenalty: Double,
    val untrustedMax: Double,
    val lowMax: Double,
    val standardMax: Double,
    val heatHalfLifeHours: Double,
    val heatDeltaBan: Double,
    val heatDeltaTimeout: Double,
    val heatDeltaReportValidated: Double,
    val heatDeltaAutoModDenied: Double,
    val heatDeltaFilterHit: Double,
)

/** The shipped defaults — what the editor shows beside each field and what a per-field reset restores. */
val TRUST_POLICY_DEFAULTS: TrustPolicy = TrustPolicy()

/** Every value of [policy] as the upsert body (the PUT replaces the whole policy, never a patch). */
fun TrustPolicy.asUpdateBody(): UpdateTrustPolicyBody =
    UpdateTrustPolicyBody(
        requestCountWeight = requestCountWeight,
        accountAgeWeight = accountAgeWeight,
        contentAgeWeight = contentAgeWeight,
        contentPopularityWeight = contentPopularityWeight,
        requestCountDecay = requestCountDecay,
        accountAgeDecay = accountAgeDecay,
        contentAgeDecay = contentAgeDecay,
        contentPopularityDecay = contentPopularityDecay,
        notFollowingFactor = notFollowingFactor,
        reputationBoostEnabled = reputationBoostEnabled,
        youTubeQualityPenaltyFactor = youTubeQualityPenaltyFactor,
        skipPenalty = skipPenalty,
        timeoutPenalty = timeoutPenalty,
        banPenalty = banPenalty,
        untrustedMax = untrustedMax,
        lowMax = lowMax,
        standardMax = standardMax,
        heatHalfLifeHours = heatHalfLifeHours,
        heatDeltaBan = heatDeltaBan,
        heatDeltaTimeout = heatDeltaTimeout,
        heatDeltaReportValidated = heatDeltaReportValidated,
        heatDeltaAutoModDenied = heatDeltaAutoModDenied,
        heatDeltaFilterHit = heatDeltaFilterHit,
    )
