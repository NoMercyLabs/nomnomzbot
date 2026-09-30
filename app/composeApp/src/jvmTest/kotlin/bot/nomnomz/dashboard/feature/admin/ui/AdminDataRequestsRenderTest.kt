// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.admin.ui

import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.assertIsEnabled
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.hasClickAction
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onFirst
import androidx.compose.ui.test.onLast
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.AdminApi
import bot.nomnomz.dashboard.core.network.AdminChannel
import bot.nomnomz.dashboard.core.network.AdminCreateInviteCodeRequest
import bot.nomnomz.dashboard.core.network.AdminCreateTierRequest
import bot.nomnomz.dashboard.core.network.AdminEntitlementGrant
import bot.nomnomz.dashboard.core.network.AdminGrantTierRequest
import bot.nomnomz.dashboard.core.network.AdminInvoice
import bot.nomnomz.dashboard.core.network.AdminIssueEntitlementGrantRequest
import bot.nomnomz.dashboard.core.network.AdminServiceHealth
import bot.nomnomz.dashboard.core.network.AdminSetFeatureFlagOverrideRequest
import bot.nomnomz.dashboard.core.network.AdminSetFeatureFlagRequest
import bot.nomnomz.dashboard.core.network.AdminStats
import bot.nomnomz.dashboard.core.network.AdminTenant
import bot.nomnomz.dashboard.core.network.AdminTier
import bot.nomnomz.dashboard.core.network.AdminUpdateTierRequest
import bot.nomnomz.dashboard.core.network.AdminUser
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.AssignRoleBody
import bot.nomnomz.dashboard.core.network.BeginTenantAccessBody
import bot.nomnomz.dashboard.core.network.ComplianceApi
import bot.nomnomz.dashboard.core.network.CreatePrincipalBody
import bot.nomnomz.dashboard.core.network.ErasurePreview
import bot.nomnomz.dashboard.core.network.ErasurePreviewCategory
import bot.nomnomz.dashboard.core.network.ErasureRequest
import bot.nomnomz.dashboard.core.network.ErasureRequestSummary
import bot.nomnomz.dashboard.core.network.FeatureFlag
import bot.nomnomz.dashboard.core.network.FeatureFlagOverride
import bot.nomnomz.dashboard.core.network.IamAuditEntry
import bot.nomnomz.dashboard.core.network.IamPrincipalSummary
import bot.nomnomz.dashboard.core.network.IamRole
import bot.nomnomz.dashboard.core.network.InviteCode
import bot.nomnomz.dashboard.core.network.PaginatedEnvelope
import bot.nomnomz.dashboard.core.network.PlatformAdminApi
import bot.nomnomz.dashboard.core.network.PlatformEvent
import bot.nomnomz.dashboard.core.network.PlatformIamApi
import bot.nomnomz.dashboard.core.network.ProviderCredential
import bot.nomnomz.dashboard.core.network.ReinstateTenantBody
import bot.nomnomz.dashboard.core.network.SaveProviderCredentialBody
import bot.nomnomz.dashboard.core.network.SuspendTenantBody
import bot.nomnomz.dashboard.feature.admin.state.AdminController
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

/**
 * A7: the platform-wide data-requests tab must render the REAL ledger — the status counts and each
 * request's recorded state — and a failed request must carry its recorded failure reason where the
 * operator reads the row, never hide behind a neutral badge. Narrowing by a chip must ask the backend
 * for exactly that filter, so the counts and rows always describe the same ledger moment.
 */
@OptIn(ExperimentalTestApi::class)
class AdminDataRequestsRenderTest {

    private val failedErasure = ErasureRequest(
        id = "req-1",
        subjectUserId = "user-9",
        subjectIdHash = "ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789",
        broadcasterId = null,
        requestType = "erasure",
        requestedBy = "self_service",
        status = "failed",
        scope = "deployment",
        rowsAffected = 0,
        failureReason = "The token vault refused the revocation.",
        requestedAt = "2026-09-27T10:00:00Z",
    )

    private val completedExport = ErasureRequest(
        id = "req-2",
        subjectUserId = "user-10",
        subjectIdHash = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF",
        broadcasterId = "chan-1",
        requestType = "export",
        requestedBy = "platform_iam",
        status = "completed",
        scope = "deployment",
        rowsAffected = 17,
        requestedAt = "2026-09-27T09:00:00Z",
        completedAt = "2026-09-27T09:00:02Z",
    )

    @Composable
    private fun EnglishContent(content: @Composable () -> Unit) {
        AppEnvironment(tag = "en") {
            NomNomzTheme { content() }
        }
    }

    @Composable
    private fun ObservingDataRequestsTab(controller: AdminController) {
        val state by controller.state.collectAsState()
        DataRequestsTab(state = state, controller = controller)
    }

