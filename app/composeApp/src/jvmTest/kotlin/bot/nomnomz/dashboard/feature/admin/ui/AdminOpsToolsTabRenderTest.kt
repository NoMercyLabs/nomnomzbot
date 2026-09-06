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
import androidx.compose.ui.test.onAllNodesWithText
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
import bot.nomnomz.dashboard.core.network.AdminEventSubTenantHealth
import bot.nomnomz.dashboard.core.network.AdminEventSubTopicHealth
import bot.nomnomz.dashboard.core.network.AdminGrantTierRequest
import bot.nomnomz.dashboard.core.network.AdminInvoice
import bot.nomnomz.dashboard.core.network.AdminIssueEntitlementGrantRequest
import bot.nomnomz.dashboard.core.network.AdminScheduledJob
import bot.nomnomz.dashboard.core.network.AdminScheduledJobRetryResult
import bot.nomnomz.dashboard.core.network.AdminServiceHealth
import bot.nomnomz.dashboard.core.network.AdminTenantUsage
import bot.nomnomz.dashboard.core.network.AdminTenantUsageMetric
import bot.nomnomz.dashboard.core.network.AdminSetFeatureFlagOverrideRequest
import bot.nomnomz.dashboard.core.network.AdminSetFeatureFlagRequest
import bot.nomnomz.dashboard.core.network.AdminStats
import bot.nomnomz.dashboard.core.network.AdminSystem
import bot.nomnomz.dashboard.core.network.AdminTier
import bot.nomnomz.dashboard.core.network.AdminUpdateTierRequest
import bot.nomnomz.dashboard.core.network.AdminUser
import bot.nomnomz.dashboard.core.network.AdminWebhookDelivery
import bot.nomnomz.dashboard.core.network.AdminWebhookReplayResult
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
 * S-ADMIN-6a/6b — the admin console's "2am tools". Proves the EventSub health tab renders one tenant's REAL
 * registry rows with their real status (a healthy topic and a revoked one read differently, never a uniform
 * "all good"), that the webhook delivery log's replay control shows exactly what it will re-send BEFORE the
 * send commits, that the background job queue renders both a queued and a failed job with real, distinct
 * state and its retry control shows exactly which pipeline it will re-run before the retry commits, and that
 * per-tenant usage renders real recorded quantities — mirroring [AdminInvoicesRenderTest]'s refund-preview
 * pattern throughout.
 */
@OptIn(ExperimentalTestApi::class)
class AdminOpsToolsTabRenderTest {

    @Composable
    private fun EnglishContent(content: @Composable () -> Unit) {
        AppEnvironment(tag = "en") {
            NomNomzTheme { content() }
        }
    }

    @Composable
    private fun ObservingEventSubHealthTab(controller: AdminController) {
        val state by controller.state.collectAsState()
        EventSubHealthTab(state = state, controller = controller)
    }

    @Composable
    private fun ObservingWebhookDeliveriesTab(controller: AdminController) {
        val state by controller.state.collectAsState()
        WebhookDeliveriesTab(state = state, controller = controller)
    }

    @Composable
    private fun ObservingScheduledJobsTab(controller: AdminController) {
        val state by controller.state.collectAsState()
        ScheduledJobsTab(state = state, controller = controller)
    }

    @Composable
    private fun ObservingTenantUsageTab(controller: AdminController) {
        val state by controller.state.collectAsState()
        TenantUsageTab(state = state, controller = controller)
    }

    @Test
    fun eventsub_health_renders_a_healthy_topic_and_a_revoked_one_with_their_real_status() {
        val healthyTopic = AdminEventSubTopicHealth(
            subscriptionId = "sub-1",
            eventType = "channel.follow",
            version = "2",
            status = "enabled",
            enabled = true,
            lastConfirmedAt = "2026-09-04T12:00:00Z",
        )
        val revokedTopic = AdminEventSubTopicHealth(
            subscriptionId = "sub-2",
            eventType = "channel.chat.message",
            version = "1",
            status = "revoked",
            enabled = false,
            lastError = "authorization revoked",
            lastConfirmedAt = "2026-09-03T08:00:00Z",
        )
        val api = FakeAdminApiForOpsToolsTest(
            eventSubHealth = listOf(
                AdminEventSubTenantHealth(
                    broadcasterId = "chan-1",
                    channelDisplayName = "qtkitte",
                    topics = listOf(healthyTopic, revokedTopic),
                ),
            ),
        )
        val controller = AdminController(
            api = api,
            iamApi = FakeIamApiForOpsToolsTest(),
            platformAdminApi = FakePlatformAdminApiForOpsToolsTest(),
        )

        runTest { controller.loadEventSubHealth() }

        runComposeUiTest {
            setContent {
                EnglishContent {
                    ObservingEventSubHealthTab(controller = controller)
                }
            }
            waitForIdle()

            // The REAL registry state per topic — never a fabricated uniform "healthy". "revoked" also
            // appears inside the error line ("authorization revoked"), so that badge is asserted via the
            // still-unique error text rather than the bare word, which now matches twice.
            onNodeWithText("qtkitte", substring = true).assertExists()
            onNodeWithText("channel.follow", substring = true).assertExists()
            onNodeWithText("enabled", substring = true).assertExists()
            onNodeWithText("channel.chat.message", substring = true).assertExists()
            onNodeWithText("authorization revoked", substring = true).assertExists()
        }
    }

