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

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.width
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.SemanticsNodeInteractionsProvider
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onLast
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.runComposeUiTest
import androidx.compose.ui.unit.dp
import bot.nomnomz.dashboard.core.designsystem.component.TabsList
import bot.nomnomz.dashboard.core.designsystem.component.TabsTrigger
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.AdminApi
import bot.nomnomz.dashboard.core.network.AdminChannel
import bot.nomnomz.dashboard.core.network.AdminCreateInviteCodeRequest
import bot.nomnomz.dashboard.core.network.AdminCreateTierRequest
import bot.nomnomz.dashboard.core.network.AdminEntitlementGrant
import bot.nomnomz.dashboard.core.network.AdminEntitlementGrantPreview
import bot.nomnomz.dashboard.core.network.AdminEventReplayPreview
import bot.nomnomz.dashboard.core.network.AdminEventReplayResult
import bot.nomnomz.dashboard.core.network.AdminEventSubTenantHealth
import bot.nomnomz.dashboard.core.network.AdminGrantTierRequest
import bot.nomnomz.dashboard.core.network.AdminInvoice
import bot.nomnomz.dashboard.core.network.AdminIssueEntitlementGrantRequest
import bot.nomnomz.dashboard.core.network.AdminReplayableProjection
import bot.nomnomz.dashboard.core.network.AdminScheduledJob
import bot.nomnomz.dashboard.core.network.AdminScheduledJobRetryResult
import bot.nomnomz.dashboard.core.network.AdminServiceHealth
import bot.nomnomz.dashboard.core.network.AdminSetFeatureFlagOverrideRequest
import bot.nomnomz.dashboard.core.network.AdminSetFeatureFlagRequest
import bot.nomnomz.dashboard.core.network.AdminStats
import bot.nomnomz.dashboard.core.network.AdminSupportApi
import bot.nomnomz.dashboard.core.network.AdminSystem
import bot.nomnomz.dashboard.core.network.AdminTenant
import bot.nomnomz.dashboard.core.network.AdminTenantErrorBudget
import bot.nomnomz.dashboard.core.network.AdminTenantUsage
import bot.nomnomz.dashboard.core.network.AdminTier
import bot.nomnomz.dashboard.core.network.AdminUpdateTierRequest
import bot.nomnomz.dashboard.core.network.AdminUser
import bot.nomnomz.dashboard.core.network.AdminWebhookDelivery
import bot.nomnomz.dashboard.core.network.AdminWebhookReplayResult
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
import bot.nomnomz.dashboard.core.network.NetworkBlockPreview
import bot.nomnomz.dashboard.core.network.PaginatedEnvelope
import bot.nomnomz.dashboard.core.network.PlatformAdminApi
import bot.nomnomz.dashboard.core.network.PlatformEvent
import bot.nomnomz.dashboard.core.network.PlatformIamApi
import bot.nomnomz.dashboard.core.network.ProviderCredential
import bot.nomnomz.dashboard.core.network.ReinstateTenantBody
import bot.nomnomz.dashboard.core.network.SaveProviderCredentialBody
import bot.nomnomz.dashboard.core.network.SupportPersonHistoryEntry
import bot.nomnomz.dashboard.core.network.SupportPersonSearchResult
import bot.nomnomz.dashboard.core.network.SupportPersonView
import bot.nomnomz.dashboard.core.network.SuspendTenantBody
import bot.nomnomz.dashboard.core.network.TrustSafetyApi
import bot.nomnomz.dashboard.core.network.TrustSafetyReviewItem
import bot.nomnomz.dashboard.feature.admin.state.AdminController
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.LifecycleRegistry
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.compose.runtime.CompositionLocalProvider
import kotlin.test.Test
import kotlin.test.assertTrue
import kotlin.test.fail
import org.jetbrains.compose.resources.stringResource

/**
 * S-ADMIN-9 — the admin plane regrouped by JOB (Activity / People / Billing / Safety / Configuration)
 * instead of one flat eighteen-tab strip.
 *
 * Three things a regrouping can silently break, each proven here:
 *  1. [every_admin_tab_is_reachable_through_its_own_group] — every [AdminTab] enum entry still leads
 *     somewhere. It drives the walk from [AdminTab.entries] (reflection over the real enum), not a
 *     hand-typed list, and fails by name if a tab has no registered marker or no dispatch branch.
 *  2. [the_job_group_strip_fits_a_390dp_phone_width_without_scrolling] — the level-1 strip an operator
 *     reaches for mid-incident, from a phone, is never the thing that needs a horizontal scroll.
 *  3. [support_and_trust_safety_stay_hidden_when_their_clients_are_not_wired] — the availability gate
 *     that hides Support / Trust & Safety when the caller's session never wired those clients (the
 *     mechanism [AdminController.supportDeskAvailable] / [AdminController.trustSafetyAvailable] stands
 *     in for a role floor) survives the regrouping untouched, and their sibling tabs are not orphaned.
 */
