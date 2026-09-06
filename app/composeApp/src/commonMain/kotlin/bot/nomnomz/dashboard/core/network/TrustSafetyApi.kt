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

// The platform-wide trust & safety desk (GET/POST /api/v1/admin/trust-safety/*, S-ADMIN-8a). Gated
// server-side on `trust-safety:review` — a key of its own, not implied by user:support:view or plain
// tenant access. Every call requires a justification, which the backend records on the IAM audit row.

import kotlinx.serialization.Serializable

/** One tenant this actor's abuse signal was recorded in — a real, persisted detection, never a guess. */
@Serializable
data class CrossTenantAbuseHit(
    val broadcasterId: String,
    val channelName: String,
    val detectionId: String,
    val confidence: String,
    val outcome: String,
    val reason: String,
    val detectedAt: String,
)

/** One actor whose spam-defence detections were recorded in two or more tenants of this deployment. */
@Serializable
data class CrossTenantAbuseSignal(
    val provider: String,
    val subjectPlatformUserId: String,
    val subjectDisplayName: String,
    val tenantCount: Int,
    val detectionCount: Int,
    val hits: List<CrossTenantAbuseHit> = emptyList(),
)

/**
 * One automatic account action the spam-defence engine took on its own, with the evidence that caused
 * it and its current review state. [reversalPreview] is the blast radius an overturn would cause,
 * computed server-side so the operator sees it BEFORE committing to one.
 */
@Serializable
data class TrustSafetyReviewItem(
    val detectionId: String,
    val broadcasterId: String,
    val channelName: String,
    val subjectPlatformUserId: String,
    val subjectDisplayName: String,
    val provider: String,
    val messageText: String,
    val signals: String,
    val confidence: String,
    val outcome: String,
    val reason: String,
    val detectedAt: String,
    val confirmedAt: String? = null,
    val confirmedByUserId: String? = null,
    val overturnedAt: String? = null,
    val overturnedByUserId: String? = null,
    val reversalPreview: String,
)

/** One tenant a prospective network-wide block would touch — real, freshly-computed presence. */
@Serializable
data class NetworkBlockAffectedTenant(val broadcasterId: String, val channelName: String)

/**
 * The counted blast radius of a network-wide block (S-ADMIN-8b), computed fresh — required reading
 * BEFORE the operator can commit to applying one. [tenantCount] is echoed back on apply and re-verified
 * fresh server-side; a stale count fails closed rather than acting on numbers the operator never saw.
 */
@Serializable
data class NetworkBlockPreview(
    val targetUserId: String,
    val targetTwitchUserId: String,
    val targetDisplayName: String? = null,
    val tenantCount: Int,
    val tenants: List<NetworkBlockAffectedTenant> = emptyList(),
)

/**
 * One network-wide block: who applied it, when, why, the blast radius it actually touched, and — once a
 * lift has been attempted — who lifted it, when, why, and whether the lift was actually clean
 * ([liftedAt] is null on a partial outcome; [liftFailedChannelIds] then names what is still actioned).
 */
@Serializable
data class NetworkBlock(
    val id: String,
    val targetUserId: String,
    val targetTwitchUserId: String,
    val targetDisplayName: String? = null,
    val reason: String? = null,
    val justification: String,
    val appliedByPrincipalId: String,
    val appliedAt: String,
    val tenantCount: Int,
    val channelCount: Int,
    val status: String,
    val liftedByPrincipalId: String? = null,
    val liftJustification: String? = null,
    val liftAttemptedAt: String? = null,
    val liftedAt: String? = null,
    val restoredChannelCount: Int = 0,
    val liftFailedChannelIds: List<String> = emptyList(),
)

interface TrustSafetyApi {
    suspend fun getCrossTenantSignals(justification: String): ApiResult<List<CrossTenantAbuseSignal>>

    suspend fun getReviewQueue(
        justification: String,
        page: Int = 1,
        pageSize: Int = 25,
    ): ApiResult<PaginatedEnvelope<TrustSafetyReviewItem>>

