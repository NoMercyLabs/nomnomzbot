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

@Serializable
data class FeatureFlag(
    val featureKey: String,
    val isEnabled: Boolean,
    val enabledAt: String? = null,
    val requiredScopes: List<String> = emptyList(),
)

@Serializable
data class AdminSetFeatureFlagRequest(
    val key: String,
    val description: String,
    val isEnabledGlobally: Boolean,
    val rolloutPercentage: Int,
    val minTierKey: String? = null,
    val requiresConsent: Boolean,
    val deploymentMode: String,
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

// ─── API interface + implementation ──────────────────────────────────────────

interface AdminApi {
    // Platform stats
    suspend fun getStats(): ApiResult<AdminStats>
    suspend fun getChannels(page: Int = 1, pageSize: Int = 25): ApiResult<PaginatedEnvelope<AdminChannel>>
    suspend fun getUsers(page: Int = 1, pageSize: Int = 25): ApiResult<PaginatedEnvelope<AdminUser>>
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
}

class AdminApiImpl(private val client: ApiClient) : AdminApi {
    override suspend fun getStats(): ApiResult<AdminStats> =
        client.getEnvelope("api/v1/admin/stats")

    override suspend fun getChannels(page: Int, pageSize: Int): ApiResult<PaginatedEnvelope<AdminChannel>> =
        client.getDirect("api/v1/admin/channels?page=$page&pageSize=$pageSize")

    override suspend fun getUsers(page: Int, pageSize: Int): ApiResult<PaginatedEnvelope<AdminUser>> =
        client.getDirect("api/v1/admin/users?page=$page&pageSize=$pageSize")

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
}