    private fun controllerFor(fakeApi: FakeComplianceApi): AdminController {
        val controller = AdminController(
            api = FakeAdminApiForDataRequestsTest(),
            iamApi = FakeIamApiForDataRequestsTest(),
            platformAdminApi = FakePlatformAdminApiForDataRequestsTest(),
            complianceApi = fakeApi,
        )
        runTest { controller.loadDataRequests(status = null, requestType = null) }
        return controller
    }

    @Test
    fun the_ledger_renders_its_real_counts_and_a_failed_request_shows_its_failure_reason() {
        val controller = controllerFor(
            FakeComplianceApi(
                requests = listOf(failedErasure, completedExport),
                summary = ErasureRequestSummary(total = 2, completed = 1, failed = 1),
            ),
        )

        runComposeUiTest {
            setContent { EnglishContent { ObservingDataRequestsTab(controller = controller) } }
            waitForIdle()

            onNodeWithText("2 requests · 0 running · 1 failed").assertExists()
            onNodeWithText("Subject ABCDEF012345…").assertExists()
            onNodeWithText("The token vault refused the revocation.").assertExists()
            onNodeWithText("Requested by an operator").assertExists()
            onNodeWithText("17 rows").assertExists()
        }
    }

    @Test
    fun choosing_a_status_chip_asks_the_backend_for_exactly_that_status() {
        val fakeApi = FakeComplianceApi(
            requests = listOf(failedErasure, completedExport),
            summary = ErasureRequestSummary(total = 2, completed = 1, failed = 1),
        )
        val controller = controllerFor(fakeApi)

        runComposeUiTest {
            setContent { EnglishContent { ObservingDataRequestsTab(controller = controller) } }
            waitForIdle()

            // "Failed" is both the status chip and the failed row's badge; the chip row renders first.
            onAllNodesWithText("Failed").onFirst().performClick()
            waitForIdle()

            val expected: List<Pair<String?, String?>> = listOf(null to null, "failed" to null)
            assertEquals(expected, fakeApi.listCalls.toList())
        }
    }

    @Test
    fun an_empty_ledger_says_so_instead_of_rendering_nothing() {
        val controller = controllerFor(FakeComplianceApi(requests = emptyList(), summary = ErasureRequestSummary()))

        runComposeUiTest {
            setContent { EnglishContent { ObservingDataRequestsTab(controller = controller) } }
            waitForIdle()

            onNodeWithText("No data requests match.").assertExists()
        }
    }

    private val subjectPreview = ErasurePreview(
        totalRows = 5,
        categories = listOf(
            ErasurePreviewCategory("gdpr_erasure_category_profile", 1),
            ErasurePreviewCategory("gdpr_erasure_category_chat_messages", 4),
        ),
    )

    /** The destructive confirm inside the dialog (the row trigger carries the same label). */
    private val confirmButton = hasText("Re-run erasure") and hasClickAction()

    @Test
    fun rerunning_a_failed_erasure_shows_the_counted_blast_radius_and_completes_the_row() {
        val fakeApi = FakeComplianceApi(
            requests = listOf(failedErasure, completedExport),
            summary = ErasureRequestSummary(total = 2, completed = 1, failed = 1),
            preview = ApiResult.Ok(subjectPreview),
            retry = ApiResult.Ok(failedErasure.copy(status = "completed", failureReason = null, rowsAffected = 5)),
            summaryAfterRetry = ErasureRequestSummary(total = 2, completed = 2, failed = 0),
        )
        val controller = controllerFor(fakeApi)

        runComposeUiTest {
            setContent { EnglishContent { ObservingDataRequestsTab(controller = controller) } }
            waitForIdle()

            // Only the failed ERASURE row offers the action; the completed export does not.
            onAllNodesWithText("Re-run erasure").assertCountEquals(1)
            onNodeWithText("Re-run erasure").performClick()
            waitForIdle()

            assertEquals(listOf("user-9"), fakeApi.previewCalls.toList())
            onNodeWithText("Re-run this erasure?").assertExists()
            onNodeWithText("cannot be undone", substring = true).assertExists()
            onNodeWithText("4 chat messages", substring = true).assertExists()

            onAllNodes(confirmButton).onLast().assertIsEnabled().performClick()
            waitForIdle()

            assertEquals(listOf("req-1"), fakeApi.retryCalls.toList())
            onNodeWithText("Re-run this erasure?").assertDoesNotExist()
            onNodeWithText("The token vault refused the revocation.").assertDoesNotExist()
            onNodeWithText("2 requests · 0 running · 0 failed").assertExists()
            onNodeWithText("5 rows").assertExists()
            assertNull(controller.state.value.erasureRetryTarget)
        }
    }

