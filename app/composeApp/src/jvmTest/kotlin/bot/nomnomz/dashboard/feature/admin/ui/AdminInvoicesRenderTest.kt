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
import bot.nomnomz.dashboard.core.network.AdminEntitlementGrantPreview
import bot.nomnomz.dashboard.core.network.AdminGrantTierRequest
import bot.nomnomz.dashboard.core.network.AdminInvoice
import bot.nomnomz.dashboard.core.network.AdminIssueEntitlementGrantRequest
import bot.nomnomz.dashboard.core.network.AdminServiceHealth
import bot.nomnomz.dashboard.core.network.AdminSetFeatureFlagOverrideRequest
import bot.nomnomz.dashboard.core.network.AdminSetFeatureFlagRequest
import bot.nomnomz.dashboard.core.network.AdminStats
import bot.nomnomz.dashboard.core.network.AdminSystem
import bot.nomnomz.dashboard.core.network.AdminTier
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
import kotlin.test.assertEquals

/**
 * S-ADMIN-4c: the Billing tab must render a tenant's real invoices — status, amount and the live dunning
 * state computed by the backend's `Invoice.ResolveDunningStatus` (a "Past due" badge appears ONLY for an
 * open invoice past its due date, never a decorative guess) — and a refund must show the counted blast
 * radius (the exact amount and currency about to move) BEFORE the destructive commit button can be pressed.
 * Asserts against the rendered semantics tree, mirroring [AdminEntitlementGrantRenderTest]'s pattern.
 */
@OptIn(ExperimentalTestApi::class)
class AdminInvoicesRenderTest {

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
    fun loading_a_tenant_renders_its_invoices_with_amount_status_and_the_real_dunning_state() {
        val pastDueInvoice = AdminInvoice(
            id = "inv-1",
            number = "INV-002",
            status = "open",
            amountDueCents = 1_500,
            amountPaidCents = 0,
            currency = "usd",
            issuedAt = "2026-09-05T00:00:00Z",
            dunningStatus = "PastDue",
        )
        val paidInvoice = AdminInvoice(
            id = "inv-2",
            number = "INV-001",
            status = "paid",
            amountDueCents = 1_999,
            amountPaidCents = 1_999,
            currency = "usd",
            issuedAt = "2026-08-01T00:00:00Z",
            paidAt = "2026-08-01T00:00:00Z",
            dunningStatus = "NotDunning",
        )
        val api = FakeAdminApiForInvoiceTest(invoices = listOf(pastDueInvoice, paidInvoice))
        val controller = AdminController(
            api = api,
            iamApi = FakeIamApiForInvoiceTest(),
            platformAdminApi = FakePlatformAdminApiForInvoiceTest(),
        )

        runTest {
            controller.load()
            controller.selectInvoiceBroadcaster("chan-1")
        }

        runComposeUiTest {
            setContent {
                EnglishContent {
                    ObservingBillingTab(controller = controller)
                }
            }
            waitForIdle()

            // The open, past-due invoice shows its amount and the "Past due" badge — the state a refund
            // panel or dunning surface would actually read, never invented in the UI layer.
            onNodeWithText("15.00 USD", substring = true).assertExists()
            onNodeWithText("Past due").assertExists()

            // The paid invoice shows its amount too, but never a "Past due" badge — dunning never applies
            // to a paid invoice, however old.
            onNodeWithText("19.99 USD", substring = true).assertExists()
        }
    }

    @Test
    fun refunding_a_paid_invoice_shows_the_counted_amount_before_the_destructive_button_commits() {
        val paidInvoice = AdminInvoice(
            id = "inv-2",
            number = "INV-001",
            status = "paid",
            amountDueCents = 1_999,
            amountPaidCents = 1_999,
            currency = "usd",
            issuedAt = "2026-08-01T00:00:00Z",
            paidAt = "2026-08-01T00:00:00Z",
            dunningStatus = "NotDunning",
        )
        val api = FakeAdminApiForInvoiceTest(invoices = listOf(paidInvoice))
        val controller = AdminController(
            api = api,
            iamApi = FakeIamApiForInvoiceTest(),
            platformAdminApi = FakePlatformAdminApiForInvoiceTest(),
        )

        runTest {
            controller.load()
            controller.selectInvoiceBroadcaster("chan-1")
        }

        runComposeUiTest {
            setContent {
                EnglishContent {
                    ObservingBillingTab(controller = controller)
                }
            }
            waitForIdle()

            // Clicking the row's quiet outline trigger opens the confirmation — refund has NOT happened yet.
            onNodeWithText("Refund").performClick()
            waitForIdle()
            assertEquals(0, api.refundCallCount)

            // The counted blast radius — the exact amount and currency about to move — renders in the
            // confirmation dialog's own body BEFORE the destructive commit button is pressed (the row
            // behind the dialog still shows its own "19.99 USD" too, so the assertion targets the dialog's
            // distinct sentence rather than the bare amount, which now matches twice).
            onNodeWithText("This refunds 19.99 USD", substring = true).assertExists()

            // The dialog's destructive commit is its own distinctly-labelled button — never the same text as
            // the row trigger, so a screen reader (and this assertion) never conflates "open the confirm"
            // with "commit the refund".
            onNodeWithText("Confirm refund").performClick()
            waitForIdle()

            assertEquals(1, api.refundCallCount)
            assertEquals("inv-2", api.lastRefundedInvoiceId)
        }
    }
}

private class FakeAdminApiForInvoiceTest(
    private var invoices: List<AdminInvoice> = emptyList(),
) : AdminApi {
    var refundCallCount: Int = 0
        private set
    var lastRefundedInvoiceId: String? = null
        private set

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
    override suspend fun previewEntitlementGrant(broadcasterId: String, tierId: String): ApiResult<AdminEntitlementGrantPreview> =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun issueEntitlementGrant(broadcasterId: String, body: AdminIssueEntitlementGrantRequest) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun getInvoices(broadcasterId: String): ApiResult<List<AdminInvoice>> = ApiResult.Ok(invoices)

    // Mirrors the real backend: a refund flips the invoice to "refunded" and records the amount, so a
    // reload after the confirm shows the state actually changed, not merely that the call was made.
    override suspend fun refundInvoice(invoiceId: String): ApiResult<AdminInvoice> {
        refundCallCount++
        lastRefundedInvoiceId = invoiceId
        val refunded = invoices.first { it.id == invoiceId }.copy(
            status = "refunded",
            amountRefundedCents = invoices.first { it.id == invoiceId }.amountPaidCents,
            refundedAt = "2026-09-05T12:00:00Z",
        )
        invoices = invoices.map { if (it.id == invoiceId) refunded else it }
        return ApiResult.Ok(refunded)
    }
}

private class FakeIamApiForInvoiceTest : PlatformIamApi {
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

private class FakePlatformAdminApiForInvoiceTest : PlatformAdminApi {
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
