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
import bot.nomnomz.dashboard.core.network.CreatePrincipalBody
import bot.nomnomz.dashboard.core.network.CrossTenantAbuseHit
import bot.nomnomz.dashboard.core.network.CrossTenantAbuseSignal
import bot.nomnomz.dashboard.core.network.FeatureFlag
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
import bot.nomnomz.dashboard.core.network.TrustSafetyApi
import bot.nomnomz.dashboard.core.network.TrustSafetyReviewItem
import bot.nomnomz.dashboard.feature.admin.state.AdminController
import kotlinx.coroutines.test.runTest
import kotlin.test.Test

/**
 * S-ADMIN-8a: the trust & safety tab must render REAL queued automatic actions with the evidence that
 * caused them, and overturning one must show the exact real effect it will reverse — the server-computed
 * [TrustSafetyReviewItem.reversalPreview] — BEFORE the operator commits to it, never after.
 */
@OptIn(ExperimentalTestApi::class)
class AdminTrustSafetyRenderTest {

    private val queuedItem = TrustSafetyReviewItem(
        detectionId = "det-1",
        broadcasterId = "chan-1",
        channelName = "streamer_a",
        subjectPlatformUserId = "bot-99",
        subjectDisplayName = "SuspiciousBot99",
        provider = "twitch",
        messageText = "free f0ll0ws check bio",
        signals = "CosmeticAbuse",
        confidence = "High",
        outcome = "DeleteAndEscalate",
        reason = "High confidence — message removed and routed to the escalation ladder.",
        detectedAt = "2026-09-06T10:00:00Z",
        reversalPreview = "Removes the automatic Twitch timeout issued against SuspiciousBot99 in streamer_a.",
    )

    private val crossTenantSignal = CrossTenantAbuseSignal(
        provider = "twitch",
        subjectPlatformUserId = "raider-1",
        subjectDisplayName = "RaidBot1",
        tenantCount = 2,
        detectionCount = 2,
        hits = listOf(
            CrossTenantAbuseHit(
                broadcasterId = "chan-1",
                channelName = "streamer_a",
                detectionId = "det-2",
                confidence = "High",
                outcome = "Flag",
                reason = "High confidence — flagged for review, no action taken.",
                detectedAt = "2026-09-06T09:00:00Z",
            ),
            CrossTenantAbuseHit(
                broadcasterId = "chan-2",
                channelName = "streamer_b",
                detectionId = "det-3",
                confidence = "High",
                outcome = "Flag",
                reason = "High confidence — flagged for review, no action taken.",
                detectedAt = "2026-09-06T09:05:00Z",
            ),
        ),
    )

    @Composable
    private fun EnglishContent(content: @Composable () -> Unit) {
        AppEnvironment(tag = "en") {
            NomNomzTheme { content() }
        }
    }

    @Composable
    private fun ObservingTrustSafetyTab(controller: AdminController) {
        val state by controller.state.collectAsState()
        TrustSafetyTab(state = state, controller = controller)
    }

    private fun controllerFor(
        signals: List<CrossTenantAbuseSignal> = emptyList(),
        queue: List<TrustSafetyReviewItem> = emptyList(),
        fakeApi: FakeTrustSafetyApi = FakeTrustSafetyApi(signals = signals, queue = queue),
    ): Pair<AdminController, FakeTrustSafetyApi> {
        val controller = AdminController(
            api = FakeAdminApiForTrustSafetyTest(),
            iamApi = FakeIamApiForTrustSafetyTest(),
            platformAdminApi = FakePlatformAdminApiForTrustSafetyTest(),
            trustSafetyApi = fakeApi,
        )
        runTest {
            controller.setTrustSafetyJustification("Ticket #4471 — cross-channel raid-chat spam.")
            controller.loadCrossTenantSignals()
            controller.loadReviewQueue()
        }
        return controller to fakeApi
    }

    @Test
    fun the_review_queue_renders_the_real_queued_action_with_its_evidence() {
        val (controller, _) = controllerFor(queue = listOf(queuedItem))

        runComposeUiTest {
            setContent { EnglishContent { ObservingTrustSafetyTab(controller = controller) } }
            waitForIdle()

            onNodeWithText("Automatic actions awaiting review").assertExists()
            onNodeWithText("SuspiciousBot99", substring = true).assertExists()
            onNodeWithText("free f0ll0ws check bio", substring = true).assertExists()
            onNodeWithText(
                "High confidence — message removed and routed to the escalation ladder.",
                substring = true,
            ).assertExists()
        }
    }

    @Test
    fun cross_tenant_signals_render_with_the_real_tenants_and_detections_that_back_them() {
        val (controller, _) = controllerFor(signals = listOf(crossTenantSignal))

        runComposeUiTest {
            setContent { EnglishContent { ObservingTrustSafetyTab(controller = controller) } }
            waitForIdle()

            onNodeWithText("Actors seen in more than one channel").assertExists()
            onNodeWithText("RaidBot1", substring = true).assertExists()
            onNodeWithText("Seen in 2 channels", substring = true).assertExists()
            onNodeWithText("streamer_a", substring = true).assertExists()
            onNodeWithText("streamer_b", substring = true).assertExists()
        }
    }

    @Test
    fun overturn_shows_the_real_reversal_preview_before_it_commits() {
        val (controller, fakeApi) = controllerFor(queue = listOf(queuedItem))

        runComposeUiTest {
            setContent { EnglishContent { ObservingTrustSafetyTab(controller = controller) } }
            waitForIdle()

            onNodeWithText("Overturn").performClick()
            waitForIdle()

            // The confirm dialog shows the SERVER-COMPUTED blast radius before anything is sent.
            onNodeWithText(
                "Removes the automatic Twitch timeout issued against SuspiciousBot99 in streamer_a.",
                substring = true,
            ).assertExists()
            onNodeWithText("Overturn this automatic action?").assertExists()
            assert(fakeApi.overturnCalls.isEmpty()) {
                "the overturn must not be sent until the operator confirms the dialog"
            }
        }
    }
}

private class FakeTrustSafetyApi(
    private val signals: List<CrossTenantAbuseSignal> = emptyList(),
    private val queue: List<TrustSafetyReviewItem> = emptyList(),
) : TrustSafetyApi {
    val overturnCalls: MutableList<String> = mutableListOf()
    val confirmCalls: MutableList<String> = mutableListOf()

    override suspend fun getCrossTenantSignals(
        justification: String,
    ): ApiResult<List<CrossTenantAbuseSignal>> = ApiResult.Ok(signals)

    override suspend fun getReviewQueue(
        justification: String,
        page: Int,
        pageSize: Int,
    ): ApiResult<PaginatedEnvelope<TrustSafetyReviewItem>> = ApiResult.Ok(PaginatedEnvelope(queue))

    override suspend fun confirm(detectionId: String, justification: String): ApiResult<Unit> {
        confirmCalls += detectionId
        return ApiResult.Ok(Unit)
    }

    override suspend fun overturn(detectionId: String, justification: String): ApiResult<Unit> {
        overturnCalls += detectionId
        return ApiResult.Ok(Unit)
    }
}

private class FakeAdminApiForTrustSafetyTest : AdminApi {
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

private class FakeIamApiForTrustSafetyTest : PlatformIamApi {
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

private class FakePlatformAdminApiForTrustSafetyTest : PlatformAdminApi {
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
