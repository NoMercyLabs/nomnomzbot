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
import bot.nomnomz.dashboard.core.network.AdminEventSubTenantHealth
import bot.nomnomz.dashboard.core.network.AdminEventSubTopicHealth
import bot.nomnomz.dashboard.core.network.AdminScheduledJob
import bot.nomnomz.dashboard.core.network.AdminServiceHealth
import bot.nomnomz.dashboard.core.network.AdminSetFeatureFlagOverrideRequest
import bot.nomnomz.dashboard.core.network.AdminSetFeatureFlagRequest
import bot.nomnomz.dashboard.core.network.AdminStats
import bot.nomnomz.dashboard.core.network.AdminSystem
import bot.nomnomz.dashboard.core.network.AdminTenant
import bot.nomnomz.dashboard.core.network.AdminTenantDetail
import bot.nomnomz.dashboard.core.network.AdminTenantErrorBudget
import bot.nomnomz.dashboard.core.network.AdminTenantUsage
import bot.nomnomz.dashboard.core.network.AdminUser
import bot.nomnomz.dashboard.core.network.AdminWebhookDelivery
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.AssignRoleBody
import bot.nomnomz.dashboard.core.network.BeginTenantAccessBody
import bot.nomnomz.dashboard.core.network.CreatePrincipalBody
import bot.nomnomz.dashboard.core.network.FeatureFlag
import bot.nomnomz.dashboard.core.network.FeatureFlagBlastRadiusDto
import bot.nomnomz.dashboard.core.network.IamAuditEntry
import bot.nomnomz.dashboard.core.network.IamPrincipal
import bot.nomnomz.dashboard.core.network.IamPrincipalSummary
import bot.nomnomz.dashboard.core.network.IamRole
import bot.nomnomz.dashboard.core.network.IamRoleAssignment
import bot.nomnomz.dashboard.core.network.InviteCode
import bot.nomnomz.dashboard.core.network.PaginatedEnvelope
import bot.nomnomz.dashboard.core.network.PlatformAdminApi
import bot.nomnomz.dashboard.core.network.PlatformEvent
import bot.nomnomz.dashboard.core.network.PlatformIamApi
import bot.nomnomz.dashboard.core.network.ReinstateTenantBody
import bot.nomnomz.dashboard.core.network.SuspendTenantBody
import bot.nomnomz.dashboard.core.network.TenantAccessGrant
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

/**
 * Proves the ops-tools admin tabs (EventSub health, webhook deliveries, scheduled jobs, tenant usage, error
 * budget) actually reach rows past the server's 25-row page cap, not just that they render page 1. Each tab
 * used to call its `get*()` endpoint with no page argument and never expose [AdminState] page/hasMore fields,
 * so a platform with more than 25 rows had everything past row 25 permanently unreachable from the dashboard.
 */
class AdminControllerOpsPaginationTest {

    @Test
    fun loadEventSubHealth_page_two_reaches_the_26th_tenant_and_reports_no_further_page() = runTest {
        val api = PagedOpsFakeAdminApi()
        val controller = AdminController(api = api, iamApi = PagedFakePlatformIamApi(), platformAdminApi = PagedFakePlatformAdminApi())

        controller.loadEventSubHealth()
        assertEquals(1, controller.state.value.eventSubHealthPage)
        assertTrue(controller.state.value.eventSubHealthHasMore)
        assertEquals(25, controller.state.value.eventSubHealth.size)
        assertEquals("tenant-1", controller.state.value.eventSubHealth.first().broadcasterId)

        controller.loadEventSubHealth(page = 2)
        assertEquals(2, controller.state.value.eventSubHealthPage)
        assertFalse(controller.state.value.eventSubHealthHasMore)
        assertEquals(1, controller.state.value.eventSubHealth.size)
        assertEquals("tenant-26", controller.state.value.eventSubHealth.first().broadcasterId)
    }

    @Test
    fun loadWebhookDeliveries_page_two_reaches_the_26th_delivery_and_reports_no_further_page() = runTest {
        val api = PagedOpsFakeAdminApi()
        val controller = AdminController(api = api, iamApi = PagedFakePlatformIamApi(), platformAdminApi = PagedFakePlatformAdminApi())

        controller.loadWebhookDeliveries()
        assertEquals(25, controller.state.value.webhookDeliveries.size)
        assertTrue(controller.state.value.webhookDeliveriesHasMore)

        controller.loadWebhookDeliveries(page = 2)
        assertEquals(2, controller.state.value.webhookDeliveriesPage)
        assertFalse(controller.state.value.webhookDeliveriesHasMore)
        assertEquals(1, controller.state.value.webhookDeliveries.size)
        assertEquals(26L, controller.state.value.webhookDeliveries.first().id)
    }

