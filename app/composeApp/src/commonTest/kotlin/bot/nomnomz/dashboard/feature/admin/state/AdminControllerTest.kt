// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.admin.state

import bot.nomnomz.dashboard.core.network.AdminApi
import bot.nomnomz.dashboard.core.network.AdminChannel
import bot.nomnomz.dashboard.core.network.AdminCreateInviteCodeRequest
import bot.nomnomz.dashboard.core.network.AdminGrantTierRequest
import bot.nomnomz.dashboard.core.network.AdminServiceHealth
import bot.nomnomz.dashboard.core.network.AdminSetFeatureFlagOverrideRequest
import bot.nomnomz.dashboard.core.network.AdminSetFeatureFlagRequest
import bot.nomnomz.dashboard.core.network.AdminStats
import bot.nomnomz.dashboard.core.network.AdminSystem
import bot.nomnomz.dashboard.core.network.AdminTenant
import bot.nomnomz.dashboard.core.network.AdminTenantDetail
import bot.nomnomz.dashboard.core.network.AdminUser
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.AssignRoleBody
import bot.nomnomz.dashboard.core.network.BeginTenantAccessBody
import bot.nomnomz.dashboard.core.network.CreatePrincipalBody
import bot.nomnomz.dashboard.core.network.FeatureFlag
import bot.nomnomz.dashboard.core.network.IamAuditEntry
import bot.nomnomz.dashboard.core.network.IamPrincipal
import bot.nomnomz.dashboard.core.network.IamPrincipalSummary
import bot.nomnomz.dashboard.core.network.IamRole
import bot.nomnomz.dashboard.core.network.IamRoleAssignment
import bot.nomnomz.dashboard.core.network.ImpersonationTokenDto
import bot.nomnomz.dashboard.core.network.InviteCode
import bot.nomnomz.dashboard.core.network.PaginatedEnvelope
import bot.nomnomz.dashboard.core.network.PlatformAdminApi
import bot.nomnomz.dashboard.core.network.PlatformEvent
import bot.nomnomz.dashboard.core.network.PlatformIamApi
import bot.nomnomz.dashboard.core.network.ReinstateTenantBody
import bot.nomnomz.dashboard.core.network.SuspendTenantBody
import bot.nomnomz.dashboard.core.network.TenantAccessGrant
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertFalse
import kotlin.test.assertTrue

// Proves the S-OWN08-SPINNER fix: AdminState.loadingSections tracks loading PER TAB, so a slow fetch on
// one tab (Users) never marks a sibling tab (Overview) as loading too — the whole-screen spinner AdminScreen
// used to render off a single AdminState.isLoading flag is gone, and the per-tab flags this test proves are
// what AdminScreen now reads instead.
class AdminControllerTest {

    @Test
    fun loading_one_tab_does_not_mark_other_tabs_as_loading() = runTest {
        val usersGate = CompletableDeferred<Unit>()
        val api = LoadingFakeAdminApi(usersGate = usersGate)
        val controller = AdminController(
            api = api,
            iamApi = LoadingFakePlatformIamApi(),
            platformAdminApi = LoadingFakePlatformAdminApi(),
        )

        // Seed a completed initial load so loadingSections starts empty, then trigger a Users-only refresh
        // (e.g. a search) that hangs on the network — this is the "one tab's data is refreshing" scenario.
        controller.load()
        val refresh = launch { controller.loadUsers(search = "someone") }
        runCurrent()

        // While the Users fetch is still in flight, Users must be the ONLY section marked loading — Overview
        // (and every other tab fed by load()) must NOT be marked loading just because a sibling tab is busy.
        assertTrue(AdminSection.Users in controller.state.value.loadingSections)
        assertFalse(AdminSection.Overview in controller.state.value.loadingSections)
        assertFalse(AdminSection.Channels in controller.state.value.loadingSections)
        assertFalse(AdminSection.System in controller.state.value.loadingSections)
        assertFalse(AdminSection.FeatureFlags in controller.state.value.loadingSections)
        assertFalse(AdminSection.Billing in controller.state.value.loadingSections)

        usersGate.complete(Unit)
        refresh.join()

        // Settles cleanly once the fetch resolves.
        assertFalse(AdminSection.Users in controller.state.value.loadingSections)
    }
}

// ─── Fakes ─────────────────────────────────────────────────────────────────

private class LoadingFakeAdminApi(private val usersGate: CompletableDeferred<Unit>? = null) : AdminApi {
    override suspend fun getStats(): ApiResult<AdminStats> = ApiResult.Ok(AdminStats(0, 0, 0, "ok", 0, 0))

    override suspend fun getChannels(search: String?, page: Int, pageSize: Int, sort: String?, isLive: Boolean?): ApiResult<PaginatedEnvelope<AdminChannel>> =
        ApiResult.Ok(PaginatedEnvelope(emptyList()))

