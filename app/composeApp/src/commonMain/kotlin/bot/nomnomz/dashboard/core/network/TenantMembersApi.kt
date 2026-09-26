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

// The people who belong to one tenant (GET /api/v1/admin/tenants/{id}/members, tenant:read) — the act-as target
// picker. An act-as under a support session scoped to that tenant may only target one of these people.

/**
 * One person tied to a tenant (TenantMemberDto). [relation] is `owner`, `manager`, `community` or `viewer`;
 * [managementRole] and [communityStanding] are role NAMES (e.g. `Moderator`, `Vip`), never numbered levels.
 */
@Serializable
data class TenantMember(
    val userId: String,
    val username: String,
    val displayName: String,
    val profileImageUrl: String? = null,
    val relation: String,
    val managementRole: String? = null,
    val communityStanding: String? = null,
)

interface TenantMembersApi {
    suspend fun listMembers(
        broadcasterId: String,
        search: String? = null,
        page: Int = 1,
        pageSize: Int = 25,
    ): ApiResult<PaginatedEnvelope<TenantMember>>
}

class RestTenantMembersApi(private val client: ApiClient) : TenantMembersApi {
    override suspend fun listMembers(
        broadcasterId: String,
        search: String?,
        page: Int,
        pageSize: Int,
    ): ApiResult<PaginatedEnvelope<TenantMember>> {
        val searchPart: String = search?.takeIf { it.isNotBlank() }?.let { "&search=${it.encodeQuery()}" } ?: ""
        val query: String = "?page=$page&take=$pageSize$searchPart"
        return client.getDirect("api/v1/admin/tenants/$broadcasterId/members$query")
    }
}
