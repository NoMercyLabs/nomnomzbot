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

// Platform-admin REST client (GET /api/v1/admin/*, /api/v1/admin/billing/*, /api/v1/admin/feature-flags).
// All endpoints are gated on platform:admin (isAdmin == true on CurrentUser); callers must verify before
// showing the Admin area — the backend re-checks on every call.

import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable

// ─── DTOs ────────────────────────────────────────────────────────────────────

@Serializable
data class AdminStats(
    val totalChannels: Int,
    val activeChannels: Int,
    val totalUsers: Int,
    val systemStatus: String,
    val botUptimeSeconds: Long,
    val eventsProcessedToday: Int,
)

@Serializable
data class AdminChannel(
    val id: String,
    val displayName: String,
    val login: String,
    val isLive: Boolean,
    val isActive: Boolean,
    val viewerCount: Int,
    val plan: String,
    val createdAt: String,
)

@Serializable
data class AdminUser(
    val id: String,
    val displayName: String,
    val login: String,
    val email: String? = null,
    val role: String,
    val channelCount: Int,
    val createdAt: String,
    val lastActive: String? = null,
)

@Serializable
data class AdminServiceHealth(
    val name: String,
    val status: String,
)

@Serializable
data class AdminSystem(
    val overall: String,
    val services: List<AdminServiceHealth>,
    val botVersion: String,
    val memoryUsageMb: Long,
    val cpuPercent: Double,
)

@Serializable
data class PlatformEvent(
    val message: String,
    val time: String,
    val type: String,
)

// ─── Feature Flags ───────────────────────────────────────────────────────────

/**
 * A staged-rollout feature flag's global definition (backend `FeatureFlagDto`). [requiresConsent] is a consent
 * TYPE key the tenant must hold for the flag to apply (or null), not a boolean — the flag can require a specific
 * consent grant, not merely "some consent". [deploymentMode] is `saas` | `self_host` | null (both).
 */
@Serializable
data class FeatureFlag(
    val key: String = "",
    val description: String? = null,
    val isEnabledGlobally: Boolean = false,
    val rolloutPercentage: Int = 0,
    val minTierKey: String? = null,
    val requiresConsent: String? = null,
    val deploymentMode: String? = null,
)

@Serializable
data class AdminSetFeatureFlagRequest(
    val key: String,
    val description: String? = null,
    val isEnabledGlobally: Boolean,
    val rolloutPercentage: Int,
    val minTierKey: String? = null,
    val requiresConsent: String? = null,
    val deploymentMode: String? = null,
)

@Serializable
data class AdminSetFeatureFlagOverrideRequest(
    val isEnabled: Boolean,
    val reason: String? = null,
    val expiresAt: String? = null,
)

// ─── Admin Billing ───────────────────────────────────────────────────────────

@Serializable
data class InviteCode(
    val id: String,
    val code: String,
    val maxRedemptions: Int,
    val redemptionCount: Int,
    val grantsFoundersBadge: Boolean,
    val grantsTierId: String? = null,
    val grantsTierKey: String? = null,
    val expiresAt: String? = null,
)

@Serializable
data class AdminCreateInviteCodeRequest(
    val maxRedemptions: Int,
    @SerialName("grantsFoundersBadge") val grantsFoundersBadge: Boolean,
    @SerialName("grantsTierId") val grantsTierId: String? = null,
    val expiresAt: String? = null,
)

@Serializable
data class AdminGrantTierRequest(
    @SerialName("tierId") val tierId: String,
    val isInviteOnlyGrant: Boolean,
)

// ─── Impersonation (admin act-as) ────────────────────────────────────────────

/**
 * The minted act-as session — the server `ImpersonationTokenDto`. Minting REQUIRES an already-open,
 * audited support session (a [TenantAccessGrant]); the token's [expiresAt] is clamped to that grant's
 * remaining time server-side. [sessionId] is what [AdminApi.endImpersonation] ends the session with.
 * [user] is the backend `UserDto`; reuses [UserSearchResult] (the contract-guarded `UserDto` map) rather
 * than [AdminUser] — the latter's required `login`/`role`/`channelCount` are absent from `UserDto` and
 * would fail deserialization. Only [UserSearchResult.displayName] is needed here (the "Acting as …" banner).
 */
@Serializable
data class ImpersonationTokenDto(
    val accessToken: String,
    val expiresAt: String,
    val sessionId: String,
    val user: UserSearchResult,
)