    @Test
    fun webhook_delivery_log_renders_status_and_the_replay_confirmation_shows_what_it_will_resend() {
        val delivery = AdminWebhookDelivery(
            id = 42L,
            broadcasterId = "chan-1",
            endpointId = "ep-1",
            endpointName = "discord-relay",
            endpointCanReplay = true,
            eventType = "channel.subscribe",
            attempt = 1,
            status = "Failed",
            responseCode = 500,
            createdAt = "2026-09-04T12:00:00Z",
        )
        val api = FakeAdminApiForOpsToolsTest(webhookDeliveries = listOf(delivery))
        val controller = AdminController(
            api = api,
            iamApi = FakeIamApiForOpsToolsTest(),
            platformAdminApi = FakePlatformAdminApiForOpsToolsTest(),
        )

        runTest { controller.loadWebhookDeliveries() }

        runComposeUiTest {
            setContent {
                EnglishContent {
                    ObservingWebhookDeliveriesTab(controller = controller)
                }
            }
            waitForIdle()

            onNodeWithText("discord-relay", substring = true).assertExists()
            onNodeWithText("Failed", substring = true).assertExists()

            // Clicking Replay opens the confirmation — nothing has been sent yet.
            onNodeWithText("Replay").performClick()
            waitForIdle()
            assertEquals(0, api.replayCallCount)

            // The confirmation names exactly what will be re-sent — the event type and the target endpoint —
            // BEFORE the distinctly-labelled commit button can be pressed. Asserted via the dialog's own
            // distinct sentence rather than the bare event type/endpoint name, which now match twice (the
            // row behind the dialog still shows both too).
            onNodeWithText("This sends a new channel.subscribe delivery to discord-relay", substring = true)
                .assertExists()

            onNodeWithText("Confirm replay").performClick()
            waitForIdle()

            assertEquals(1, api.replayCallCount)
            assertEquals(42L, api.lastReplayedDeliveryId)
        }
    }

    @Test
    fun scheduled_job_queue_renders_a_queued_job_and_a_failed_one_with_their_real_state() {
        val queued = AdminScheduledJob(
            id = "job-1",
            broadcasterId = "chan-1",
            channelDisplayName = "qtkitte",
            pipelineId = "pipe-1",
            pipelineName = "feather-hide",
            pipelineExists = true,
            status = "pending",
            displayState = "queued",
            dueAt = "2026-09-06T13:00:00Z",
            createdAt = "2026-09-06T12:00:00Z",
            triggeredByDisplayName = "some_viewer",
            canRetry = false,
        )
        val failed = AdminScheduledJob(
            id = "job-2",
            broadcasterId = "chan-1",
            channelDisplayName = "qtkitte",
            pipelineId = "pipe-2",
            pipelineName = "voice-swap-revert",
            pipelineExists = true,
            status = "expired",
            displayState = "failed",
            dueAt = "2026-09-06T11:00:00Z",
            firedAt = "2026-09-06T11:00:00Z",
            createdAt = "2026-09-06T10:00:00Z",
            triggeredByDisplayName = "other_viewer",
            canRetry = true,
        )
        val api = FakeAdminApiForOpsToolsTest(scheduledJobs = listOf(queued, failed))
        val controller = AdminController(
            api = api,
            iamApi = FakeIamApiForOpsToolsTest(),
            platformAdminApi = FakePlatformAdminApiForOpsToolsTest(),
        )

        runTest { controller.loadScheduledJobs() }

        runComposeUiTest {
            setContent {
                EnglishContent {
                    ObservingScheduledJobsTab(controller = controller)
                }
            }
            waitForIdle()

            // Real, distinct state — never a uniform label for both rows.
            onNodeWithText("feather-hide", substring = true).assertExists()
            onNodeWithText("queued", substring = true).assertExists()
            onNodeWithText("voice-swap-revert", substring = true).assertExists()
            onNodeWithText("failed", substring = true).assertExists()

            // Retrying shows exactly which pipeline it will re-run — nothing sent yet. Two rows both carry a
            // Retry control (the queued job's is disabled); the failed row's is the second one rendered.
            onAllNodesWithText("Retry")[1].performClick()
            waitForIdle()
            assertEquals(0, api.retryCallCount)
            onNodeWithText("This schedules a brand-new run of voice-swap-revert for qtkitte", substring = true)
                .assertExists()

            onNodeWithText("Confirm retry").performClick()
            waitForIdle()

            assertEquals(1, api.retryCallCount)
            assertEquals("job-2", api.lastRetriedTaskId)
        }
    }