@OptIn(ExperimentalTestApi::class)
class AdminTabGroupingTest {

    // AdminScreen collects its controller state with collectAsStateWithLifecycle(), which requires a
    // LocalLifecycleOwner — no existing admin test renders AdminScreen itself (only its individual tabs,
    // which take a pre-collected AdminState), so this is the first one that needs it. Mirrors
    // CommandsScreenTest's withLifecycle helper.
    @Composable
    private fun EnglishContent(content: @Composable () -> Unit) {
        val owner: LifecycleOwner =
            object : LifecycleOwner {
                override val lifecycle: Lifecycle = LifecycleRegistry.createUnsafe(this)
            }
        (owner.lifecycle as LifecycleRegistry).apply {
            currentState = Lifecycle.State.CREATED
            currentState = Lifecycle.State.STARTED
            currentState = Lifecycle.State.RESUMED
        }
        CompositionLocalProvider(LocalLifecycleOwner provides owner) {
            AppEnvironment(tag = "en") {
                NomNomzTheme { content() }
            }
        }
    }

    private fun SemanticsNodeInteractionsProvider.clickGroup(group: AdminTabGroup) {
        onNodeWithText(ENGLISH_GROUP_LABEL.getValue(group)).performClick()
    }

    // onLast() guards a group and its lone tab ever sharing a label again (e.g. Billing the group vs.
    // Billing the tab) — it always resolves to the sub-tab strip, which renders after the group strip in
    // the tree; for a non-colliding label there is only one match and onLast() is a no-op.
    private fun SemanticsNodeInteractionsProvider.clickTab(tab: AdminTab) {
        onAllNodesWithText(ENGLISH_TAB_LABEL.getValue(tab)).onLast().performClick()
    }

    @Test
    fun every_admin_tab_is_reachable_through_its_own_group() {
        val api = FakeAdminApiForGroupingTest(
            featureFlags = listOf(FeatureFlag(key = PROBE_FLAG_KEY, isEnabledGlobally = false)),
        )
        val controller = AdminController(
            api = api,
            iamApi = FakeIamApiForGroupingTest(),
            platformAdminApi = FakePlatformAdminApiForGroupingTest(),
            supportApi = FakeAdminSupportApiForGroupingTest(),
            trustSafetyApi = FakeTrustSafetyApiForGroupingTest(),
        )

        runComposeUiTest {
            setContent { EnglishContent { AdminScreen(controller = controller) } }
            waitForIdle()

            AdminTab.entries.forEach { tab ->
                val marker: String = EXPECTED_MARKER[tab]
                    ?: fail(
                        "AdminTabGroupingTest has no reachability marker registered for AdminTab.${tab.name} " +
                            "— a tab added to the enum must be proven reachable too, not silently skipped."
                    )

                clickGroup(tab.group)
                waitForIdle()
                clickTab(tab)
                waitForIdle()

                onNodeWithText(marker, substring = true).assertExists()
            }
        }
    }

    @Test
    fun the_job_group_strip_fits_a_390dp_phone_width_without_scrolling() {
        runComposeUiTest {
            setContent {
                EnglishContent {
                    Box(modifier = Modifier.width(390.dp).testTag("phoneViewport")) {
                        Column {
                            TabsList {
                                AdminTabGroup.entries.forEach { group ->
                                    TabsTrigger(
                                        selected = group == AdminTabGroup.Activity,
                                        onClick = {},
                                    ) {
                                        Text(text = stringResource(group.label))
                                    }
                                }
                            }
                        }
                    }
                }
            }
            waitForIdle()

            val viewport = onNodeWithTag("phoneViewport").fetchSemanticsNode()
            val viewportRightEdgePx: Float = viewport.positionInRoot.x + viewport.size.width

            // The last group's label is the one furthest right — if the five-group strip needed
            // TabsList's horizontal-scroll fallback to fit, this is exactly where its right edge would
            // land past the 390dp viewport it is drawn inside.
            val lastGroupLabel = ENGLISH_GROUP_LABEL.getValue(AdminTabGroup.entries.last())
            val lastNode = onNodeWithText(lastGroupLabel).fetchSemanticsNode()
            val lastRightEdgePx: Float = lastNode.positionInRoot.x + lastNode.size.width

            assertTrue(
                lastRightEdgePx <= viewportRightEdgePx + 1f,
                "the five-group strip needs $lastRightEdgePx px but the 390dp viewport only offers " +
                    "$viewportRightEdgePx px — it would scroll on a phone during an incident, the exact " +
                    "thing S-ADMIN-9 exists to fix",
            )
        }
    }