/** Request body for [AdminApi.impersonate] — the open support session this act-as token is minted under, plus
 * the mandatory, audited [justification] the server records for it. */
@Serializable
data class ImpersonateUserRequest(val accessGrantId: String, val justification: String)

// ─── API interface + implementation ──────────────────────────────────────────

interface AdminApi {
    // Platform stats
    suspend fun getStats(): ApiResult<AdminStats>
    suspend fun getChannels(
        search: String? = null,
        page: Int = 1,
        pageSize: Int = 25,
        sort: String? = null,
        isLive: Boolean? = null,
    ): ApiResult<PaginatedEnvelope<AdminChannel>>
    suspend fun getUsers(
        search: String? = null,
        page: Int = 1,
        pageSize: Int = 25,
        sort: String? = null,
        role: String? = null,
    ): ApiResult<PaginatedEnvelope<AdminUser>>
    suspend fun getSystem(): ApiResult<AdminSystem>
    suspend fun getHealth(): ApiResult<List<AdminServiceHealth>>
    suspend fun getEvents(): ApiResult<List<PlatformEvent>>

    // Feature flags
    suspend fun getFeatureFlags(): ApiResult<List<FeatureFlag>>
    suspend fun setFeatureFlag(body: AdminSetFeatureFlagRequest): ApiResult<FeatureFlag>
    suspend fun setFeatureFlagOverride(flagKey: String, broadcasterId: String, body: AdminSetFeatureFlagOverrideRequest): ApiResult<Unit>
    suspend fun deleteFeatureFlagOverride(flagKey: String, broadcasterId: String): ApiResult<Unit>

    // Admin billing
    suspend fun getInviteCodes(page: Int = 1, pageSize: Int = 25): ApiResult<PaginatedEnvelope<InviteCode>>
    suspend fun createInviteCode(body: AdminCreateInviteCodeRequest): ApiResult<InviteCode>
    suspend fun revokeInviteCode(inviteCodeId: String): ApiResult<Unit>
    suspend fun grantTier(broadcasterId: String, body: AdminGrantTierRequest): ApiResult<Unit>
    suspend fun grantFounderBadge(broadcasterId: String): ApiResult<Unit>

    // Impersonation (admin act-as)
    /** Mints an act-as token for [subjectUserId], scoped to the already-open [accessGrantId] support session,
     * carrying the mandatory, audited [justification]. */
    suspend fun impersonate(subjectUserId: String, accessGrantId: String, justification: String): ApiResult<ImpersonationTokenDto>

    /** Ends the act-as session — the minted token is revoked server-side and stops authenticating immediately. */
    suspend fun endImpersonation(accessGrantId: String): ApiResult<Unit>

    // Provider app credentials (platform OAuth apps)
    /** Every provider's credential state. No secret is ever returned — only whether one exists and its source. */
    suspend fun getProviderCredentials(): ApiResult<List<ProviderCredential>>

    /** Stores a client id and/or secret. A blank field is left untouched; clearing is [clearProviderCredential]. */
    suspend fun saveProviderCredential(
        provider: String,
        body: SaveProviderCredentialBody,
    ): ApiResult<ProviderCredential>

    /** Removes the stored rows so the environment resolves again. Destructive, and deliberately separate. */
    suspend fun clearProviderCredential(provider: String): ApiResult<ProviderCredential>
}

/**
 * One provider's app-credential state.
 *
 * [clientId] is the RESOLVED id — what the OAuth flows will actually send — and is safe to show: it appears
 * in every OAuth URL a viewer's browser already sees. There is no secret field, by design; [secretSource]
 * says only whether one exists and which source wins.
 */
@Serializable
data class ProviderCredential(
    val provider: String = "",
    val clientId: String? = null,
    val clientIdSource: String = "unset",
    val secretSource: String = "unset",
    val appDecisionRecorded: Boolean = false,
    val supported: Boolean = true,
)

@Serializable
data class SaveProviderCredentialBody(
    val clientId: String? = null,
    val clientSecret: String? = null,
)

class AdminApiImpl(private val client: ApiClient) : AdminApi {
    override suspend fun getStats(): ApiResult<AdminStats> =
        client.getEnvelope("api/v1/admin/stats")

