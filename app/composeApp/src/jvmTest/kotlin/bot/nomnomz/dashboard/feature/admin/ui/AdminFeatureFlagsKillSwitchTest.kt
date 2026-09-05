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
import androidx.compose.ui.test.isToggleable
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
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
import bot.nomnomz.dashboard.core.network.AdminUser
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.AssignRoleBody
import bot.nomnomz.dashboard.core.network.BeginTenantAccessBody
import bot.nomnomz.dashboard.core.network.CreatePrincipalBody
import bot.nomnomz.dashboard.core.network.FeatureFlag
import bot.nomnomz.dashboard.core.network.FeatureFlagBlastRadiusDto
import bot.nomnomz.dashboard.core.network.IamAuditEntry
import bot.nomnomz.dashboard.core.network.IamPrincipalSummary
import bot.nomnomz.dashboard.core.network.IamRole
import bot.nomnomz.dashboard.core.network.InviteCode
import bot.nomnomz.dashboard.core.network.PaginatedEnvelope
import bot.nomnomz.dashboard.core.network.PlatformAdminApi
import bot.nomnomz.dashboard.core.network.PlatformEvent
import bot.nomnomz.dashboard.core.network.PlatformIamApi
import bot.nomnomz.dashboard.core.network.ReinstateTenantBody
import bot.nomnomz.dashboard.core.network.SuspendTenantBody
import bot.nomnomz.dashboard.feature.admin.state.AdminController
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

/**
 * S-ADMIN-5: the feature-flag admin console must render each flag's staged-rollout cohort state (the
 * percentage ramp) and its per-tenant override, AND — because turning a flag's global toggle off is a
 * platform-wide kill switch — it must show the COUNTED blast radius and let the operator cancel BEFORE the
 * toggle actually commits, never flip it blind. Both assertions read the rendered semantics tree: a
 * regression that wires the preview through state but paints nothing, or commits on the first click instead
 * of after confirmation, must fail this test.
 */
@OptIn(ExperimentalTestApi::class)
class AdminFeatureFlagsKillSwitchTest {

    @Composable
    private fun EnglishFlagsTab(controller: AdminController) {
        val state by controller.state.collectAsState()
        AppEnvironment(tag = "en") {
            NomNomzTheme { FeatureFlagsTab(state = state, controller = controller) }
        }
    }

    @Test
    fun the_tab_renders_the_rollout_cohort_percentage_and_the_per_tenant_override_state() {
        val api = KillSwitchFakeAdminApi(
            flags = listOf(
                FeatureFlag(
                    key = "integration:spotify",
                    description = "Spotify music provider",
                    isEnabledGlobally = true,
                    rolloutPercentage = 42,
                ),
            ),
        )
        val controller = AdminController(api = api, iamApi = KillSwitchNoopIamApi(), platformAdminApi = KillSwitchNoopPlatformAdminApi())
        runTest { controller.load() }

        runComposeUiTest {
            setContent { EnglishFlagsTab(controller) }
            waitForIdle()

            // The staged-rollout cohort — the percentage of tenants currently ramped in — must be on screen,
            // not just held in AdminState.featureFlags.
            onNodeWithText("Enabled — 42%").assertExists()
            // The per-tenant override controls (the internal/beta opt-in or per-channel kill-switch) must
            // render alongside the cohort state, not be a separate screen the operator has to find.
            onNodeWithText("Broadcaster id").assertExists()
            onNodeWithText("Override: enable").assertExists()
            onNodeWithText("Override: disable").assertExists()
            onNodeWithText("Clear override").assertExists()
        }
    }

    @Test
    fun turning_a_flag_off_shows_the_counted_blast_radius_before_it_commits() {
        val api = KillSwitchFakeAdminApi(
            flags = listOf(
                FeatureFlag(key = "integration:spotify", isEnabledGlobally = true, rolloutPercentage = 100),
            ),
            blastRadius = FeatureFlagBlastRadiusDto(
                tenantsAffected = 7,
                sampleChannelNames = listOf("acme", "beta-stream"),
            ),
        )
        val controller = AdminController(api = api, iamApi = KillSwitchNoopIamApi(), platformAdminApi = KillSwitchNoopPlatformAdminApi())
        runTest { controller.load() }

        runComposeUiTest {
            setContent { EnglishFlagsTab(controller) }
            waitForIdle()

            onNode(isToggleable()).performClick()
            waitForIdle()

            // The counted number and a recognisable sample must be on screen BEFORE any commit happened.
            onNodeWithText(
                "This turns the flag off platform-wide. 7 active channel(s) with no override will lose it " +
                    "right now, including: acme, beta-stream.",
            ).assertExists()
            assertTrue(api.setFeatureFlagCalls.isEmpty(), "opening the confirm dialog must not itself commit the toggle")

            onNodeWithText("Turn off").performClick()
            waitForIdle()

            assertEquals(1, api.setFeatureFlagCalls.size)
            assertFalse(api.setFeatureFlagCalls.single().isEnabledGlobally, "confirming must commit the flag as OFF")
        }
    }

