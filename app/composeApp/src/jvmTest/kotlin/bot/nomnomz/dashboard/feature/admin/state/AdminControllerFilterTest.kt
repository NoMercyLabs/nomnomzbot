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
import bot.nomnomz.dashboard.core.network.AdminCreateTierRequest
import bot.nomnomz.dashboard.core.network.AdminEntitlementGrant
import bot.nomnomz.dashboard.core.network.AdminEntitlementGrantPreview
import bot.nomnomz.dashboard.core.network.AdminGrantTierRequest
import bot.nomnomz.dashboard.core.network.AdminInvoice
import bot.nomnomz.dashboard.core.network.AdminIssueEntitlementGrantRequest
import bot.nomnomz.dashboard.core.network.AdminServiceHealth
import bot.nomnomz.dashboard.core.network.AdminSetFeatureFlagOverrideRequest
import bot.nomnomz.dashboard.core.network.AdminSetFeatureFlagRequest
import bot.nomnomz.dashboard.core.network.AdminStats
import bot.nomnomz.dashboard.core.network.AdminSupportApi
import bot.nomnomz.dashboard.core.network.AdminTenant
import bot.nomnomz.dashboard.core.network.AdminTier
import bot.nomnomz.dashboard.core.network.AdminUpdateTierRequest
import bot.nomnomz.dashboard.core.network.AdminUser
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.AssignRoleBody
import bot.nomnomz.dashboard.core.network.BeginTenantAccessBody
import bot.nomnomz.dashboard.core.network.CommandSummary
import bot.nomnomz.dashboard.core.network.CreatePrincipalBody
import bot.nomnomz.dashboard.core.network.FeatureFlag
import bot.nomnomz.dashboard.core.network.FeatureFlagOverride
import bot.nomnomz.dashboard.core.network.IamAuditEntry
import bot.nomnomz.dashboard.core.network.IamPrincipalSummary
import bot.nomnomz.dashboard.core.network.IamRole
import bot.nomnomz.dashboard.core.network.InviteCode
import bot.nomnomz.dashboard.core.network.PaginatedEnvelope
import bot.nomnomz.dashboard.core.network.PipelineSummary
import bot.nomnomz.dashboard.core.network.PlatformAdminApi
import bot.nomnomz.dashboard.core.network.PlatformEvent
import bot.nomnomz.dashboard.core.network.PlatformIamApi
import bot.nomnomz.dashboard.core.network.ProviderCredential
import bot.nomnomz.dashboard.core.network.ReinstateTenantBody
import bot.nomnomz.dashboard.core.network.SaveProviderCredentialBody
import bot.nomnomz.dashboard.core.network.SupportPersonView
import bot.nomnomz.dashboard.core.network.SuspendTenantBody
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue

/**
 * Admin-console usability walk (2026-09-27): the controller's filters and refreshes must do what the chips
 * say. Each test pins a defect found by reading the console end to end: a null filter that was read as
 * "keep the previous one", a write refresh that widened the lists and unmounted the tab, a first load that
 * never recorded whether a next page exists, and a sheet that could not open because the field it needed
 * lived inside it.
 */
class AdminControllerFilterTest {

    private fun controllerFor(
        api: RecordingAdminApi = RecordingAdminApi(),
        platformAdminApi: RecordingPlatformAdminApi = RecordingPlatformAdminApi(),
        supportApi: RecordingSupportApi? = null,
    ): AdminController =
        AdminController(
            api = api,
            iamApi = NoopIamApiForFilterTest(),
            platformAdminApi = platformAdminApi,
            supportApi = supportApi,
        )

    @Test
    fun the_all_outcomes_chip_clears_the_audit_outcome_filter() = runTest {
        val platformAdminApi = RecordingPlatformAdminApi()
        val controller = controllerFor(platformAdminApi = platformAdminApi)

        controller.loadAudit(outcome = "denied", permission = "")
        controller.loadAudit(outcome = null, permission = "")

        val expected: List<Pair<String?, String?>> = listOf("denied" to "", null to "")
        assertEquals(expected, platformAdminApi.auditCalls.toList())
        assertNull(controller.state.value.auditOutcomeFilter)
    }

    @Test
    fun the_all_statuses_chip_clears_the_tenant_status_filter_and_keeps_the_search() = runTest {
        val platformAdminApi = RecordingPlatformAdminApi()
        val controller = controllerFor(platformAdminApi = platformAdminApi)

        controller.loadTenants(search = "acme", status = "suspended")
        controller.loadTenants(search = controller.state.value.tenantSearch, status = null)

        val expected: List<Pair<String?, String?>> = listOf("acme" to "suspended", "acme" to null)
        assertEquals(expected, platformAdminApi.tenantCalls.toList())
        assertNull(controller.state.value.tenantStatusFilter)
        assertEquals("acme", controller.state.value.tenantSearch)
    }

    @Test
    fun the_first_load_records_whether_a_next_page_of_channels_and_users_exists() = runTest {
        val api = RecordingAdminApi(channelsHaveMore = true, usersHaveMore = true)
        val controller = controllerFor(api = api)

        controller.load()

        assertTrue(controller.state.value.channelHasMore, "the Next control is decided by the first page's hasMore")
        assertTrue(controller.state.value.userHasMore)
    }

