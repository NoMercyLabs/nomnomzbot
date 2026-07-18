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

// Plane-C privileged tenant operations + audit search (GET/POST /api/v1/admin/tenants*, /admin/audit). The
// admin panel's tenant console: suspend/reinstate a tenant (both enforced by the bot lifecycle + tenant
// resolution — a suspended tenant's API surface 403s at Gate 1), audited support access, and the Plane-C
// audit log. Gated server-side on tenant:read / tenant:suspend / tenant:access / audit:read; the whole admin
// area is admin-only and the backend re-checks per call, so a missing grant surfaces as an error.

import kotlinx.serialization.Serializable

// ─── DTOs (mirror server/openapi/v1.json — AdminTenant*/TenantAccessGrant/IamAuditEntry) ─────

/** One tenant row for the operator console (AdminTenantDto). [status] = active|suspended|platform_banned. */
@Serializable
data class AdminTenant(
    val id: String,
    val name: String,
    val twitchChannelId: String,
    val status: String,
    val billingTierKey: String,
    val isLive: Boolean = false,
    val createdAt: String,
    val suspendedAt: String? = null,
)

/** Tenant detail — status, tier, owner, membership count (AdminTenantDetailDto). */
@Serializable
data class AdminTenantDetail(
    val id: String,
    val name: String,
    val twitchChannelId: String,
    val status: String,
    val suspendedReason: String? = null,
    val billingTierKey: String,
    val deploymentMode: String,
    val ownerUserId: String,
    val ownerDisplayName: String,
    val membershipCount: Int = 0,
    val createdAt: String,
    val suspendedAt: String? = null,
)

/** An audited support-access grant — a time-boxed, tenant-narrowed role assignment (TenantAccessGrantDto). */
@Serializable
data class TenantAccessGrant(
    val id: String,
    val principalId: String,
    val targetBroadcasterId: String,
    val justification: String,
    val breakGlass: Boolean = false,
    val grantedAt: String,
    val expiresAt: String? = null,
    val revokedAt: String? = null,
)

/** One Plane-C audit row for the operator console (IamAuditEntryDto). */
@Serializable
data class IamAuditEntry(
    val id: Long,
    val principalId: String,
    val principalType: String,
    val permission: String,
    val targetBroadcasterId: String? = null,
    val targetResource: String? = null,
    val justification: String? = null,
    val breakGlass: Boolean = false,
    val outcome: String,
    val occurredAt: String,
)

// ─── Request bodies ──────────────────────────────────────────────────────────

/** Suspends a tenant — [newStatus] is `suspended` | `platform_banned` (SuspendTenantRequest). */
@Serializable
data class SuspendTenantBody(val newStatus: String, val reason: String)

/** Reinstates a suspended tenant (ReinstateTenantRequest). */
@Serializable
data class ReinstateTenantBody(val justification: String)

/** Begins audited support access to one tenant (BeginTenantAccessRequest). */
@Serializable
data class BeginTenantAccessBody(
    val justification: String,
    val breakGlass: Boolean = false,
    val expiresAt: String? = null,
)

// ─── API interface + implementation ──────────────────────────────────────────

interface PlatformAdminApi {
    suspend fun listTenants(
        search: String? = null,
        status: String? = null,
        isLive: Boolean? = null,
        page: Int = 1,
        pageSize: Int = 25,
    ): ApiResult<PaginatedEnvelope<AdminTenant>>

    suspend fun getTenant(broadcasterId: String): ApiResult<AdminTenantDetail>
    suspend fun suspendTenant(broadcasterId: String, body: SuspendTenantBody): ApiResult<Unit>
    suspend fun reinstateTenant(broadcasterId: String, body: ReinstateTenantBody): ApiResult<Unit>
    suspend fun beginAccess(broadcasterId: String, body: BeginTenantAccessBody): ApiResult<TenantAccessGrant>
    suspend fun endAccess(accessGrantId: String): ApiResult<Unit>

    suspend fun searchAudit(
        principalId: String? = null,
        targetBroadcasterId: String? = null,
        permission: String? = null,
        outcome: String? = null,
        from: String? = null,
        to: String? = null,
        page: Int = 1,
        pageSize: Int = 25,
    ): ApiResult<PaginatedEnvelope<IamAuditEntry>>
}

class PlatformAdminApiImpl(private val client: ApiClient) : PlatformAdminApi {
    override suspend fun listTenants(
        search: String?,
        status: String?,
        isLive: Boolean?,
        page: Int,
        pageSize: Int,
    ): ApiResult<PaginatedEnvelope<AdminTenant>> {
        val query: String = buildQuery(
            "page" to page.toString(),
            "take" to pageSize.toString(),
            "search" to search?.takeIf { it.isNotBlank() },
            "status" to status?.takeIf { it.isNotBlank() },
            "isLive" to isLive?.toString(),
        )
        return client.getDirect("api/v1/admin/tenants$query")
    }

    override suspend fun getTenant(broadcasterId: String): ApiResult<AdminTenantDetail> =
        client.getEnvelope("api/v1/admin/tenants/$broadcasterId")

    override suspend fun suspendTenant(broadcasterId: String, body: SuspendTenantBody): ApiResult<Unit> =
        client.postUnit("api/v1/admin/tenants/$broadcasterId/suspend", body)

    override suspend fun reinstateTenant(broadcasterId: String, body: ReinstateTenantBody): ApiResult<Unit> =
        client.postUnit("api/v1/admin/tenants/$broadcasterId/reinstate", body)

    override suspend fun beginAccess(broadcasterId: String, body: BeginTenantAccessBody): ApiResult<TenantAccessGrant> =
        client.postEnvelope("api/v1/admin/tenants/$broadcasterId/access", body)

    override suspend fun endAccess(accessGrantId: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/admin/access/$accessGrantId")

    override suspend fun searchAudit(
        principalId: String?,
        targetBroadcasterId: String?,
        permission: String?,
        outcome: String?,
        from: String?,
        to: String?,
        page: Int,
        pageSize: Int,
    ): ApiResult<PaginatedEnvelope<IamAuditEntry>> {
        val query: String = buildQuery(
            "page" to page.toString(),
            "take" to pageSize.toString(),
            "principalId" to principalId?.takeIf { it.isNotBlank() },
            "targetBroadcasterId" to targetBroadcasterId?.takeIf { it.isNotBlank() },
            "permission" to permission?.takeIf { it.isNotBlank() },
            "outcome" to outcome?.takeIf { it.isNotBlank() },
            "from" to from?.takeIf { it.isNotBlank() },
            "to" to to?.takeIf { it.isNotBlank() },
        )
        return client.getDirect("api/v1/admin/audit$query")
    }

    /** Builds a `?a=1&b=2` query string from non-null pairs, percent-encoding each value. */
    private fun buildQuery(vararg params: Pair<String, String?>): String {
        val parts: List<String> = params.mapNotNull { (key, value) ->
            value?.let { "$key=${it.encodeQuery()}" }
        }
        return if (parts.isEmpty()) "" else "?" + parts.joinToString("&")
    }
}
