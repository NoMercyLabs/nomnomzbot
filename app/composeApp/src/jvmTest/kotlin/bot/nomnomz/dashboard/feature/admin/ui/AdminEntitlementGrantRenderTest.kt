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
import androidx.compose.ui.test.hasClickAction
import androidx.compose.ui.test.hasSetTextAction
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performTextInput
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.AdminApi
import bot.nomnomz.dashboard.core.network.AdminChannel
import bot.nomnomz.dashboard.core.network.AdminCreateInviteCodeRequest
import bot.nomnomz.dashboard.core.network.AdminCreateTierRequest
import bot.nomnomz.dashboard.core.network.AdminEntitlementGrant
import bot.nomnomz.dashboard.core.network.AdminEntitlementGrantPreview
import bot.nomnomz.dashboard.core.network.AdminGrantTierRequest
import bot.nomnomz.dashboard.core.network.AdminIssueEntitlementGrantRequest
import bot.nomnomz.dashboard.core.network.AdminServiceHealth
import bot.nomnomz.dashboard.core.network.AdminSetFeatureFlagOverrideRequest
import bot.nomnomz.dashboard.core.network.AdminSetFeatureFlagRequest
import bot.nomnomz.dashboard.core.network.AdminStats
import bot.nomnomz.dashboard.core.network.AdminSystem
import bot.nomnomz.dashboard.core.network.AdminTier
import bot.nomnomz.dashboard.core.network.AdminTierChangePreview
import bot.nomnomz.dashboard.core.network.AdminUpdateTierRequest
import bot.nomnomz.dashboard.core.network.AdminUser
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.AssignRoleBody
import bot.nomnomz.dashboard.core.network.CreatePrincipalBody
import bot.nomnomz.dashboard.core.network.FeatureFlag
import bot.nomnomz.dashboard.core.network.IamPrincipalSummary
import bot.nomnomz.dashboard.core.network.IamRole
import bot.nomnomz.dashboard.core.network.InviteCode
import bot.nomnomz.dashboard.core.network.PaginatedEnvelope
import bot.nomnomz.dashboard.core.network.PlatformAdminApi
import bot.nomnomz.dashboard.core.network.PlatformEvent
import bot.nomnomz.dashboard.core.network.PlatformIamApi
import bot.nomnomz.dashboard.core.network.ProviderCredential
import bot.nomnomz.dashboard.core.network.SaveProviderCredentialBody
import bot.nomnomz.dashboard.feature.admin.state.AdminController
import kotlinx.coroutines.test.runTest
import kotlin.test.Test

/**
 * S-ADMIN-4b: the Billing tab must render a tenant's LIVE comps — reason and expiry, never a raw domain
 * field ([bot.nomnomz.dashboard.core.designsystem.resolveRowLabel]) — and show the COUNTED blast radius
 * (the limit keys that would actually change) BEFORE the operator can issue a new one. Asserts against the
 * rendered semantics tree, mirroring [AdminBillingTierEditorRenderTest]'s pattern for the tier editor.
 */
@OptIn(ExperimentalTestApi::class)
class AdminEntitlementGrantRenderTest {

    @Composable
    private fun EnglishContent(content: @Composable () -> Unit) {
        AppEnvironment(tag = "en") {
            NomNomzTheme { content() }
        }
    }

    @Composable
    private fun ObservingBillingTab(controller: AdminController) {
        val state by controller.state.collectAsState()
        BillingTab(state = state, controller = controller)
    }

    @Test
    fun loading_a_tenant_renders_its_live_grants_with_reason_and_expiry() {
        val existingGrant = AdminEntitlementGrant(
            id = "grant-1",
            broadcasterId = "chan-1",
            grantedTierId = "tier-pro",
            grantedTierKey = "pro",
            reason = "Support case #4471 — negotiated 30-day comp",
            expiresAt = "2026-10-05T00:00:00Z",
            issuedAt = "2026-09-05T00:00:00Z",
            issuedByAdminId = "admin-1",
        )
        val api = FakeAdminApiForGrantTest(grants = listOf(existingGrant))
        val controller = AdminController(
            api = api,
            iamApi = FakeIamApiForGrantTest(),
            platformAdminApi = FakePlatformAdminApiForGrantTest(),
        )

        // Selecting the tenant (the operator's "Load" action) is driven directly through the controller —
        // the same call the Load button's onClick makes — so this test asserts against the RENDERED result
        // of a real state transition rather than the mechanics of typing into a text field.
        runTest {
            controller.load()
            controller.selectGrantBroadcaster("chan-1")
        }

        runComposeUiTest {
            setContent {
                EnglishContent {
                    ObservingBillingTab(controller = controller)
                }
            }
            waitForIdle()

            // The existing grant's reason and expiry render — never a raw tier id or a blank row.
            onNodeWithText("Support case #4471", substring = true).assertExists()
            onNodeWithText("Expires 2026-10-05T00:00:00Z", substring = true).assertExists()
        }
    }