    @Test
    fun a_feature_flag_write_refreshes_the_lists_on_their_current_filters_without_a_spinner() = runTest {
        val api = RecordingAdminApi()
        val controller = controllerFor(api = api)
        api.spinnerProbe = { controller.state.value.loadingSections.isNotEmpty() }
        controller.load()
        controller.loadChannels(search = "acme", isLive = true)
        controller.loadUsers(search = "stoney", role = "admin")
        api.channelCalls.clear()
        api.userCalls.clear()

        controller.setFeatureFlag(
            AdminSetFeatureFlagRequest(key = "integration:spotify", isEnabledGlobally = true, rolloutPercentage = 100),
        )

        assertEquals(listOf(ChannelQuery("acme", 1, true)), api.channelCalls.toList())
        assertEquals(listOf(UserQuery("stoney", 1, "admin")), api.userCalls.toList())
        assertEquals("acme", controller.state.value.channelSearch)
        assertEquals(true, controller.state.value.channelLiveFilter)
        assertEquals(2, api.featureFlagReads, "the flags were re-read once after the write")
        assertTrue(controller.state.value.loadingSections.isEmpty())
        assertFalse(api.spinnerSeenDuringRefresh, "a write refresh must never flip the tab spinner")
    }

    @Test
    fun view_own_content_opens_the_sheet_before_a_justification_exists_and_loads_once_one_is_typed() = runTest {
        val supportApi = RecordingSupportApi()
        val controller = controllerFor(supportApi = supportApi)

        controller.openTenantContent("chan-1")

        assertEquals("chan-1", controller.state.value.tenantContentOpenFor)
        assertTrue(supportApi.tenantCommandCalls.isEmpty(), "nothing is read until the operator justifies")

        controller.setTenantContentJustification("Ticket #12 — command abuse report")
        controller.openTenantContent("chan-1")

        assertEquals(listOf("chan-1" to "Ticket #12 — command abuse report"), supportApi.tenantCommandCalls.toList())
        assertFalse(controller.state.value.tenantContentLoading)
    }
}

private data class ChannelQuery(val search: String?, val page: Int, val isLive: Boolean?)

private data class UserQuery(val search: String?, val page: Int, val role: String?)

private class RecordingAdminApi(
    private val channelsHaveMore: Boolean = false,
    private val usersHaveMore: Boolean = false,
) : AdminApi {
    val channelCalls: MutableList<ChannelQuery> = mutableListOf()
    val userCalls: MutableList<UserQuery> = mutableListOf()
    var featureFlagReads: Int = 0

    /** Reads the controller's spinner state; sampled on every read the write's refresh makes. */
    var spinnerProbe: () -> Boolean = { false }
    var spinnerSeenDuringRefresh: Boolean = false
    private var refreshing: Boolean = false

    override suspend fun getStats(): ApiResult<AdminStats> = ApiResult.Ok(AdminStats(0, 0, 0, "ok", 0, 0))
    override suspend fun getChannels(search: String?, page: Int, pageSize: Int, sort: String?, isLive: Boolean?): ApiResult<PaginatedEnvelope<AdminChannel>> {
        channelCalls += ChannelQuery(search, page, isLive)
        val row = AdminChannel("chan-1", "Acme", "acme", true, true, 12, "free", "2026-09-27T10:00:00Z")
        return ApiResult.Ok(PaginatedEnvelope(listOf(row), hasMore = channelsHaveMore, nextPage = 2))
    }
    override suspend fun getUsers(search: String?, page: Int, pageSize: Int, sort: String?, role: String?): ApiResult<PaginatedEnvelope<AdminUser>> {
        userCalls += UserQuery(search, page, role)
        val row = AdminUser("user-1", "Stoney", "stoney_eagle", role = "admin", channelCount = 1, createdAt = "2026-09-27T10:00:00Z")
        return ApiResult.Ok(PaginatedEnvelope(listOf(row), hasMore = usersHaveMore, nextPage = 2))
    }
    override suspend fun getSystem() = ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun getHealth() = ApiResult.Ok(emptyList<AdminServiceHealth>())
    override suspend fun getEvents() = ApiResult.Ok(emptyList<PlatformEvent>())
    override suspend fun getFeatureFlags(): ApiResult<List<FeatureFlag>> {
        featureFlagReads += 1
        if (refreshing && spinnerProbe()) spinnerSeenDuringRefresh = true
        return ApiResult.Ok(listOf(FeatureFlag(key = "integration:spotify", isEnabledGlobally = true, rolloutPercentage = 100)))
    }
    override suspend fun setFeatureFlag(body: AdminSetFeatureFlagRequest): ApiResult<FeatureFlag> {
        // From here on the refresh runs; a spinner flipped during it is the defect this fake watches for.
        refreshing = true
        return ApiResult.Ok(FeatureFlag(key = body.key, isEnabledGlobally = body.isEnabledGlobally, rolloutPercentage = body.rolloutPercentage))
    }
    override suspend fun setFeatureFlagOverride(flagKey: String, broadcasterId: String, body: AdminSetFeatureFlagOverrideRequest) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun deleteFeatureFlagOverride(flagKey: String, broadcasterId: String) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun getFeatureFlagOverrides(): ApiResult<List<FeatureFlagOverride>> = ApiResult.Ok(emptyList())
    override suspend fun previewFeatureFlagBlastRadius(flagKey: String) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun getInviteCodes(page: Int, pageSize: Int) = ApiResult.Ok(PaginatedEnvelope<InviteCode>(emptyList()))
    override suspend fun createInviteCode(body: AdminCreateInviteCodeRequest) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun revokeInviteCode(inviteCodeId: String) = ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun grantTier(broadcasterId: String, body: AdminGrantTierRequest) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun grantFounderBadge(broadcasterId: String) = ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun impersonate(subjectUserId: String, accessGrantId: String, justification: String) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun endImpersonation(accessGrantId: String) = ApiResult.Ok(Unit)
    override suspend fun getProviderCredentials() = ApiResult.Ok(emptyList<ProviderCredential>())
    override suspend fun saveProviderCredential(provider: String, body: SaveProviderCredentialBody) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun clearProviderCredential(provider: String) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun getTiers(): ApiResult<List<AdminTier>> = ApiResult.Ok(emptyList())
    override suspend fun previewTierChange(tierId: String) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun createTier(body: AdminCreateTierRequest) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun updateTier(tierId: String, body: AdminUpdateTierRequest) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun getEntitlementGrants(broadcasterId: String): ApiResult<List<AdminEntitlementGrant>> =
        ApiResult.Ok(emptyList())
    override suspend fun previewEntitlementGrant(broadcasterId: String, tierId: String): ApiResult<AdminEntitlementGrantPreview> =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun issueEntitlementGrant(broadcasterId: String, body: AdminIssueEntitlementGrantRequest) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun getInvoices(broadcasterId: String): ApiResult<List<AdminInvoice>> = ApiResult.Ok(emptyList())
    override suspend fun refundInvoice(invoiceId: String) = ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
}

