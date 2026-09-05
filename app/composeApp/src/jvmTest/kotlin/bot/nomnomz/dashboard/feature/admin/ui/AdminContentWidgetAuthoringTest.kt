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
import bot.nomnomz.dashboard.core.network.AdminGrantTierRequest
import bot.nomnomz.dashboard.core.network.AdminServiceHealth
import bot.nomnomz.dashboard.core.network.AdminSetFeatureFlagOverrideRequest
import bot.nomnomz.dashboard.core.network.AdminSetFeatureFlagRequest
import bot.nomnomz.dashboard.core.network.AdminStats
import bot.nomnomz.dashboard.core.network.AdminSystem
import bot.nomnomz.dashboard.core.network.AdminUser
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.AssignRoleBody
import bot.nomnomz.dashboard.core.network.CreateContentDefinitionBody
import bot.nomnomz.dashboard.core.network.CreatePrincipalBody
import bot.nomnomz.dashboard.core.network.DraftContentVersionBody
import bot.nomnomz.dashboard.core.network.FeatureFlag
import bot.nomnomz.dashboard.core.network.IamPrincipalSummary
import bot.nomnomz.dashboard.core.network.IamRole
import bot.nomnomz.dashboard.core.network.InviteCode
import bot.nomnomz.dashboard.core.network.PaginatedEnvelope
import bot.nomnomz.dashboard.core.network.PlatformAdminApi
import bot.nomnomz.dashboard.core.network.PlatformContentApi
import bot.nomnomz.dashboard.core.network.PlatformContentDefinition
import bot.nomnomz.dashboard.core.network.PlatformContentDefinitionDetail
import bot.nomnomz.dashboard.core.network.PlatformContentPublishJob
import bot.nomnomz.dashboard.core.network.PlatformContentVersion
import bot.nomnomz.dashboard.core.network.PlatformEvent
import bot.nomnomz.dashboard.core.network.PlatformIamApi
import bot.nomnomz.dashboard.core.network.ProviderCredential
import bot.nomnomz.dashboard.core.network.PublishContentBody
import bot.nomnomz.dashboard.core.network.PublishPreview
import bot.nomnomz.dashboard.core.network.PublishPreviewBody
import bot.nomnomz.dashboard.core.network.SaveProviderCredentialBody
import bot.nomnomz.dashboard.feature.admin.state.AdminController
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertTrue

/**
 * S-ADMIN-2c-c: the Content tab must render editable Vue source / settings / event subscriptions for a
 * `widget`-kind definition (never just a raw undifferentiated JSON blob), and a publish job carrying failed
 * tenant widget rebuilds must render a visible failure surface naming the count — not silently drop the
 * field. Both assertions read the rendered semantics tree, not controller state, so a regression that wires
 * the field through state but never paints it on screen still fails this test.
 */
@OptIn(ExperimentalTestApi::class)
class AdminContentWidgetAuthoringTest {

    @Composable
    private fun EnglishContent(content: @Composable () -> Unit) {
        AppEnvironment(tag = "en") {
            NomNomzTheme { content() }
        }
    }

    @Composable
    private fun ObservingContentTab(controller: AdminController, currentUserId: String) {
        val state by controller.state.collectAsState()
        ContentTab(state = state, controller = controller, currentUserId = currentUserId)
    }

    private val widgetPayloadJson: String =
        """{"sourceCode":"<template><div>hi</div></template>","defaultSettings":{"title":"Now Playing"},"defaultEventSubscriptions":["song.changed"]}"""

    @Test
    fun opening_a_widget_definition_renders_editable_source_settings_and_event_subscriptions() {
        val definitionId = "def-widget-1"
        val versionId = "ver-widget-1"
        val definition = PlatformContentDefinition(
            id = definitionId,
            kind = "widget",
            key = "now-playing",
            displayName = "Now Playing",
            currentVersion = 1,
            currentVersionId = versionId,
            createdAt = "2026-09-01T00:00:00Z",
        )
        val version = PlatformContentVersion(
            id = versionId,
            definitionId = definitionId,
            version = 1,
            contentHash = "abc123",
            payloadJson = widgetPayloadJson,
            publishedAt = "2026-09-01T00:00:00Z",
            draftedAt = "2026-09-01T00:00:00Z",
            draftedByPrincipalId = "principal-1",
        )
        val api = FakeContentApi(
            definitions = listOf(definition),
            definitionDetail = PlatformContentDefinitionDetail(definition = definition, versions = listOf(version)),
        )
        val controller = AdminController(
            api = NoopAdminApiForWidgetTest(),
            iamApi = FakeIamApiForWidgetTest(),
            platformAdminApi = NoopPlatformAdminApiForWidgetTest(),
            contentApi = api,
        )

        runTest {
            controller.loadIam()
            controller.loadContentDefinitions()
            controller.openContentDefinition(definitionId)
        }

        runComposeUiTest {
            setContent {
                EnglishContent {
                    ObservingContentTab(controller = controller, currentUserId = "user-1")
                }
            }

            onAllNodesWithText("Draft new version")[0].performClick()
            waitForIdle()

            onNodeWithText("Vue source").assertExists()
            onNodeWithText("<template><div>hi</div></template>").assertExists()
            onNodeWithText("Default settings (JSON)").assertExists()
            onNodeWithText("Default event subscriptions").assertExists()
            assertTrue(
                onAllNodesWithText("song.changed").fetchSemanticsNodes().isNotEmpty(),
                "the parsed defaultEventSubscriptions entry must render as an editable row, not stay unread inside the raw payload",
            )
        }
    }