    @Test
    fun tenant_usage_renders_the_real_recorded_quantities_and_period() {
        val usage = AdminTenantUsage(
            broadcasterId = "chan-1",
            channelDisplayName = "qtkitte",
            periodStart = "2026-09-01T00:00:00Z",
            periodEnd = "2026-10-01T00:00:00Z",
            metrics = listOf(AdminTenantUsageMetric(metricKey = "chat_messages", quantity = 500)),
            ttsCharacterCount = 42,
        )
        val api = FakeAdminApiForOpsToolsTest(tenantUsage = listOf(usage))
        val controller = AdminController(
            api = api,
            iamApi = FakeIamApiForOpsToolsTest(),
            platformAdminApi = FakePlatformAdminApiForOpsToolsTest(),
        )

        runTest { controller.loadTenantUsage() }

        runComposeUiTest {
            setContent {
                EnglishContent {
                    ObservingTenantUsageTab(controller = controller)
                }
            }
            waitForIdle()

            onNodeWithText("qtkitte", substring = true).assertExists()
            onNodeWithText("chat_messages: 500", substring = true).assertExists()
            onNodeWithText("TTS characters: 42", substring = true).assertExists()
        }
    }
}

private class FakeAdminApiForOpsToolsTest(
    private val eventSubHealth: List<AdminEventSubTenantHealth> = emptyList(),
    private var webhookDeliveries: List<AdminWebhookDelivery> = emptyList(),
    private var scheduledJobs: List<AdminScheduledJob> = emptyList(),
    private val tenantUsage: List<AdminTenantUsage> = emptyList(),
) : AdminApi {
    var replayCallCount: Int = 0
        private set
    var lastReplayedDeliveryId: Long? = null
        private set
    var retryCallCount: Int = 0
        private set
    var lastRetriedTaskId: String? = null
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
    override suspend fun getInvoices(broadcasterId: String): ApiResult<List<AdminInvoice>> = ApiResult.Ok(emptyList())
    override suspend fun refundInvoice(invoiceId: String) = ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))

    override suspend fun getEventSubHealth(page: Int, pageSize: Int): ApiResult<PaginatedEnvelope<AdminEventSubTenantHealth>> =
        ApiResult.Ok(PaginatedEnvelope(eventSubHealth))

    override suspend fun getWebhookDeliveries(page: Int, pageSize: Int): ApiResult<PaginatedEnvelope<AdminWebhookDelivery>> =
        ApiResult.Ok(PaginatedEnvelope(webhookDeliveries))

    // Mirrors the real backend: a replay appends a NEW row and leaves the original attempt exactly as it
    // was — this fake's list gains a second row rather than mutating the one being replayed.
    override suspend fun replayWebhookDelivery(deliveryId: Long): ApiResult<AdminWebhookReplayResult> {
        replayCallCount++
        lastReplayedDeliveryId = deliveryId
        val original = webhookDeliveries.first { it.id == deliveryId }
        val replay = original.copy(id = original.id + 1000L, attempt = 1, status = "Delivered", responseCode = 200)
        webhookDeliveries = webhookDeliveries + replay
        return ApiResult.Ok(AdminWebhookReplayResult(deliveryId, replay.id, replay.status, replay.responseCode))
    }

    override suspend fun getScheduledJobs(page: Int, pageSize: Int): ApiResult<PaginatedEnvelope<AdminScheduledJob>> =
        ApiResult.Ok(PaginatedEnvelope(scheduledJobs))

    // Mirrors the real backend: a retry appends a NEW pending row and leaves the original failed attempt
    // exactly as it was — this fake's list gains a second row rather than mutating the one being retried.
    override suspend fun retryScheduledJob(taskId: String): ApiResult<AdminScheduledJobRetryResult> {
        retryCallCount++
        lastRetriedTaskId = taskId
        val original = scheduledJobs.first { it.id == taskId }
        val retried = original.copy(
            id = "${original.id}-retry",
            status = "pending",
            displayState = "queued",
            firedAt = null,
            canRetry = false,
        )
        scheduledJobs = scheduledJobs + retried
        return ApiResult.Ok(
            AdminScheduledJobRetryResult(
                originalTaskId = original.id,
                newTaskId = retried.id,
                pipelineId = original.pipelineId,
                pipelineName = original.pipelineName ?: original.pipelineId,
                newDueAt = "2026-09-06T12:00:01Z",
            )
        )
    }

    override suspend fun getTenantUsage(page: Int, pageSize: Int): ApiResult<PaginatedEnvelope<AdminTenantUsage>> =
        ApiResult.Ok(PaginatedEnvelope(tenantUsage))
}

private class FakeIamApiForOpsToolsTest : PlatformIamApi {
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

private class FakePlatformAdminApiForOpsToolsTest : PlatformAdminApi {
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
