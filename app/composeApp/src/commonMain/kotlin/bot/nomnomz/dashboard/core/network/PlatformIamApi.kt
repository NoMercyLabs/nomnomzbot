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

// Plane-C platform IAM management client (GET/POST /api/v1/platform/iam/*). The admin panel's IAM screen
// promotes users to platform principals, assigns/revokes granular platform roles, and creates service
// accounts. Every route is gated server-side on iam:manage (create-principal on iam:principal:create); the
// whole admin area is only reachable by an admin, and the backend re-checks the exact grant per call, so a
// caller lacking a specific grant gets a surfaced error rather than a hidden control.

import kotlinx.serialization.Serializable

// ─── DTOs (mirror server/openapi/v1.json — Iam*Dto) ──────────────────────────

/** A platform-IAM role with its permission bundle — the role picker's row (IamRoleDto). */
@Serializable
data class IamRole(
    val id: String,
    val name: String,
    val description: String? = null,
    val isSystem: Boolean = false,
    val permissionKeys: List<String> = emptyList(),
)

/**
 * One principal with its ACTIVE role assignments — the IAM screen's row (IamPrincipalSummaryDto).
 * [principalType] is the backend integer enum: 0 = Employee, 1 = ServiceAccount.
 */
@Serializable
data class IamPrincipalSummary(
    val id: String,
    val principalType: Int = 0,
    val userId: String? = null,
    val name: String,
    val isActive: Boolean = true,
    val expiresAt: String? = null,
    val activeAssignments: List<IamRoleAssignment> = emptyList(),
)

/** A platform-IAM role assignment on a principal (IamRoleAssignmentDto). */
@Serializable
data class IamRoleAssignment(
    val id: String,
    val principalId: String,
    val roleId: String,
    val roleName: String,
    val scopeChannelId: String? = null,
    val expiresAt: String? = null,
    val revokedAt: String? = null,
    val reason: String? = null,
    val createdAt: String,
)

/**
 * A provisioned principal (IamPrincipalDto). [serviceAccountKey] is populated exactly ONCE — on
 * service-account creation — and never returned by any read; the screen shows it once, then only its hash exists.
 */
@Serializable
data class IamPrincipal(
    val id: String,
    val principalType: Int = 0,
    val userId: String? = null,
    val name: String,
    val isActive: Boolean = true,
    val expiresAt: String? = null,
    val serviceAccountKey: String? = null,
)

// ─── Request bodies ──────────────────────────────────────────────────────────

/** Provisions a principal — [principalType] 0 = promote an [userId] employee, 1 = create a service account. */
@Serializable
data class CreatePrincipalBody(
    val principalType: Int,
    val userId: String? = null,
    val displayName: String,
    val roleIds: List<String> = emptyList(),
    val serviceAccountName: String? = null,
)

/** Assigns a role to a principal, optionally tenant-scoped and time-boxed (AssignIamRoleRequest). */
@Serializable
data class AssignRoleBody(
    val principalId: String,
    val roleId: String,
    val scopeChannelId: String? = null,
    val expiresAt: String? = null,
    val reason: String? = null,
)

// ─── API interface + implementation ──────────────────────────────────────────

interface PlatformIamApi {
    suspend fun listRoles(): ApiResult<List<IamRole>>
    suspend fun listPrincipals(): ApiResult<List<IamPrincipalSummary>>
    suspend fun effectivePermissions(principalId: String, scopeChannelId: String? = null): ApiResult<List<String>>
    suspend fun createPrincipal(body: CreatePrincipalBody): ApiResult<IamPrincipal>
    suspend fun deactivatePrincipal(principalId: String, reason: String?): ApiResult<Unit>
    suspend fun reactivatePrincipal(principalId: String): ApiResult<Unit>
    suspend fun assignRole(body: AssignRoleBody): ApiResult<IamRoleAssignment>
    suspend fun revokeAssignment(assignmentId: String, reason: String?): ApiResult<Unit>
}

class PlatformIamApiImpl(private val client: ApiClient) : PlatformIamApi {
    override suspend fun listRoles(): ApiResult<List<IamRole>> =
        client.getEnvelope("api/v1/platform/iam/roles")

    override suspend fun listPrincipals(): ApiResult<List<IamPrincipalSummary>> =
        client.getEnvelope("api/v1/platform/iam/principals")

    override suspend fun effectivePermissions(
        principalId: String,
        scopeChannelId: String?,
    ): ApiResult<List<String>> {
        val query: String = scopeChannelId?.let { "?scopeChannelId=${it.encodeQuery()}" } ?: ""
        return client.getEnvelope("api/v1/platform/iam/principals/$principalId/permissions$query")
    }

    override suspend fun createPrincipal(body: CreatePrincipalBody): ApiResult<IamPrincipal> =
        client.postEnvelope("api/v1/platform/iam/principals", body)

    override suspend fun deactivatePrincipal(principalId: String, reason: String?): ApiResult<Unit> {
        val query: String = reason?.takeIf { it.isNotBlank() }?.let { "?reason=${it.encodeQuery()}" } ?: ""
        return client.postUnit("api/v1/platform/iam/principals/$principalId/deactivate$query")
    }

    override suspend fun reactivatePrincipal(principalId: String): ApiResult<Unit> =
        client.postUnit("api/v1/platform/iam/principals/$principalId/reactivate")

    override suspend fun assignRole(body: AssignRoleBody): ApiResult<IamRoleAssignment> =
        client.postEnvelope("api/v1/platform/iam/assignments", body)

    override suspend fun revokeAssignment(assignmentId: String, reason: String?): ApiResult<Unit> {
        val query: String = reason?.takeIf { it.isNotBlank() }?.let { "?reason=${it.encodeQuery()}" } ?: ""
        return client.deleteUnit("api/v1/platform/iam/assignments/$assignmentId$query")
    }
}