    @Test
    fun a_publish_job_with_failed_widget_rebuilds_renders_a_visible_failure_surface_naming_the_count() {
        val definitionId = "def-widget-2"
        val versionId = "ver-widget-2"
        val definition = PlatformContentDefinition(
            id = definitionId,
            kind = "widget",
            key = "chat-box",
            displayName = "Chat Box",
            currentVersion = 1,
            currentVersionId = versionId,
            createdAt = "2026-09-01T00:00:00Z",
        )
        val version = PlatformContentVersion(
            id = versionId,
            definitionId = definitionId,
            version = 1,
            contentHash = "abc123",
            payloadJson = widgetPayloadJson,
            publishedAt = "2026-09-01T00:00:00Z",
            draftedAt = "2026-09-01T00:00:00Z",
            draftedByPrincipalId = "principal-1",
        )
        val publishJob = PlatformContentPublishJob(
            id = "job-1",
            definitionId = definitionId,
            toVersion = 1,
            mode = "update_in_place_where_untouched",
            requestedByPrincipalId = "principal-1",
            requestedAt = "2026-09-01T00:00:00Z",
            previewAffectedCount = 10,
            previewSkippedCount = 0,
            confirmedAffectedCount = 10,
            status = "completed",
            // The field this slice closes: a non-empty rebuildFailedWidgetIds must never sit unread.
            rebuildFailedWidgetIds = listOf("tenant-a", "tenant-b", "tenant-c"),
        )
        val api = FakeContentApi(
            definitions = listOf(definition),
            definitionDetail = PlatformContentDefinitionDetail(definition = definition, versions = listOf(version)),
            preview = PublishPreview(affectedCount = 10, skippedCount = 0),
            publishResult = publishJob,
        )
        val controller = AdminController(
            api = NoopAdminApiForWidgetTest(),
            iamApi = FakeIamApiForWidgetTest(),
            platformAdminApi = NoopPlatformAdminApiForWidgetTest(),
            contentApi = api,
        )

        runTest {
            controller.loadIam()
            controller.loadContentDefinitions()
            controller.openContentDefinition(definitionId)
            controller.publishContentVersion(
                definitionId = definitionId,
                versionId = versionId,
                mode = "update_in_place_where_untouched",
                publishNote = null,
                confirmedAffectedCount = 10,
            )
        }

        runComposeUiTest {
            setContent {
                EnglishContent {
                    ObservingContentTab(controller = controller, currentUserId = "user-1")
                }
            }
            waitForIdle()

            assertTrue(
                onAllNodesWithText("3 tenant widget rebuild(s) failed", substring = true).fetchSemanticsNodes().isNotEmpty(),
                "a publish job with 3 rebuildFailedWidgetIds must render the exact failed count somewhere on screen",
            )
        }
    }
}

private class FakeContentApi(
    private val definitions: List<PlatformContentDefinition>,
    private val definitionDetail: PlatformContentDefinitionDetail,
    private val preview: PublishPreview = PublishPreview(affectedCount = 0, skippedCount = 0),
    private val publishResult: PlatformContentPublishJob? = null,
) : PlatformContentApi {
    override suspend fun listDefinitions(kind: String?, page: Int, pageSize: Int): ApiResult<PaginatedEnvelope<PlatformContentDefinition>> =
        ApiResult.Ok(PaginatedEnvelope(definitions))

    override suspend fun getDefinition(definitionId: String): ApiResult<PlatformContentDefinitionDetail> =
        ApiResult.Ok(definitionDetail)

    override suspend fun createDefinition(body: CreateContentDefinitionBody): ApiResult<PlatformContentDefinition> =
        ApiResult.Ok(definitions.first())

    override suspend fun draftVersion(definitionId: String, body: DraftContentVersionBody) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))

    override suspend fun getVersion(definitionId: String, versionId: String) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))

    override suspend fun previewPublish(definitionId: String, versionId: String, body: PublishPreviewBody): ApiResult<PublishPreview> =
        ApiResult.Ok(preview)

    override suspend fun publish(definitionId: String, versionId: String, body: PublishContentBody): ApiResult<PlatformContentPublishJob> =
        publishResult?.let { ApiResult.Ok(it) } ?: ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))

    override suspend fun getPublishJob(publishJobId: String) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))

    override suspend fun retireDefinition(definitionId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
}

/** Returns one active principal for `userId = "user-1"` with no entry in effectivePermissions — ContentTab
 * treats an unresolved lookup as not-yet-denied, so every gate in this test renders enabled without needing
 * to fabricate a full permission set. */
private class FakeIamApiForWidgetTest : PlatformIamApi {
    override suspend fun listRoles(): ApiResult<List<IamRole>> = ApiResult.Ok(emptyList())
    override suspend fun listPrincipals(): ApiResult<List<IamPrincipalSummary>> =
        ApiResult.Ok(listOf(IamPrincipalSummary(id = "principal-1", userId = "user-1", name = "Operator")))
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

private class NoopAdminApiForWidgetTest : AdminApi {
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
    override suspend fun getTiers() = ApiResult.Ok(emptyList<bot.nomnomz.dashboard.core.network.AdminTier>())
    override suspend fun previewTierChange(tierId: String) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun createTier(body: bot.nomnomz.dashboard.core.network.AdminCreateTierRequest) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun updateTier(tierId: String, body: bot.nomnomz.dashboard.core.network.AdminUpdateTierRequest) =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
}

private class NoopPlatformAdminApiForWidgetTest : PlatformAdminApi {
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
