// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.admin.state

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
import bot.nomnomz.dashboard.core.network.IamPrincipal
import bot.nomnomz.dashboard.core.network.IamPrincipalSummary
import bot.nomnomz.dashboard.core.network.IamRole
import bot.nomnomz.dashboard.core.network.IamRoleAssignment
import bot.nomnomz.dashboard.core.network.ImpersonationTokenDto
import bot.nomnomz.dashboard.core.network.InviteCode
import bot.nomnomz.dashboard.core.network.PaginatedEnvelope
import bot.nomnomz.dashboard.core.network.PlatformAdminApi
import bot.nomnomz.dashboard.core.network.PlatformContentApi
import bot.nomnomz.dashboard.core.network.PlatformContentDefinition
import bot.nomnomz.dashboard.core.network.PlatformContentDefinitionDetail
import bot.nomnomz.dashboard.core.network.PlatformContentPublishJob
import bot.nomnomz.dashboard.core.network.PlatformContentPublishModes
import bot.nomnomz.dashboard.core.network.PlatformContentVersion
import bot.nomnomz.dashboard.core.network.PlatformEvent
import bot.nomnomz.dashboard.core.network.PlatformIamApi
import bot.nomnomz.dashboard.core.network.ProviderCredential
import bot.nomnomz.dashboard.core.network.PublishContentBody
import bot.nomnomz.dashboard.core.network.PublishPreview
import bot.nomnomz.dashboard.core.network.PublishPreviewBody
import bot.nomnomz.dashboard.core.network.SaveProviderCredentialBody
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

// S-ADMIN-2b — proves the platform content authoring flow actually reaches AdminState: a successful
// definitions fetch lands in state.contentDefinitions, a failed one surfaces on state.contentError (never a
// silently-empty list), the publish-preview blast radius rendered is the EXACT count the endpoint returned
// (not a guess), and a `force` publish is refused client-side without a justification before it ever reaches
// the network.
class AdminControllerContentTest {

    @Test
    fun successful_definitions_load_reaches_state() = runTest {
        val definition = PlatformContentDefinition(
            id = "def-1",
            kind = "command",
            key = "sr",
            displayName = "Song Request",
            currentVersionId = "v-1",
            currentVersion = 1,
            createdAt = "2026-09-01T00:00:00Z",
        )
        val controller = contentController(FakeContentApi(definitions = listOf(definition)))

        controller.loadContentDefinitions()

        assertEquals(listOf(definition), controller.state.value.contentDefinitions)
        assertNull(controller.state.value.contentError)
    }

    @Test
    fun failed_definitions_load_surfaces_a_visible_error_not_an_empty_list() = runTest {
        val controller = contentController(FakeContentApi(listFailure = ApiError(500, "SERVER_ERROR", "boom")))

        controller.loadContentDefinitions()

        assertTrue(controller.state.value.contentDefinitions.isEmpty())
        assertEquals("boom", controller.state.value.contentError)
    }

    @Test
    fun publish_preview_renders_the_exact_counted_blast_radius_the_endpoint_returned() = runTest {
        val preview = PublishPreview(affectedCount = 37, skippedCount = 4, sampleTenantNames = listOf("acme", "beta"))
        val controller = contentController(FakeContentApi(preview = preview))

        controller.previewContentPublish("def-1", "v-1", PlatformContentPublishModes.UpdateInPlaceWhereUntouched)

        val rendered: PublishPreview? = controller.state.value.publishPreview
        assertNotNull(rendered)
        assertEquals(37, rendered.affectedCount)
        assertEquals(4, rendered.skippedCount)
        assertEquals(listOf("acme", "beta"), rendered.sampleTenantNames)
    }

