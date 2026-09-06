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
import bot.nomnomz.dashboard.core.network.AdminSupportApi
import bot.nomnomz.dashboard.core.network.AdminTenant
import bot.nomnomz.dashboard.core.network.AdminTier
import bot.nomnomz.dashboard.core.network.AdminUpdateTierRequest
import bot.nomnomz.dashboard.core.network.AdminUser
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.AssignRoleBody
import bot.nomnomz.dashboard.core.network.BeginTenantAccessBody
import bot.nomnomz.dashboard.core.network.CreatePrincipalBody
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
import bot.nomnomz.dashboard.core.network.SupportPersonCommunityStanding
import bot.nomnomz.dashboard.core.network.SupportPersonEntitlement
import bot.nomnomz.dashboard.core.network.SupportPersonHistoryEntry
import bot.nomnomz.dashboard.core.network.SupportPersonSearchResult
import bot.nomnomz.dashboard.core.network.SupportPersonTrustScore
import bot.nomnomz.dashboard.core.network.SupportPersonView
import bot.nomnomz.dashboard.core.network.SuspendTenantBody
import bot.nomnomz.dashboard.feature.admin.state.AdminController
import kotlinx.coroutines.test.runTest
import kotlin.test.Test

/**
 * S-ADMIN-7a: the support desk must render a found person's REAL cross-tenant state — the trust/heat numbers,
 * the community standing and the entitlement tier the backend actually returned, each naming its channel —
 * and it must OMIT a fact the system does not hold. A person with no trust score and no entitlement renders
 * no "Trust and heat" and no "Entitlements" block at all: a 0.0 placeholder would read as a measured value
 * and mislead the operator acting on it.
 */
@OptIn(ExperimentalTestApi::class)
class AdminSupportPersonRenderTest {

    private val searchHit = SupportPersonSearchResult(
        userId = "user-1",
        username = "wandering_viewer",
        displayName = "Wandering Viewer",
        platform = "twitch",
        twitchUserId = "tw-4242",
        tenantCount = 2,
    )

    @Composable
    private fun EnglishContent(content: @Composable () -> Unit) {
        AppEnvironment(tag = "en") {
            NomNomzTheme { content() }
        }
    }

    @Composable
    private fun ObservingSupportTab(controller: AdminController) {
        val state by controller.state.collectAsState()
        SupportTab(state = state, controller = controller)
    }

    private fun controllerFor(
        person: SupportPersonView,
        history: List<SupportPersonHistoryEntry> = emptyList(),
    ): AdminController {
        val controller = AdminController(
            api = FakeAdminApiForSupportTest(),
            iamApi = FakeIamApiForSupportTest(),
            platformAdminApi = FakePlatformAdminApiForSupportTest(),
            supportApi = FakeSupportApi(results = listOf(searchHit), person = person, history = history),
        )
        runTest {
            controller.setSupportSearch("wandering")
            controller.setSupportJustification("Ticket #8812")
            controller.searchPeople()
            controller.openSupportPerson("user-1")
        }
        return controller
    }

    @Test
    fun a_found_persons_real_state_renders_with_the_channel_each_fact_belongs_to() {
        val controller = controllerFor(
            SupportPersonView(
                userId = "user-1",
                username = "wandering_viewer",
                displayName = "Wandering Viewer",
                platform = "twitch",
                trustScores = listOf(
                    SupportPersonTrustScore(
                        broadcasterId = "chan-2",
                        channelName = "other_streamer",
                        trustScore = 73.5,
                        heatScore = 12.25,
                        computedAt = "2026-09-01T00:00:00Z",
                    ),
                ),
                communityStandings = listOf(
                    SupportPersonCommunityStanding(
                        broadcasterId = "chan-2",
                        channelName = "other_streamer",
                        standing = "Vip",
                        levelValue = 30,
                        source = "EventSubBadge",
                    ),
                ),
                entitlements = listOf(
                    SupportPersonEntitlement(
                        broadcasterId = "chan-1",
                        channelName = "wandering_viewer",
                        tierKey = "pro",
                    ),
                ),
            ),
        )

        runComposeUiTest {
            setContent { EnglishContent { ObservingSupportTab(controller = controller) } }
            waitForIdle()

            onNodeWithText("Wandering Viewer", substring = true).assertExists()
            // The trust block renders the PERSISTED numbers, under the channel they were computed in.
            onNodeWithText("Trust and heat").assertExists()
            onNodeWithText("Trust 73.5, heat 12.25", substring = true).assertExists()
            onNodeWithText("Community standing").assertExists()
            onNodeWithText("Vip", substring = true).assertExists()
            onNodeWithText("Entitlements").assertExists()
            onNodeWithText("Tier pro", substring = true).assertExists()
        }
    }