    @Test
    fun cancelling_the_kill_switch_confirm_leaves_the_flag_untouched() {
        val api = KillSwitchFakeAdminApi(
            flags = listOf(
                FeatureFlag(key = "integration:spotify", isEnabledGlobally = true, rolloutPercentage = 100),
            ),
            blastRadius = FeatureFlagBlastRadiusDto(tenantsAffected = 3, sampleChannelNames = listOf("acme")),
        )
        val controller = AdminController(api = api, iamApi = KillSwitchNoopIamApi(), platformAdminApi = KillSwitchNoopPlatformAdminApi())
        runTest { controller.load() }

        runComposeUiTest {
            setContent { EnglishFlagsTab(controller) }
            waitForIdle()

            onNode(isToggleable()).performClick()
            waitForIdle()
            onNodeWithText("Cancel").performClick()
            waitForIdle()

            assertTrue(api.setFeatureFlagCalls.isEmpty(), "Cancel must never commit the kill switch")
            onNodeWithText("Enabled — 100%").assertExists()
        }
    }
}

private class KillSwitchFakeAdminApi(
    private val flags: List<FeatureFlag>,
    private val blastRadius: FeatureFlagBlastRadiusDto = FeatureFlagBlastRadiusDto(),
) : AdminApi {
    val setFeatureFlagCalls: MutableList<AdminSetFeatureFlagRequest> = mutableListOf()

    override suspend fun getStats(): ApiResult<AdminStats> = ApiResult.Ok(AdminStats(0, 0, 0, "ok", 0, 0))
    override suspend fun getChannels(search: String?, page: Int, pageSize: Int, sort: String?, isLive: Boolean?) =
        ApiResult.Ok(PaginatedEnvelope<AdminChannel>(emptyList()))
    override suspend fun getUsers(search: String?, page: Int, pageSize: Int, sort: String?, role: String?) =
        ApiResult.Ok(PaginatedEnvelope<AdminUser>(emptyList()))
    override suspend fun getSystem() = ApiResult.Ok(AdminSystem("ok", emptyList(), "1.0", 0, 0.0))
    override suspend fun getHealth() = ApiResult.Ok(emptyList<AdminServiceHealth>())
    override suspend fun getEvents() = ApiResult.Ok(emptyList<PlatformEvent>())
    override suspend fun getFeatureFlags(): ApiResult<List<FeatureFlag>> = ApiResult.Ok(flags)
    override suspend fun setFeatureFlag(body: AdminSetFeatureFlagRequest): ApiResult<FeatureFlag> {
        setFeatureFlagCalls += body
        return ApiResult.Ok(
            FeatureFlag(
                key = body.key,
                description = body.description,
                isEnabledGlobally = body.isEnabledGlobally,
                rolloutPercentage = body.rolloutPercentage,
            ),
        )
    }
    override suspend fun setFeatureFlagOverride(flagKey: String, broadcasterId: String, body: AdminSetFeatureFlagOverrideRequest) =
        ApiResult.Ok(Unit)
    override suspend fun deleteFeatureFlagOverride(flagKey: String, broadcasterId: String) = ApiResult.Ok(Unit)
    override suspend fun previewFeatureFlagBlastRadius(flagKey: String): ApiResult<FeatureFlagBlastRadiusDto> =
        ApiResult.Ok(blastRadius)
    override suspend fun getInviteCodes(page: Int, pageSize: Int) = ApiResult.Ok(PaginatedEnvelope<InviteCode>(emptyList()))
    override suspend fun createInviteCode(body: AdminCreateInviteCodeRequest) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun revokeInviteCode(inviteCodeId: String) = ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun grantTier(broadcasterId: String, body: AdminGrantTierRequest) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun grantFounderBadge(broadcasterId: String) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun impersonate(subjectUserId: String, accessGrantId: String, justification: String) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun endImpersonation(accessGrantId: String) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun getProviderCredentials() = ApiResult.Ok(emptyList<bot.nomnomz.dashboard.core.network.ProviderCredential>())
    override suspend fun saveProviderCredential(provider: String, body: bot.nomnomz.dashboard.core.network.SaveProviderCredentialBody) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun clearProviderCredential(provider: String) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun getTiers() = ApiResult.Ok(emptyList<bot.nomnomz.dashboard.core.network.AdminTier>())
    override suspend fun createTier(body: bot.nomnomz.dashboard.core.network.AdminCreateTierRequest) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun previewTierChange(tierId: String) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun updateTier(tierId: String, body: bot.nomnomz.dashboard.core.network.AdminUpdateTierRequest) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun getEntitlementGrants(broadcasterId: String) =
        ApiResult.Ok(emptyList<bot.nomnomz.dashboard.core.network.AdminEntitlementGrant>())
    override suspend fun previewEntitlementGrant(broadcasterId: String, tierId: String) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun issueEntitlementGrant(broadcasterId: String, body: bot.nomnomz.dashboard.core.network.AdminIssueEntitlementGrantRequest) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun getInvoices(broadcasterId: String) =
        ApiResult.Ok(emptyList<bot.nomnomz.dashboard.core.network.AdminInvoice>())
    override suspend fun refundInvoice(invoiceId: String) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
}

private class KillSwitchNoopIamApi : PlatformIamApi {
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

private class KillSwitchNoopPlatformAdminApi : PlatformAdminApi {
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
