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
import bot.nomnomz.dashboard.core.network.CrossTenantAbuseSignal
import bot.nomnomz.dashboard.core.network.FeatureFlag
import bot.nomnomz.dashboard.core.network.IamAuditEntry
import bot.nomnomz.dashboard.core.network.IamPrincipalSummary
import bot.nomnomz.dashboard.core.network.IamRole
import bot.nomnomz.dashboard.core.network.InviteCode
import bot.nomnomz.dashboard.core.network.NetworkBlock
import bot.nomnomz.dashboard.core.network.NetworkBlockAffectedTenant
import bot.nomnomz.dashboard.core.network.NetworkBlockPreview
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
 * S-ADMIN-8b: the network-wide block is the most dangerous control in the product, so the surface must
 * show the operator the REAL counted blast radius before they can even reach the apply action, and once
 * applied must render an active block naming who applied it, when, and why.
 */
@OptIn(ExperimentalTestApi::class)
class AdminNetworkBlockRenderTest {

    private val preview = NetworkBlockPreview(
        targetUserId = "user-99",
        targetTwitchUserId = "troll-99",
        targetDisplayName = "Troll99",
        tenantCount = 3,
        tenants = listOf(
            NetworkBlockAffectedTenant(broadcasterId = "chan-1", channelName = "streamer_a"),
            NetworkBlockAffectedTenant(broadcasterId = "chan-2", channelName = "streamer_b"),
            NetworkBlockAffectedTenant(broadcasterId = "chan-3", channelName = "streamer_c"),
        ),
    )

    private val activeBlock = NetworkBlock(
        id = "block-1",
        targetUserId = "user-99",
        targetTwitchUserId = "troll-99",
        targetDisplayName = "Troll99",
        reason = "cross-channel raid",
        justification = "Ticket #9001 — coordinated raid harassment.",
        appliedByPrincipalId = "operator-1",
        appliedAt = "2026-09-06T12:00:00Z",
        tenantCount = 3,
        channelCount = 3,
        status = "active",
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
        fakeApi: FakeNetworkBlockTrustSafetyApi,
    ): AdminController {
        val controller = AdminController(
            api = FakeAdminApiForNetworkBlockTest(),
            iamApi = FakeIamApiForNetworkBlockTest(),
            platformAdminApi = FakePlatformAdminApiForNetworkBlockTest(),
            trustSafetyApi = fakeApi,
        )
        runTest {
            controller.setTrustSafetyJustification("Ticket #9001 — coordinated raid harassment.")
        }
        return controller
    }

    @Test
    fun preview_shows_the_real_counted_blast_radius_before_apply_can_be_reached() {
        val fakeApi = FakeNetworkBlockTrustSafetyApi(preview = preview)
        val controller = controllerFor(fakeApi)

        runComposeUiTest {
            setContent { EnglishContent { ObservingTrustSafetyTab(controller = controller) } }
            waitForIdle()

            controller.setNetworkBlockTargetTwitchUserId("troll-99")
            waitForIdle()

            onNodeWithText("Preview blast radius").performClick()
            waitForIdle()

            // The real, server-computed count is on screen before apply is reachable.
            onNodeWithText("Touches 3 channel(s)", substring = true).assertExists()
            assert(fakeApi.applyCalls.isEmpty()) {
                "apply must not be reachable before a real preview is shown"
            }

            onNodeWithText("Apply network-wide block").performClick()
            waitForIdle()

            // The destructive confirm dialog repeats the exact count the operator was just shown.
            onNodeWithText("Block across every channel?").assertExists()
            onNodeWithText(
                "This bans the account in 3 channel(s) right now",
                substring = true,
            ).assertExists()
            assert(fakeApi.applyCalls.isEmpty()) {
                "the block must not be applied until the confirm dialog is accepted"
            }
        }
    }

    @Test
    fun an_active_block_renders_who_applied_it_when_and_why() {
        val fakeApi = FakeNetworkBlockTrustSafetyApi(blocks = listOf(activeBlock))
        val controller = controllerFor(fakeApi)

        runComposeUiTest {
            setContent { EnglishContent { ObservingTrustSafetyTab(controller = controller) } }
            waitForIdle()

            onNodeWithText("Active network blocks").assertExists()
            onNodeWithText("Troll99", substring = true).assertExists()
            onNodeWithText("operator-1", substring = true).assertExists()
            onNodeWithText("2026-09-06T12:00:00Z", substring = true).assertExists()
            onNodeWithText("Active — enforced across 3 channel(s)", substring = true).assertExists()
            onNodeWithText("Lift").assertExists()
        }
    }
}

private class FakeNetworkBlockTrustSafetyApi(
    private val preview: NetworkBlockPreview? = null,
    private val blocks: List<NetworkBlock> = emptyList(),
) : TrustSafetyApi {
    val applyCalls: MutableList<String> = mutableListOf()

    override suspend fun getCrossTenantSignals(justification: String) = ApiResult.Ok(emptyList<CrossTenantAbuseSignal>())

    override suspend fun getReviewQueue(justification: String, page: Int, pageSize: Int) =
        ApiResult.Ok(PaginatedEnvelope(emptyList<TrustSafetyReviewItem>()))

    override suspend fun confirm(detectionId: String, justification: String) = ApiResult.Ok(Unit)

    override suspend fun overturn(detectionId: String, justification: String) = ApiResult.Ok(Unit)

    override suspend fun previewNetworkBlock(
        targetTwitchUserId: String,
        justification: String,
    ): ApiResult<NetworkBlockPreview> =
        preview?.let { ApiResult.Ok(it) } ?: ApiResult.Failure(ApiError(404, "NOT_FOUND", "unknown target"))

    override suspend fun applyNetworkBlock(
        targetTwitchUserId: String,
        reason: String?,
        justification: String,
        confirmedTenantCount: Int,
    ): ApiResult<NetworkBlock> {
        applyCalls += targetTwitchUserId
        return ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused in this test"))
    }

    override suspend fun listNetworkBlocks(justification: String): ApiResult<List<NetworkBlock>> =
        ApiResult.Ok(blocks)

    override suspend fun liftNetworkBlock(blockId: String, justification: String) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused in this test"))
}

private class FakeAdminApiForNetworkBlockTest : AdminApi {
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

private class FakeIamApiForNetworkBlockTest : PlatformIamApi {
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

private class FakePlatformAdminApiForNetworkBlockTest : PlatformAdminApi {
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