    @Test
    fun a_fact_the_system_does_not_hold_is_absent_not_a_blank_row() {
        // Everything the backend genuinely has for this person is their community standing. No trust score has
        // ever been computed for them and no entitlement exists.
        val controller = controllerFor(
            SupportPersonView(
                userId = "user-1",
                username = "wandering_viewer",
                displayName = "Wandering Viewer",
                platform = "twitch",
                communityStandings = listOf(
                    SupportPersonCommunityStanding(
                        broadcasterId = "chan-2",
                        channelName = "other_streamer",
                        standing = "Vip",
                        levelValue = 30,
                        source = "EventSubBadge",
                    ),
                ),
            ),
        )

        runComposeUiTest {
            setContent { EnglishContent { ObservingSupportTab(controller = controller) } }
            waitForIdle()

            onNodeWithText("Community standing").assertExists()
            onNodeWithText("Trust and heat").assertDoesNotExist()
            onNodeWithText("Entitlements").assertDoesNotExist()
            onNodeWithText("Moderation history").assertDoesNotExist()
            onNodeWithText("Platform roles").assertDoesNotExist()
        }
    }

    @Test
    fun the_activity_section_renders_each_real_event_with_the_tenant_it_happened_in() {
        val controller = controllerFor(
            person = SupportPersonView(
                userId = "user-1",
                username = "wandering_viewer",
                displayName = "Wandering Viewer",
                platform = "twitch",
            ),
            history = listOf(
                SupportPersonHistoryEntry(
                    eventId = "evt-1",
                    broadcasterId = "chan-1",
                    channelName = "streamer_a",
                    eventType = "UserTimedOutEvent",
                    source = "eventsub",
                    occurredAt = "2026-09-03T12:00:00Z",
                ),
                SupportPersonHistoryEntry(
                    eventId = "evt-2",
                    broadcasterId = "chan-2",
                    channelName = "streamer_b",
                    eventType = "NewFollowerEvent",
                    source = "eventsub",
                    occurredAt = "2026-09-02T12:00:00Z",
                ),
            ),
        )

        runComposeUiTest {
            setContent { EnglishContent { ObservingSupportTab(controller = controller) } }
            waitForIdle()

            onNodeWithText("Activity across every channel").assertExists()
            // Each event names the TENANT it actually happened in, not a bare id.
            onNodeWithText("UserTimedOutEvent", substring = true).assertExists()
            onNodeWithText("streamer_a", substring = true).assertExists()
            onNodeWithText("NewFollowerEvent", substring = true).assertExists()
            onNodeWithText("streamer_b", substring = true).assertExists()
        }
    }

    @Test
    fun no_recorded_history_renders_the_empty_state_not_an_error() {
        val controller = controllerFor(
            person = SupportPersonView(
                userId = "user-1",
                username = "brand_new_chatter",
                displayName = "Brand New Chatter",
                platform = "twitch",
            ),
            history = emptyList(),
        )

        runComposeUiTest {
            setContent { EnglishContent { ObservingSupportTab(controller = controller) } }
            waitForIdle()

            onNodeWithText("Activity across every channel").assertExists()
            onNodeWithText("Nothing has been recorded for this person yet.").assertExists()
            onNodeWithText("Could not load", substring = true).assertDoesNotExist()
        }
    }
}

private class FakeSupportApi(
    private val results: List<SupportPersonSearchResult>,
    private val person: SupportPersonView,
    private val history: List<SupportPersonHistoryEntry> = emptyList(),
) : AdminSupportApi {
    override suspend fun searchPeople(
        search: String,
        justification: String,
        page: Int,
        pageSize: Int,
    ): ApiResult<PaginatedEnvelope<SupportPersonSearchResult>> =
        ApiResult.Ok(PaginatedEnvelope(results))

    override suspend fun getPerson(
        subjectUserId: String,
        justification: String,
    ): ApiResult<SupportPersonView> = ApiResult.Ok(person)

    override suspend fun getPersonHistory(
        subjectUserId: String,
        justification: String,
        page: Int,
        pageSize: Int,
    ): ApiResult<PaginatedEnvelope<SupportPersonHistoryEntry>> =
        ApiResult.Ok(PaginatedEnvelope(history))
}

private class FakeAdminApiForSupportTest : AdminApi {
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

private class FakeIamApiForSupportTest : PlatformIamApi {
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

private class FakePlatformAdminApiForSupportTest : PlatformAdminApi {
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