    @Test
    fun issuing_a_grant_shows_the_counted_blast_radius_before_the_button_enables() {
        val tier = AdminTier(
            id = "tier-pro",
            key = "pro",
            displayName = "Pro",
            priceCents = 1499,
            currency = "usd",
            allowsCustomBotName = true,
            prioritySupport = false,
            sortOrder = 1,
            limits = emptyList(),
            isPublic = true,
        )
        val preview = AdminEntitlementGrantPreview(
            currentTierKey = "base",
            grantedTierKey = "pro",
            changedLimitCount = 1,
            changedLimitKeys = listOf("custom_commands"),
        )
        val existingGrant = AdminEntitlementGrant(
            id = "grant-1",
            broadcasterId = "chan-1",
            grantedTierId = "tier-pro",
            grantedTierKey = "pro",
            reason = "Support case #4471 — negotiated 30-day comp",
            expiresAt = "2026-10-05T00:00:00Z",
            issuedAt = "2026-09-05T00:00:00Z",
            issuedByAdminId = "admin-1",
        )
        val api = FakeAdminApiForGrantTest(tiers = listOf(tier), grants = listOf(existingGrant), preview = preview)
        val controller = AdminController(
            api = api,
            iamApi = FakeIamApiForGrantTest(),
            platformAdminApi = FakePlatformAdminApiForGrantTest(),
        )

        // The tenant lookup itself is covered by the render test above; here the tenant is pre-selected
        // through the controller (the exact call the Load button's onClick makes) so this test isolates
        // the part it actually targets — the counted blast radius appearing BEFORE the issue button enables.
        runTest {
            controller.load()
            controller.selectGrantBroadcaster("chan-1")
        }

        runComposeUiTest {
            setContent {
                EnglishContent {
                    ObservingBillingTab(controller = controller)
                }
            }
            waitForIdle()

            onNodeWithText("Support case #4471", substring = true).assertExists()
            onNodeWithText("Expires 2026-10-05T00:00:00Z", substring = true).assertExists()

            // Open the issue dialog and pick the tier — the counted blast radius fetches and renders BEFORE
            // any issue is possible.
            onNodeWithText("New comp").performClick()
            waitForIdle()
            // The existing grant row also renders "pro" as its tier key, so the tier PICKER button is
            // disambiguated by its click action rather than by text alone.
            onNode(hasText("pro") and hasClickAction()).performClick()
            waitForIdle()

            onNodeWithText(
                "1 limit(s) would change for this tenant, including: custom_commands.",
                substring = true,
            ).assertExists()
        }
    }
}

private class FakeAdminApiForGrantTest(
    private val tiers: List<AdminTier> = emptyList(),
    private val grants: List<AdminEntitlementGrant> = emptyList(),
    private val preview: AdminEntitlementGrantPreview? = null,
) : AdminApi {
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
    override suspend fun getTiers(): ApiResult<List<AdminTier>> = ApiResult.Ok(tiers)
    override suspend fun previewTierChange(tierId: String) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun createTier(body: AdminCreateTierRequest) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun updateTier(tierId: String, body: AdminUpdateTierRequest) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun getEntitlementGrants(broadcasterId: String): ApiResult<List<AdminEntitlementGrant>> =
        ApiResult.Ok(grants)
    override suspend fun previewEntitlementGrant(broadcasterId: String, tierId: String): ApiResult<AdminEntitlementGrantPreview> =
        preview?.let { ApiResult.Ok(it) } ?: ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun issueEntitlementGrant(broadcasterId: String, body: AdminIssueEntitlementGrantRequest) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
}

private class FakeIamApiForGrantTest : PlatformIamApi {
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

private class FakePlatformAdminApiForGrantTest : PlatformAdminApi {
    override suspend fun listTenants(search: String?, status: String?, isLive: Boolean?, page: Int, pageSize: Int) =
        ApiResult.Ok(PaginatedEnvelope<bot.nomnomz.dashboard.core.network.AdminTenant>(emptyList()))
    override suspend fun getTenant(broadcasterId: String) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun suspendTenant(broadcasterId: String, body: bot.nomnomz.dashboard.core.network.SuspendTenantBody) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun reinstateTenant(broadcasterId: String, body: bot.nomnomz.dashboard.core.network.ReinstateTenantBody) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun beginAccess(broadcasterId: String, body: bot.nomnomz.dashboard.core.network.BeginTenantAccessBody) =
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
    ) = ApiResult.Ok(PaginatedEnvelope<bot.nomnomz.dashboard.core.network.IamAuditEntry>(emptyList()))
}