    override suspend fun getUsers(search: String?, page: Int, pageSize: Int, sort: String?, role: String?): ApiResult<PaginatedEnvelope<AdminUser>> {
        if (search != null) usersGate?.await()
        return ApiResult.Ok(PaginatedEnvelope(emptyList()))
    }

    override suspend fun getSystem(): ApiResult<AdminSystem> = ApiResult.Ok(AdminSystem("ok", emptyList(), "1.0", 0, 0.0))
    override suspend fun getHealth(): ApiResult<List<AdminServiceHealth>> = ApiResult.Ok(emptyList())
    override suspend fun getEvents(): ApiResult<List<PlatformEvent>> = ApiResult.Ok(emptyList())
    override suspend fun getFeatureFlags(): ApiResult<List<FeatureFlag>> = ApiResult.Ok(emptyList())

    override suspend fun setFeatureFlag(body: AdminSetFeatureFlagRequest): ApiResult<FeatureFlag> =
        ApiResult.Ok(FeatureFlag(key = body.key, isEnabledGlobally = body.isEnabledGlobally, rolloutPercentage = body.rolloutPercentage))

    override suspend fun setFeatureFlagOverride(flagKey: String, broadcasterId: String, body: AdminSetFeatureFlagOverrideRequest): ApiResult<Unit> =
        ApiResult.Ok(Unit)

    override suspend fun deleteFeatureFlagOverride(flagKey: String, broadcasterId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun getInviteCodes(page: Int, pageSize: Int): ApiResult<PaginatedEnvelope<InviteCode>> =
        ApiResult.Ok(PaginatedEnvelope(emptyList()))

    override suspend fun createInviteCode(body: AdminCreateInviteCodeRequest): ApiResult<InviteCode> =
        ApiResult.Ok(InviteCode("id", "code", body.maxRedemptions, 0, body.grantsFoundersBadge))

    override suspend fun revokeInviteCode(inviteCodeId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun grantTier(broadcasterId: String, body: AdminGrantTierRequest): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun grantFounderBadge(broadcasterId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun impersonate(subjectUserId: String, accessGrantId: String, justification: String): ApiResult<ImpersonationTokenDto> =
        ApiResult.Failure(ApiError(status = 500, code = null, message = "not stubbed"))

    override suspend fun endImpersonation(accessGrantId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
}

private class LoadingFakePlatformIamApi : PlatformIamApi {
    override suspend fun listRoles(): ApiResult<List<IamRole>> = ApiResult.Ok(emptyList())
    override suspend fun listPrincipals(): ApiResult<List<IamPrincipalSummary>> = ApiResult.Ok(emptyList())
    override suspend fun effectivePermissions(principalId: String, scopeChannelId: String?): ApiResult<List<String>> =
        ApiResult.Ok(emptyList())
    override suspend fun createPrincipal(body: CreatePrincipalBody): ApiResult<IamPrincipal> =
        ApiResult.Failure(ApiError(status = 500, code = null, message = "not stubbed"))
    override suspend fun deactivatePrincipal(principalId: String, reason: String?): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun reactivatePrincipal(principalId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun assignRole(body: AssignRoleBody): ApiResult<IamRoleAssignment> =
        ApiResult.Failure(ApiError(status = 500, code = null, message = "not stubbed"))
    override suspend fun revokeAssignment(assignmentId: String, reason: String?): ApiResult<Unit> = ApiResult.Ok(Unit)
}

private class LoadingFakePlatformAdminApi : PlatformAdminApi {
    override suspend fun listTenants(search: String?, status: String?, isLive: Boolean?, page: Int, pageSize: Int): ApiResult<PaginatedEnvelope<AdminTenant>> =
        ApiResult.Ok(PaginatedEnvelope(emptyList()))
    override suspend fun getTenant(broadcasterId: String): ApiResult<AdminTenantDetail> =
        ApiResult.Failure(ApiError(status = 404, code = null, message = "not stubbed"))
    override suspend fun suspendTenant(broadcasterId: String, body: SuspendTenantBody): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun reinstateTenant(broadcasterId: String, body: ReinstateTenantBody): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun beginAccess(broadcasterId: String, body: BeginTenantAccessBody): ApiResult<TenantAccessGrant> =
        ApiResult.Failure(ApiError(status = 500, code = null, message = "not stubbed"))
    override suspend fun endAccess(accessGrantId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun searchAudit(
        principalId: String?,
        targetBroadcasterId: String?,
        permission: String?,
        outcome: String?,
        from: String?,
        to: String?,
        page: Int,
        pageSize: Int,
    ): ApiResult<PaginatedEnvelope<IamAuditEntry>> = ApiResult.Ok(PaginatedEnvelope(emptyList()))
}