    @Test
    fun loadScheduledJobs_page_two_reaches_the_26th_job_and_reports_no_further_page() = runTest {
        val api = PagedOpsFakeAdminApi()
        val controller = AdminController(api = api, iamApi = PagedFakePlatformIamApi(), platformAdminApi = PagedFakePlatformAdminApi())

        controller.loadScheduledJobs()
        assertEquals(25, controller.state.value.scheduledJobs.size)
        assertTrue(controller.state.value.scheduledJobsHasMore)

        controller.loadScheduledJobs(page = 2)
        assertEquals(2, controller.state.value.scheduledJobsPage)
        assertFalse(controller.state.value.scheduledJobsHasMore)
        assertEquals(1, controller.state.value.scheduledJobs.size)
        assertEquals("job-26", controller.state.value.scheduledJobs.first().id)
    }

    @Test
    fun loadTenantUsage_page_two_reaches_the_26th_tenant_and_reports_no_further_page() = runTest {
        val api = PagedOpsFakeAdminApi()
        val controller = AdminController(api = api, iamApi = PagedFakePlatformIamApi(), platformAdminApi = PagedFakePlatformAdminApi())

        controller.loadTenantUsage()
        assertEquals(25, controller.state.value.tenantUsage.size)
        assertTrue(controller.state.value.tenantUsageHasMore)

        controller.loadTenantUsage(page = 2)
        assertEquals(2, controller.state.value.tenantUsagePage)
        assertFalse(controller.state.value.tenantUsageHasMore)
        assertEquals(1, controller.state.value.tenantUsage.size)
        assertEquals("tenant-26", controller.state.value.tenantUsage.first().broadcasterId)
    }

    @Test
    fun loadErrorBudget_page_two_reaches_the_26th_tenant_and_reports_no_further_page() = runTest {
        val api = PagedOpsFakeAdminApi()
        val controller = AdminController(api = api, iamApi = PagedFakePlatformIamApi(), platformAdminApi = PagedFakePlatformAdminApi())

        controller.loadErrorBudget()
        assertEquals(25, controller.state.value.errorBudget.size)
        assertTrue(controller.state.value.errorBudgetHasMore)

        controller.loadErrorBudget(page = 2)
        assertEquals(2, controller.state.value.errorBudgetPage)
        assertFalse(controller.state.value.errorBudgetHasMore)
        assertEquals(1, controller.state.value.errorBudget.size)
        assertEquals("tenant-26", controller.state.value.errorBudget.first().broadcasterId)
    }
}

// ─── Fakes ─────────────────────────────────────────────────────────────────

/** 26 rows total per list — page 1 is a full 25-row page with more after it; page 2 is the lone 26th row
 * with nothing further, exactly the server's own `PaginatedResponse` contract. */
