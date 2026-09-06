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

// Cross-tenant support desk (GET /api/v1/admin/support/*). Gated server-side on `user:support:view` —
// a key of its own, NOT implied by tenant:access or user:impersonate. Both calls require a justification,
// which the backend records on the audit row together with the subject.

import kotlinx.serialization.Serializable

/** One person matched across every tenant. [tenantCount] is how many channels the person is known in. */
@Serializable
data class SupportPersonSearchResult(
    val userId: String,
    val username: String,
    val displayName: String,
    val platform: String,
    val twitchUserId: String? = null,
    val isPlatformPrincipal: Boolean = false,
    val isAnonymized: Boolean = false,
    val lastSeenAt: String? = null,
    val tenantCount: Int = 0,
)

/** One proven external login the person holds. */
@Serializable
data class SupportPersonIdentity(
    val provider: String,
    val providerUserId: String,
    val providerUsername: String,
    val isPrimary: Boolean = false,
    val linkedAt: String,
    val lastLoginAt: String? = null,
)

/** One streaming-platform connection on a channel this person owns. */
@Serializable
data class SupportPersonPlatformConnection(
    val broadcasterId: String,
    val channelName: String,
    val provider: String,
    val externalChannelId: String,
    val displayName: String,
    val isPrimary: Boolean = false,
    val isLive: Boolean = false,
)

/** One live platform-IAM role assignment, platform-wide or narrowed to [scopeChannelName]. */
@Serializable
data class SupportPersonIamRole(
    val principalId: String,
    val principalName: String,
    val roleName: String,
    val scopeBroadcasterId: String? = null,
    val scopeChannelName: String? = null,
    val expiresAt: String? = null,
)

/** The person's positive community standing in ONE channel. */
@Serializable
data class SupportPersonCommunityStanding(
    val broadcasterId: String,
    val channelName: String,
    val standing: String,
    val levelValue: Int = 0,
    val source: String,
    val subTier: String? = null,
    val lastSeenAt: String? = null,
)

/** The bot-side negative moderation standing the person carries in ONE channel on ONE platform. */
@Serializable
data class SupportPersonModerationStanding(
    val broadcasterId: String,
    val channelName: String,
    val provider: String,
    val standing: String,
    val reason: String? = null,
    val createdAt: String,
)

/** The person's moderation rollup in ONE channel. */
@Serializable
data class SupportPersonModerationHistory(
    val broadcasterId: String,
    val channelName: String,
    val timeoutCount: Int = 0,
    val banCount: Int = 0,
    val warningCount: Int = 0,
    val messagesDeletedCount: Int = 0,
    val lastActionAt: String? = null,
    val lastActionType: String? = null,
)

/** The person's trust + heat projection in ONE channel. */
@Serializable
data class SupportPersonTrustScore(
    val broadcasterId: String,
    val channelName: String,
    val trustScore: Double,
    val heatScore: Double,
    val lastHeatEventAt: String? = null,
    val computedAt: String,
)

/** One live operator comp on a channel the person owns. */
@Serializable
data class SupportPersonEntitlementGrant(
    val grantId: String,
    val grantedTierId: String,
    val reason: String,
    val issuedAt: String,
    val expiresAt: String,
)

/** The effective entitlement of ONE channel the person owns, plus the live comps behind it. */
@Serializable
data class SupportPersonEntitlement(
    val broadcasterId: String,
    val channelName: String,
    val tierKey: String,
    val grants: List<SupportPersonEntitlementGrant> = emptyList(),
)

/**
 * ONE view of a person's real state across every tenant. Every list is sourced from its own backend table
 * and every per-tenant fact names the tenant it belongs to. A list is EMPTY when the system genuinely holds
 * no such fact for this person — the UI must omit that block rather than render a zeroed placeholder.
 */
@Serializable
data class SupportPersonView(
    val userId: String,
    val username: String,
    val displayName: String,
    val platform: String,
    val twitchUserId: String? = null,
    val isPlatformPrincipal: Boolean = false,
    val isAnonymized: Boolean = false,
    val lastSeenAt: String? = null,
    val identities: List<SupportPersonIdentity> = emptyList(),
    val platformConnections: List<SupportPersonPlatformConnection> = emptyList(),
    val iamRoles: List<SupportPersonIamRole> = emptyList(),
    val communityStandings: List<SupportPersonCommunityStanding> = emptyList(),
    val moderationStandings: List<SupportPersonModerationStanding> = emptyList(),
    val moderationHistory: List<SupportPersonModerationHistory> = emptyList(),
    val trustScores: List<SupportPersonTrustScore> = emptyList(),
    val entitlements: List<SupportPersonEntitlement> = emptyList(),
)

interface AdminSupportApi {
    suspend fun searchPeople(
        search: String,
        justification: String,
        page: Int = 1,
        pageSize: Int = 25,
    ): ApiResult<PaginatedEnvelope<SupportPersonSearchResult>>

    suspend fun getPerson(
        subjectUserId: String,
        justification: String,
    ): ApiResult<SupportPersonView>
}

class AdminSupportApiImpl(private val client: ApiClient) : AdminSupportApi {
    override suspend fun searchPeople(
        search: String,
        justification: String,
        page: Int,
        pageSize: Int,
    ): ApiResult<PaginatedEnvelope<SupportPersonSearchResult>> =
        client.getDirect(
            "api/v1/admin/support/people?search=${search.encodeQuery()}" +
                "&justification=${justification.encodeQuery()}&page=$page&pageSize=$pageSize",
        )

    override suspend fun getPerson(
        subjectUserId: String,
        justification: String,
    ): ApiResult<SupportPersonView> =
        client.getEnvelope(
            "api/v1/admin/support/people/$subjectUserId?justification=${justification.encodeQuery()}",
        )
}