    @Test
    fun support_and_trust_safety_stay_hidden_when_their_clients_are_not_wired() {
        // No supportApi / trustSafetyApi wired at all — the same "caller's session never got that client"
        // gate the pre-existing admin_tab_support / admin_tab_trust_safety conditional inclusion used.
        val api = FakeAdminApiForGroupingTest()
        val controller = AdminController(
            api = api,
            iamApi = FakeIamApiForGroupingTest(),
            platformAdminApi = FakePlatformAdminApiForGroupingTest(),
        )

        runComposeUiTest {
            setContent { EnglishContent { AdminScreen(controller = controller) } }
            waitForIdle()

            onNodeWithText(ENGLISH_TAB_LABEL.getValue(AdminTab.Support)).assertDoesNotExist()
            onNodeWithText(ENGLISH_TAB_LABEL.getValue(AdminTab.TrustSafety), substring = true).assertDoesNotExist()

            // The People group still reaches its other members — the gate hides exactly the one
            // unavailable destination, never its whole group.
            clickGroup(AdminTabGroup.People)
            waitForIdle()
            clickTab(AdminTab.Tenants)
            waitForIdle()
            onNodeWithText(EXPECTED_MARKER.getValue(AdminTab.Tenants), substring = true).assertExists()

            // Same for the Safety group — Spam defaults is not orphaned by Trust & Safety's absence.
            clickGroup(AdminTabGroup.Safety)
            waitForIdle()
            onNodeWithText(EXPECTED_MARKER.getValue(AdminTab.SpamDefaults), substring = true).assertExists()
        }
    }

    private companion object {
        const val PROBE_FLAG_KEY: String = "s-admin-9-reachability-probe-flag"

        val ENGLISH_GROUP_LABEL: Map<AdminTabGroup, String> = mapOf(
            AdminTabGroup.Activity to "Live",
            AdminTabGroup.People to "Who",
            AdminTabGroup.Billing to "Money",
            AdminTabGroup.Safety to "Risk",
            AdminTabGroup.Configuration to "Setup",
        )

        val ENGLISH_TAB_LABEL: Map<AdminTab, String> = mapOf(
            AdminTab.Overview to "Overview",
            AdminTab.EventSubHealth to "EventSub health",
            AdminTab.WebhookDeliveries to "Webhook deliveries",
            AdminTab.ScheduledJobs to "Job queue",
            AdminTab.TenantUsage to "Usage",
            AdminTab.ErrorBudget to "Error budget",
            AdminTab.EventReplay to "Event replay",
            AdminTab.Audit to "Audit",
            AdminTab.Channels to "Channels",
            AdminTab.Users to "Users",
            AdminTab.Tenants to "Tenants",
            AdminTab.Support to "Support",
            AdminTab.Billing to "Billing",
            AdminTab.SpamDefaults to "Spam defence defaults",
            AdminTab.TrustSafety to "Trust & safety",
            AdminTab.System to "System",
            AdminTab.FeatureFlags to "Feature Flags",
            AdminTab.Providers to "Providers",
            AdminTab.Content to "Content",
            AdminTab.Iam to "IAM",
        )

        // Text that renders unconditionally once each tab is open, with default (empty) fake-API data —
        // never a fabricated uniform label, each one is that tab's own real empty-state or explanatory copy.
        val EXPECTED_MARKER: Map<AdminTab, String> = mapOf(
            AdminTab.Overview to "Channel registry (live)",
            AdminTab.EventSubHealth to "No EventSub subscriptions found.",
            AdminTab.WebhookDeliveries to "No webhook deliveries recorded.",
            AdminTab.ScheduledJobs to "No scheduled jobs found.",
            AdminTab.TenantUsage to "No recorded usage found.",
            AdminTab.ErrorBudget to "No error budget data recorded.",
            AdminTab.EventReplay to "No projections registered.",
            AdminTab.Audit to "No audit entries.",
            AdminTab.Channels to "No channels match.",
            AdminTab.Users to "No users match.",
            AdminTab.Tenants to "No tenants match.",
            AdminTab.Support to "Name or platform id",
            AdminTab.Billing to "No invite codes yet.",
            AdminTab.SpamDefaults to "These are the values every new channel starts with",
            AdminTab.TrustSafety to "Why are you looking? (recorded)",
            AdminTab.System to "No health checks reported.",
            AdminTab.FeatureFlags to PROBE_FLAG_KEY,
            AdminTab.Providers to "No providers loaded.",
            // With no IAM principal wired up for this fake session, ContentTab denies read rather than
            // showing an empty list — the same "below the read floor" state a real under-permissioned
            // operator would see, and just as unique a marker for "the Content tab is open" as an empty list.
            AdminTab.Content to "Requires content:read",
            AdminTab.Iam to "No principals yet.",
        )
    }
}