    override suspend fun getChannels(
        search: String?,
        page: Int,
        pageSize: Int,
        sort: String?,
        isLive: Boolean?,
    ): ApiResult<PaginatedEnvelope<AdminChannel>> =
        client.getDirect(
            "api/v1/admin/channels?page=$page&pageSize=$pageSize" +
                searchQuery(search) +
                sortQuery(sort) +
                (isLive?.let { "&isLive=$it" } ?: "")
        )

    override suspend fun getUsers(
        search: String?,
        page: Int,
        pageSize: Int,
        sort: String?,
        role: String?,
    ): ApiResult<PaginatedEnvelope<AdminUser>> =
        client.getDirect(
            "api/v1/admin/users?page=$page&pageSize=$pageSize" +
                searchQuery(search) +
                sortQuery(sort) +
                (role?.takeIf { it.isNotBlank() }?.let { "&role=${it.encodeQuery()}" } ?: "")
        )

    override suspend fun getSystem(): ApiResult<AdminSystem> =
        client.getEnvelope("api/v1/admin/system")

    override suspend fun getHealth(): ApiResult<List<AdminServiceHealth>> =
        client.getEnvelope("api/v1/admin/health")

    override suspend fun getEvents(): ApiResult<List<PlatformEvent>> =
        client.getEnvelope("api/v1/admin/events")

    override suspend fun getFeatureFlags(): ApiResult<List<FeatureFlag>> =
        client.getEnvelope("api/v1/admin/feature-flags")

    override suspend fun setFeatureFlag(body: AdminSetFeatureFlagRequest): ApiResult<FeatureFlag> =
        client.putEnvelope("api/v1/admin/feature-flags", body)

    override suspend fun setFeatureFlagOverride(
        flagKey: String,
        broadcasterId: String,
        body: AdminSetFeatureFlagOverrideRequest,
    ): ApiResult<Unit> =
        client.putUnit("api/v1/admin/feature-flags/$flagKey/overrides/$broadcasterId", body)

    override suspend fun deleteFeatureFlagOverride(flagKey: String, broadcasterId: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/admin/feature-flags/$flagKey/overrides/$broadcasterId")

    override suspend fun getInviteCodes(page: Int, pageSize: Int): ApiResult<PaginatedEnvelope<InviteCode>> =
        client.getDirect("api/v1/admin/billing/invites?page=$page&pageSize=$pageSize")

    override suspend fun createInviteCode(body: AdminCreateInviteCodeRequest): ApiResult<InviteCode> =
        client.postEnvelope("api/v1/admin/billing/invites", body)

    override suspend fun revokeInviteCode(inviteCodeId: String): ApiResult<Unit> =
        client.postUnit("api/v1/admin/billing/invites/$inviteCodeId/revoke")

    override suspend fun grantTier(broadcasterId: String, body: AdminGrantTierRequest): ApiResult<Unit> =
        client.postUnit("api/v1/admin/billing/channels/$broadcasterId/grant-tier", body)

    override suspend fun grantFounderBadge(broadcasterId: String): ApiResult<Unit> =
        client.postUnit("api/v1/admin/billing/channels/$broadcasterId/grant-founder")

    override suspend fun impersonate(subjectUserId: String, accessGrantId: String, justification: String): ApiResult<ImpersonationTokenDto> =
        client.postEnvelope(
            "api/v1/admin/users/$subjectUserId/impersonate",
            ImpersonateUserRequest(accessGrantId = accessGrantId, justification = justification),
        )

    override suspend fun endImpersonation(accessGrantId: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/admin/impersonation/$accessGrantId")

    override suspend fun getProviderCredentials(): ApiResult<List<ProviderCredential>> =
        client.getEnvelope("api/v1/admin/providers")

    override suspend fun saveProviderCredential(
        provider: String,
        body: SaveProviderCredentialBody,
    ): ApiResult<ProviderCredential> = client.putEnvelope("api/v1/admin/providers/$provider", body)

    override suspend fun clearProviderCredential(provider: String): ApiResult<ProviderCredential> =
        client.deleteEnvelope("api/v1/admin/providers/$provider")

    private fun searchQuery(search: String?): String =
        search?.takeIf { it.isNotBlank() }?.let { "&search=${it.encodeQuery()}" } ?: ""

    /** The server binds ordering from `sort`; an unknown value falls back to its default rather than failing. */
    private fun sortQuery(sort: String?): String =
        sort?.takeIf { it.isNotBlank() }?.let { "&sort=${it.encodeQuery()}" } ?: ""
}