    suspend fun confirm(detectionId: String, justification: String): ApiResult<Unit>

    suspend fun overturn(detectionId: String, justification: String): ApiResult<Unit>

    /** The real blast radius a network-wide block against [targetTwitchUserId] would touch. */
    suspend fun previewNetworkBlock(
        targetTwitchUserId: String,
        justification: String,
    ): ApiResult<NetworkBlockPreview>

    /** Applies the block — [confirmedTenantCount] must match a freshly recomputed one or this fails closed. */
    suspend fun applyNetworkBlock(
        targetTwitchUserId: String,
        reason: String?,
        justification: String,
        confirmedTenantCount: Int,
    ): ApiResult<NetworkBlock>

    /** Every network block, newest first. */
    suspend fun listNetworkBlocks(justification: String): ApiResult<List<NetworkBlock>>

    /** Lifts a network block — fully lifted only when every tenant leg actually restores. */
    suspend fun liftNetworkBlock(blockId: String, justification: String): ApiResult<NetworkBlock>
}

/** The wire shape of `POST /admin/trust-safety/network-blocks`. */
@Serializable
private data class ApplyNetworkBlockBody(
    val targetTwitchUserId: String,
    val reason: String?,
    val justification: String,
    val confirmedTenantCount: Int,
)

class TrustSafetyApiImpl(private val client: ApiClient) : TrustSafetyApi {
    override suspend fun getCrossTenantSignals(
        justification: String,
    ): ApiResult<List<CrossTenantAbuseSignal>> =
        client.getEnvelope(
            "api/v1/admin/trust-safety/cross-tenant-signals?justification=${justification.encodeQuery()}",
        )

    override suspend fun getReviewQueue(
        justification: String,
        page: Int,
        pageSize: Int,
    ): ApiResult<PaginatedEnvelope<TrustSafetyReviewItem>> =
        client.getDirect(
            "api/v1/admin/trust-safety/review-queue?justification=${justification.encodeQuery()}" +
                "&page=$page&pageSize=$pageSize",
        )

    override suspend fun confirm(detectionId: String, justification: String): ApiResult<Unit> =
        client.postUnit(
            "api/v1/admin/trust-safety/review-queue/$detectionId/confirm" +
                "?justification=${justification.encodeQuery()}",
        )

    override suspend fun overturn(detectionId: String, justification: String): ApiResult<Unit> =
        client.postUnit(
            "api/v1/admin/trust-safety/review-queue/$detectionId/overturn" +
                "?justification=${justification.encodeQuery()}",
        )

    override suspend fun previewNetworkBlock(
        targetTwitchUserId: String,
        justification: String,
    ): ApiResult<NetworkBlockPreview> =
        client.getEnvelope(
            "api/v1/admin/trust-safety/network-blocks/preview" +
                "?targetTwitchUserId=${targetTwitchUserId.encodeQuery()}" +
                "&justification=${justification.encodeQuery()}",
        )

    override suspend fun applyNetworkBlock(
        targetTwitchUserId: String,
        reason: String?,
        justification: String,
        confirmedTenantCount: Int,
    ): ApiResult<NetworkBlock> =
        client.postEnvelope(
            "api/v1/admin/trust-safety/network-blocks",
            ApplyNetworkBlockBody(targetTwitchUserId, reason, justification, confirmedTenantCount),
        )

    override suspend fun listNetworkBlocks(justification: String): ApiResult<List<NetworkBlock>> =
        client.getEnvelope(
            "api/v1/admin/trust-safety/network-blocks?justification=${justification.encodeQuery()}",
        )

    override suspend fun liftNetworkBlock(
        blockId: String,
        justification: String,
    ): ApiResult<NetworkBlock> =
        client.postEnvelope(
            "api/v1/admin/trust-safety/network-blocks/$blockId/lift" +
                "?justification=${justification.encodeQuery()}",
        )
}