private class FakeAdminApiForGroupingTest(
    private val featureFlags: List<FeatureFlag> = emptyList(),
) : AdminApi {
    override suspend fun getStats(): ApiResult<AdminStats> = ApiResult.Ok(AdminStats(0, 0, 0, "ok", 0, 0))
    override suspend fun getChannels(search: String?, page: Int, pageSize: Int, sort: String?, isLive: Boolean?) =
        ApiResult.Ok(PaginatedEnvelope<AdminChannel>(emptyList()))
    override suspend fun getUsers(search: String?, page: Int, pageSize: Int, sort: String?, role: String?) =
        ApiResult.Ok(PaginatedEnvelope<AdminUser>(emptyList()))
    override suspend fun getSystem() = ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun getHealth() = ApiResult.Ok(emptyList<AdminServiceHealth>())
    override suspend fun getEvents() = ApiResult.Ok(emptyList<PlatformEvent>())
    override suspend fun getFeatureFlags() = ApiResult.Ok(featureFlags)
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
    override suspend fun getInvoices(broadcasterId: String): ApiResult<List<AdminInvoice>> = ApiResult.Ok(emptyList())
    override suspend fun refundInvoice(invoiceId: String) = ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun getEventSubHealth(page: Int, pageSize: Int): ApiResult<PaginatedEnvelope<AdminEventSubTenantHealth>> =
        ApiResult.Ok(PaginatedEnvelope(emptyList()))
    override suspend fun getWebhookDeliveries(page: Int, pageSize: Int): ApiResult<PaginatedEnvelope<AdminWebhookDelivery>> =
        ApiResult.Ok(PaginatedEnvelope(emptyList()))
    override suspend fun replayWebhookDelivery(deliveryId: Long): ApiResult<AdminWebhookReplayResult> =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun getScheduledJobs(page: Int, pageSize: Int): ApiResult<PaginatedEnvelope<AdminScheduledJob>> =
        ApiResult.Ok(PaginatedEnvelope(emptyList()))
    override suspend fun retryScheduledJob(taskId: String): ApiResult<AdminScheduledJobRetryResult> =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun getTenantUsage(page: Int, pageSize: Int): ApiResult<PaginatedEnvelope<AdminTenantUsage>> =
        ApiResult.Ok(PaginatedEnvelope(emptyList()))
    override suspend fun getErrorBudget(page: Int, pageSize: Int): ApiResult<PaginatedEnvelope<AdminTenantErrorBudget>> =
        ApiResult.Ok(PaginatedEnvelope(emptyList()))
    override suspend fun getReplayableProjections(): ApiResult<List<AdminReplayableProjection>> =
        ApiResult.Ok(emptyList())
    override suspend fun previewEventReplay(
        broadcasterId: String,
        projectionName: String,
        fromUtc: String,
        toUtc: String,
        eventType: String?,
    ): ApiResult<AdminEventReplayPreview> = ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun executeEventReplay(
        broadcasterId: String,
        projectionName: String,
        fromUtc: String,
        toUtc: String,
        eventType: String?,
        expectedCount: Long,
    ): ApiResult<AdminEventReplayResult> = ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
}

private class FakeIamApiForGroupingTest : PlatformIamApi {
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

private class FakePlatformAdminApiForGroupingTest : PlatformAdminApi {
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

/** Only present to flip [AdminController.supportDeskAvailable] on — none of its methods are exercised by
 * this slice's navigation, since [SupportTab] never auto-fetches (it waits for a typed search). */
private class FakeAdminSupportApiForGroupingTest : AdminSupportApi {
    override suspend fun searchPeople(search: String, justification: String, page: Int, pageSize: Int) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun getPerson(subjectUserId: String, justification: String): ApiResult<SupportPersonView> =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun getPersonHistory(subjectUserId: String, justification: String, page: Int, pageSize: Int) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
}

/** Only present to flip [AdminController.trustSafetyAvailable] on — none of its methods are exercised by
 * this slice's navigation, since [TrustSafetyTab] never auto-fetches (it waits for a typed justification). */
private class FakeTrustSafetyApiForGroupingTest : TrustSafetyApi {
    override suspend fun getCrossTenantSignals(justification: String): ApiResult<List<CrossTenantAbuseSignal>> =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun getReviewQueue(justification: String, page: Int, pageSize: Int) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun confirm(detectionId: String, justification: String) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun overturn(detectionId: String, justification: String) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun previewNetworkBlock(targetTwitchUserId: String, justification: String): ApiResult<NetworkBlockPreview> =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun applyNetworkBlock(
        targetTwitchUserId: String,
        reason: String?,
        justification: String,
        confirmedTenantCount: Int,
    ): ApiResult<NetworkBlock> = ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun listNetworkBlocks(justification: String): ApiResult<List<NetworkBlock>> =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun liftNetworkBlock(blockId: String, justification: String): ApiResult<NetworkBlock> =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
}