    @Test
    fun a_refused_rerun_keeps_the_dialog_open_and_says_why() {
        val fakeApi = FakeComplianceApi(
            requests = listOf(failedErasure),
            summary = ErasureRequestSummary(total = 1, failed = 1),
            preview = ApiResult.Ok(subjectPreview),
            retry = ApiResult.Failure(ApiError(409, "ERASURE_ALREADY_COMPLETED", "already completed")),
        )
        val controller = controllerFor(fakeApi)

        runComposeUiTest {
            setContent { EnglishContent { ObservingDataRequestsTab(controller = controller) } }
            waitForIdle()

            onNodeWithText("Re-run erasure").performClick()
            waitForIdle()
            onAllNodes(confirmButton).onLast().performClick()
            waitForIdle()

            onNodeWithText("Re-run this erasure?").assertExists()
            onNodeWithText("This erasure already completed. There is nothing left to re-run.").assertExists()
            // The row is untouched: still failed, still carrying its recorded reason.
            onNodeWithText("The token vault refused the revocation.").assertExists()
            assertEquals("failed", controller.state.value.dataRequests.single().status)
        }
    }

    @Test
    fun a_failed_blast_radius_lookup_withholds_the_confirm() {
        val fakeApi = FakeComplianceApi(
            requests = listOf(failedErasure),
            summary = ErasureRequestSummary(total = 1, failed = 1),
            preview = ApiResult.Failure(ApiError(500, "INTERNAL_ERROR", "boom")),
        )
        val controller = controllerFor(fakeApi)

        runComposeUiTest {
            setContent { EnglishContent { ObservingDataRequestsTab(controller = controller) } }
            waitForIdle()

            onNodeWithText("Re-run erasure").performClick()
            waitForIdle()

            onNodeWithText("Could not check what would be erased", substring = true).assertExists()
            onAllNodes(confirmButton).onLast().assertIsNotEnabled()
            assertEquals(emptyList(), fakeApi.retryCalls.toList())
        }
    }
}

private class FakeComplianceApi(
    private val requests: List<ErasureRequest>,
    private val summary: ErasureRequestSummary,
    private val preview: ApiResult<ErasurePreview> = ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused")),
    private val retry: ApiResult<ErasureRequest> = ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused")),
    private val summaryAfterRetry: ErasureRequestSummary = summary,
) : ComplianceApi {
    /** Every (status, requestType) pair the tab asked for, in order. */
    val listCalls: MutableList<Pair<String?, String?>> = mutableListOf()
    val previewCalls: MutableList<String> = mutableListOf()
    val retryCalls: MutableList<String> = mutableListOf()

    override suspend fun listRequests(
        status: String?,
        requestType: String?,
        page: Int,
        pageSize: Int,
    ): ApiResult<PaginatedEnvelope<ErasureRequest>> {
        listCalls += status to requestType
        val filtered: List<ErasureRequest> =
            requests.filter { (status == null || it.status == status) && (requestType == null || it.requestType == requestType) }
        return ApiResult.Ok(PaginatedEnvelope<ErasureRequest>(filtered))
    }

    override suspend fun summary(): ApiResult<ErasureRequestSummary> =
        ApiResult.Ok(if (retryCalls.isEmpty()) summary else summaryAfterRetry)

    override suspend fun previewErasure(subjectUserId: String): ApiResult<ErasurePreview> {
        previewCalls += subjectUserId
        return preview
    }

    override suspend fun retryErasure(erasureRequestId: String): ApiResult<ErasureRequest> {
        retryCalls += erasureRequestId
        return retry
    }
}

private class FakeAdminApiForDataRequestsTest : AdminApi {
    override suspend fun getStats(): ApiResult<AdminStats> = ApiResult.Ok(AdminStats(0, 0, 0, "ok", 0, 0))
    override suspend fun getChannels(search: String?, page: Int, pageSize: Int, sort: String?, isLive: Boolean?) =
        ApiResult.Ok(PaginatedEnvelope<AdminChannel>(emptyList()))
    override suspend fun getUsers(search: String?, page: Int, pageSize: Int, sort: String?, role: String?) =
        ApiResult.Ok(PaginatedEnvelope<AdminUser>(emptyList()))
    override suspend fun getSystem() = ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun getHealth() = ApiResult.Ok(emptyList<AdminServiceHealth>())
    override suspend fun getEvents() = ApiResult.Ok(emptyList<PlatformEvent>())
    override suspend fun getFeatureFlags() = ApiResult.Ok(emptyList<FeatureFlag>())
    override suspend fun setFeatureFlag(body: AdminSetFeatureFlagRequest) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
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
    override suspend fun previewEntitlementGrant(broadcasterId: String, tierId: String) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun issueEntitlementGrant(broadcasterId: String, body: AdminIssueEntitlementGrantRequest) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun getInvoices(broadcasterId: String) = ApiResult.Ok(emptyList<AdminInvoice>())
    override suspend fun refundInvoice(invoiceId: String) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
}

private class FakeIamApiForDataRequestsTest : PlatformIamApi {
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

private class FakePlatformAdminApiForDataRequestsTest : PlatformAdminApi {
    override suspend fun listTenants(search: String?, status: String?, isLive: Boolean?, page: Int, pageSize: Int) =
        ApiResult.Ok(PaginatedEnvelope<AdminTenant>(emptyList()))
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
    ) = ApiResult.Ok(PaginatedEnvelope<IamAuditEntry>(emptyList()))
}