    @Test
    fun force_publish_without_a_justification_never_reaches_the_network() = runTest {
        val api = FakeContentApi(
            preview = PublishPreview(affectedCount = 10, skippedCount = 0),
            publishJob = PlatformContentPublishJob(
                id = "job-1",
                definitionId = "def-1",
                toVersion = 2,
                mode = PlatformContentPublishModes.Force,
                requestedByPrincipalId = "principal-1",
                requestedAt = "2026-09-01T00:00:00Z",
                previewAffectedCount = 10,
                previewSkippedCount = 0,
                status = "completed",
            ),
        )
        val controller = contentController(api)

        controller.publishContentVersion(
            definitionId = "def-1",
            versionId = "v-1",
            mode = PlatformContentPublishModes.Force,
            publishNote = "   ", // blank after trim — must be refused, not sent
            confirmedAffectedCount = 10,
        )

        assertEquals(0, api.publishCallCount)
        assertNotNull(controller.state.value.publishError)
        assertNull(controller.state.value.lastPublishJob)
    }

    @Test
    fun force_publish_with_a_justification_submits_and_records_the_completed_job() = runTest {
        val job = PlatformContentPublishJob(
            id = "job-2",
            definitionId = "def-1",
            toVersion = 2,
            mode = PlatformContentPublishModes.Force,
            requestedByPrincipalId = "principal-1",
            requestedAt = "2026-09-01T00:00:00Z",
            previewAffectedCount = 10,
            previewSkippedCount = 0,
            confirmedAffectedCount = 10,
            status = "completed",
        )
        val api = FakeContentApi(
            definition = PlatformContentDefinitionDetail(
                definition = PlatformContentDefinition(
                    id = "def-1",
                    kind = "command",
                    key = "sr",
                    displayName = "Song Request",
                    createdAt = "2026-09-01T00:00:00Z",
                ),
                versions = emptyList(),
            ),
            preview = PublishPreview(affectedCount = 10, skippedCount = 0),
            publishJob = job,
        )
        val controller = contentController(api)

        controller.publishContentVersion(
            definitionId = "def-1",
            versionId = "v-1",
            mode = PlatformContentPublishModes.Force,
            publishNote = "Security fix — CVE-2026-1234",
            confirmedAffectedCount = 10,
        )

        assertEquals(1, api.publishCallCount)
        assertEquals(job, controller.state.value.lastPublishJob)
        assertNull(controller.state.value.publishError)
    }

    private fun contentController(contentApi: PlatformContentApi): AdminController =
        AdminController(
            api = StubAdminApi(),
            iamApi = StubPlatformIamApi(),
            platformAdminApi = StubPlatformAdminApi(),
            contentApi = contentApi,
        )
}

/** A controllable [PlatformContentApi] fake — every method returns a canned success or the configured
 * failure, and [publishCallCount] proves whether [publish] was actually invoked. */
private class FakeContentApi(
    private val definitions: List<PlatformContentDefinition> = emptyList(),
    private val listFailure: ApiError? = null,
    private val definition: PlatformContentDefinitionDetail? = null,
    private val preview: PublishPreview? = null,
    private val publishJob: PlatformContentPublishJob? = null,
) : PlatformContentApi {
    var publishCallCount: Int = 0
        private set

    override suspend fun listDefinitions(kind: String?, page: Int, pageSize: Int): ApiResult<PaginatedEnvelope<PlatformContentDefinition>> =
        listFailure?.let { ApiResult.Failure(it) } ?: ApiResult.Ok(PaginatedEnvelope(definitions))

    override suspend fun getDefinition(definitionId: String): ApiResult<PlatformContentDefinitionDetail> =
        definition?.let { ApiResult.Ok(it) } ?: ApiResult.Failure(ApiError(404, "NOT_FOUND", "not found"))

    override suspend fun createDefinition(body: CreateContentDefinitionBody): ApiResult<PlatformContentDefinition> =
        ApiResult.Ok(definitions.firstOrNull() ?: error("no definition configured"))

    override suspend fun draftVersion(definitionId: String, body: DraftContentVersionBody): ApiResult<PlatformContentVersion> =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "not used in this test"))

    override suspend fun getVersion(definitionId: String, versionId: String): ApiResult<PlatformContentVersion> =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "not used in this test"))

    override suspend fun previewPublish(definitionId: String, versionId: String, body: PublishPreviewBody): ApiResult<PublishPreview> =
        preview?.let { ApiResult.Ok(it) } ?: ApiResult.Failure(ApiError(500, "SERVER_ERROR", "no preview configured"))

    override suspend fun publish(definitionId: String, versionId: String, body: PublishContentBody): ApiResult<PlatformContentPublishJob> {
        publishCallCount += 1
        return publishJob?.let { ApiResult.Ok(it) } ?: ApiResult.Failure(ApiError(500, "SERVER_ERROR", "no job configured"))
    }

    override suspend fun getPublishJob(publishJobId: String): ApiResult<PlatformContentPublishJob> =
        publishJob?.let { ApiResult.Ok(it) } ?: ApiResult.Failure(ApiError(404, "NOT_FOUND", "not found"))

    override suspend fun retireDefinition(definitionId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
}