private class PagedOpsFakeAdminApi : AdminApi {
    override suspend fun getStats(): ApiResult<AdminStats> = ApiResult.Ok(AdminStats(0, 0, 0, "ok", 0, 0))
    override suspend fun getChannels(search: String?, page: Int, pageSize: Int, sort: String?, isLive: Boolean?): ApiResult<PaginatedEnvelope<AdminChannel>> =
        ApiResult.Ok(PaginatedEnvelope(emptyList()))
    override suspend fun getUsers(search: String?, page: Int, pageSize: Int, sort: String?, role: String?): ApiResult<PaginatedEnvelope<AdminUser>> =
        ApiResult.Ok(PaginatedEnvelope(emptyList()))
    override suspend fun getSystem(): ApiResult<AdminSystem> = ApiResult.Ok(AdminSystem("ok", emptyList(), "1.0", 0, 0.0))
    override suspend fun getHealth(): ApiResult<List<AdminServiceHealth>> = ApiResult.Ok(emptyList())
    override suspend fun getEvents(): ApiResult<List<PlatformEvent>> = ApiResult.Ok(emptyList())
    override suspend fun getFeatureFlags(): ApiResult<List<FeatureFlag>> = ApiResult.Ok(emptyList())
    override suspend fun setFeatureFlag(body: AdminSetFeatureFlagRequest): ApiResult<FeatureFlag> =
        ApiResult.Ok(FeatureFlag(key = body.key, isEnabledGlobally = body.isEnabledGlobally, rolloutPercentage = body.rolloutPercentage))
    override suspend fun setFeatureFlagOverride(flagKey: String, broadcasterId: String, body: AdminSetFeatureFlagOverrideRequest): ApiResult<Unit> =
        ApiResult.Ok(Unit)
    override suspend fun deleteFeatureFlagOverride(flagKey: String, broadcasterId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun previewFeatureFlagBlastRadius(flagKey: String): ApiResult<FeatureFlagBlastRadiusDto> =
        ApiResult.Ok(FeatureFlagBlastRadiusDto())
    override suspend fun getInviteCodes(page: Int, pageSize: Int): ApiResult<PaginatedEnvelope<InviteCode>> =
        ApiResult.Ok(PaginatedEnvelope(emptyList()))
    override suspend fun createInviteCode(body: bot.nomnomz.dashboard.core.network.AdminCreateInviteCodeRequest): ApiResult<InviteCode> =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun revokeInviteCode(inviteCodeId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun grantTier(broadcasterId: String, body: bot.nomnomz.dashboard.core.network.AdminGrantTierRequest): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun grantFounderBadge(broadcasterId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun impersonate(subjectUserId: String, accessGrantId: String, justification: String) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun endImpersonation(accessGrantId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun getProviderCredentials() = ApiResult.Ok(emptyList<bot.nomnomz.dashboard.core.network.ProviderCredential>())
    override suspend fun saveProviderCredential(provider: String, body: bot.nomnomz.dashboard.core.network.SaveProviderCredentialBody) =
        ApiResult.Ok(bot.nomnomz.dashboard.core.network.ProviderCredential(provider = provider))
    override suspend fun clearProviderCredential(provider: String) =
        ApiResult.Ok(bot.nomnomz.dashboard.core.network.ProviderCredential(provider = provider))
    override suspend fun getTiers() = ApiResult.Ok(emptyList<bot.nomnomz.dashboard.core.network.AdminTier>())
    override suspend fun previewTierChange(tierId: String) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun createTier(body: bot.nomnomz.dashboard.core.network.AdminCreateTierRequest) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun updateTier(tierId: String, body: bot.nomnomz.dashboard.core.network.AdminUpdateTierRequest) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun getEntitlementGrants(broadcasterId: String) = ApiResult.Ok(emptyList<bot.nomnomz.dashboard.core.network.AdminEntitlementGrant>())
    override suspend fun previewEntitlementGrant(broadcasterId: String, tierId: String) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun issueEntitlementGrant(broadcasterId: String, body: bot.nomnomz.dashboard.core.network.AdminIssueEntitlementGrantRequest) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun getInvoices(broadcasterId: String) = ApiResult.Ok(emptyList<bot.nomnomz.dashboard.core.network.AdminInvoice>())
    override suspend fun refundInvoice(invoiceId: String) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))

    override suspend fun getEventSubHealth(page: Int, pageSize: Int): ApiResult<PaginatedEnvelope<AdminEventSubTenantHealth>> =
        ApiResult.Ok(pagedEnvelope(page) { n ->
            AdminEventSubTenantHealth(
                broadcasterId = "tenant-$n",
                channelDisplayName = "Tenant $n",
                topics = listOf(AdminEventSubTopicHealth("sub-$n", "channel.follow", "1", "enabled", true, lastConfirmedAt = "now")),
            )
        })

    override suspend fun getWebhookDeliveries(page: Int, pageSize: Int): ApiResult<PaginatedEnvelope<AdminWebhookDelivery>> =
        ApiResult.Ok(pagedEnvelope(page) { n ->
            AdminWebhookDelivery(
                id = n.toLong(),
                broadcasterId = "tenant-$n",
                endpointId = "ep-$n",
                endpointName = "Endpoint $n",
                endpointCanReplay = true,
                eventType = "channel.follow",
                attempt = 1,
                status = "Delivered",
                createdAt = "now",
            )
        })

    override suspend fun getScheduledJobs(page: Int, pageSize: Int): ApiResult<PaginatedEnvelope<AdminScheduledJob>> =
        ApiResult.Ok(pagedEnvelope(page) { n ->
            AdminScheduledJob(
                id = "job-$n",
                broadcasterId = "tenant-$n",
                channelDisplayName = "Tenant $n",
                pipelineId = "pipeline-$n",
                pipelineExists = true,
                status = "queued",
                displayState = "queued",
                dueAt = "now",
                createdAt = "now",
                triggeredByDisplayName = "system",
                canRetry = false,
            )
        })

    override suspend fun getTenantUsage(page: Int, pageSize: Int): ApiResult<PaginatedEnvelope<AdminTenantUsage>> =
        ApiResult.Ok(pagedEnvelope(page) { n ->
            AdminTenantUsage(broadcasterId = "tenant-$n", channelDisplayName = "Tenant $n", periodStart = "start", periodEnd = "end")
        })

    override suspend fun getErrorBudget(page: Int, pageSize: Int): ApiResult<PaginatedEnvelope<AdminTenantErrorBudget>> =
        ApiResult.Ok(pagedEnvelope(page) { n ->
            AdminTenantErrorBudget(
                broadcasterId = "tenant-$n",
                channelDisplayName = "Tenant $n",
                windowStartUtc = "start",
                windowEndUtc = "end",
                attempts = 10,
                errors = 0,
                targetSuccessRate = 0.99,
            )
        })
}

/** 25 rows on page 1 (with more after it), the lone 26th row on page 2 (with nothing further) — the exact
 * shape the server's own 25-row-page `PaginatedResponse` returns. */
private fun <T> pagedEnvelope(page: Int, build: (Int) -> T): PaginatedEnvelope<T> =
    if (page <= 1) {
        PaginatedEnvelope(data = (1..25).map(build), hasMore = true)
    } else {
        PaginatedEnvelope(data = listOf(build(26)), hasMore = false)
    }

private class PagedFakePlatformIamApi : PlatformIamApi {
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

private class PagedFakePlatformAdminApi : PlatformAdminApi {
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