private class RecordingPlatformAdminApi : PlatformAdminApi {
    /** Every (search, status) pair the tenant list was asked for, in order. */
    val tenantCalls: MutableList<Pair<String?, String?>> = mutableListOf()

    /** Every (outcome, permission) pair the audit log was asked for, in order. */
    val auditCalls: MutableList<Pair<String?, String?>> = mutableListOf()

    override suspend fun listTenants(search: String?, status: String?, isLive: Boolean?, page: Int, pageSize: Int): ApiResult<PaginatedEnvelope<AdminTenant>> {
        tenantCalls += search to status
        return ApiResult.Ok(PaginatedEnvelope(emptyList()))
    }
    override suspend fun getTenant(broadcasterId: String) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun suspendTenant(broadcasterId: String, body: SuspendTenantBody) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun reinstateTenant(broadcasterId: String, body: ReinstateTenantBody) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun beginAccess(broadcasterId: String, body: BeginTenantAccessBody) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun endAccess(accessGrantId: String) = ApiResult.Ok(Unit)
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
        auditCalls += outcome to permission
        return ApiResult.Ok(PaginatedEnvelope(emptyList()))
    }
}

private class RecordingSupportApi : AdminSupportApi {
    /** Every (channelId, justification) pair the tenant's commands were read with. */
    val tenantCommandCalls: MutableList<Pair<String, String>> = mutableListOf()

    override suspend fun searchPeople(search: String, justification: String, page: Int, pageSize: Int) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun getPerson(subjectUserId: String, justification: String): ApiResult<SupportPersonView> =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun getPersonHistory(subjectUserId: String, justification: String, page: Int, pageSize: Int) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun getTenantCommands(channelId: String, justification: String, page: Int, pageSize: Int): ApiResult<PaginatedEnvelope<CommandSummary>> {
        tenantCommandCalls += channelId to justification
        return ApiResult.Ok(PaginatedEnvelope(emptyList()))
    }
    override suspend fun getTenantPipelines(channelId: String, justification: String, page: Int, pageSize: Int): ApiResult<PaginatedEnvelope<PipelineSummary>> =
        ApiResult.Ok(PaginatedEnvelope(emptyList()))
}

private class NoopIamApiForFilterTest : PlatformIamApi {
    override suspend fun listRoles(): ApiResult<List<IamRole>> = ApiResult.Ok(emptyList())
    override suspend fun listPrincipals(): ApiResult<List<IamPrincipalSummary>> = ApiResult.Ok(emptyList())
    override suspend fun effectivePermissions(principalId: String, scopeChannelId: String?) = ApiResult.Ok(emptyList<String>())
    override suspend fun createPrincipal(body: CreatePrincipalBody) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun deactivatePrincipal(principalId: String, reason: String?) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun reactivatePrincipal(principalId: String) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun assignRole(body: AssignRoleBody) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun revokeAssignment(assignmentId: String, reason: String?) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
}