private class StubAdminApi : AdminApi {
    override suspend fun getStats(): ApiResult<AdminStats> = ApiResult.Ok(AdminStats(0, 0, 0, "ok", 0, 0))
    override suspend fun getChannels(search: String?, page: Int, pageSize: Int, sort: String?, isLive: Boolean?): ApiResult<PaginatedEnvelope<AdminChannel>> =
        ApiResult.Ok(PaginatedEnvelope(emptyList()))
    override suspend fun getUsers(search: String?, page: Int, pageSize: Int, sort: String?, role: String?): ApiResult<PaginatedEnvelope<AdminUser>> =
        ApiResult.Ok(PaginatedEnvelope(emptyList()))
    override suspend fun getSystem(): ApiResult<AdminSystem> = ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun getHealth(): ApiResult<List<AdminServiceHealth>> = ApiResult.Ok(emptyList())
    override suspend fun getEvents(): ApiResult<List<PlatformEvent>> = ApiResult.Ok(emptyList())
    override suspend fun getFeatureFlags(): ApiResult<List<FeatureFlag>> = ApiResult.Ok(emptyList())
    override suspend fun setFeatureFlag(body: AdminSetFeatureFlagRequest): ApiResult<FeatureFlag> =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun setFeatureFlagOverride(flagKey: String, broadcasterId: String, body: AdminSetFeatureFlagOverrideRequest): ApiResult<Unit> =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun deleteFeatureFlagOverride(flagKey: String, broadcasterId: String): ApiResult<Unit> =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun getInviteCodes(page: Int, pageSize: Int): ApiResult<PaginatedEnvelope<InviteCode>> =
        ApiResult.Ok(PaginatedEnvelope(emptyList()))
    override suspend fun createInviteCode(body: AdminCreateInviteCodeRequest): ApiResult<InviteCode> =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun revokeInviteCode(inviteCodeId: String): ApiResult<Unit> =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun grantTier(broadcasterId: String, body: AdminGrantTierRequest): ApiResult<Unit> =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun grantFounderBadge(broadcasterId: String): ApiResult<Unit> =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun impersonate(subjectUserId: String, accessGrantId: String, justification: String): ApiResult<ImpersonationTokenDto> =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun endImpersonation(accessGrantId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun getProviderCredentials(): ApiResult<List<ProviderCredential>> = ApiResult.Ok(emptyList())
    override suspend fun saveProviderCredential(provider: String, body: SaveProviderCredentialBody): ApiResult<ProviderCredential> =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun clearProviderCredential(provider: String): ApiResult<ProviderCredential> =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
}

private class StubPlatformIamApi : PlatformIamApi {
    override suspend fun listRoles(): ApiResult<List<IamRole>> = ApiResult.Ok(emptyList())
    override suspend fun listPrincipals(): ApiResult<List<IamPrincipalSummary>> = ApiResult.Ok(emptyList())
    override suspend fun effectivePermissions(principalId: String, scopeChannelId: String?): ApiResult<List<String>> =
        ApiResult.Ok(emptyList())
    override suspend fun createPrincipal(body: CreatePrincipalBody): ApiResult<IamPrincipal> =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun deactivatePrincipal(principalId: String, reason: String?): ApiResult<Unit> =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun reactivatePrincipal(principalId: String): ApiResult<Unit> =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun assignRole(body: AssignRoleBody): ApiResult<IamRoleAssignment> =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
    override suspend fun revokeAssignment(assignmentId: String, reason: String?): ApiResult<Unit> =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))
}

private class StubPlatformAdminApi : PlatformAdminApi {
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
