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
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.editor.CompileFeedback
import bot.nomnomz.dashboard.core.editor.ProjectEditorIO
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
import kotlin.test.assertEquals
import kotlin.test.assertTrue

/**
 * S-ADMIN-2e: a `Kind = "code_script"` definition's draft editor must author its `payloadJson` through the
 * SAME shared multi-file project editor ([ProjectEditorIO]) the tenant-side Code Scripts page (and Widgets)
 * open for their own source — never a raw, undifferentiated JSON textarea. The real editor implementation
 * opens a native overlay outside the Compose semantics tree (a non-modal Swing dialog on desktop, an iframe
 * on web), so two complementary proofs are needed:
 *
 *  1. [opening_a_code_script_definition_draft_renders_the_shared_editor_entry_point_with_the_seeded_source] —
 *     mounted through the REAL [ContentTab], proving the code-script kind's draft dialog wires
 *     [CodeScriptPayloadEditor] (its own entry-point control renders, keyed by the SAME content description
 *     the tenant `CodeScriptsScreen` uses for its "Edit & compile" button) instead of the generic
 *     `JsonPayloadField` fallback, and that the seeded payload's source round-trips through the real
 *     `{"sourceCode": ...}` parse (not left opaque inside an unread JSON string).
 *  2. [code_script_payload_editor_drives_the_shared_project_editor_io_contract_and_captures_the_edited_source] —
 *     mounts [CodeScriptPayloadEditor] directly with a fake [ProjectEditorIO], proving a click on that entry
 *     point calls the EXACT SAME interface method (`editAndCompile`, with `entryPath = "script.ts"` and
 *     `language = "script"`) the tenant surface calls, and that the editor's compiled result really flows
 *     back into the draft's `payloadJson` — a passing test that never drives this contract would not count.
 */
@OptIn(ExperimentalTestApi::class)
class AdminContentCodeScriptAuthoringTest {

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

    private val codeScriptPayloadJson: String = """{"sourceCode":"bot.send('hello from platform script');"}"""

    @Test
    fun opening_a_code_script_definition_draft_renders_the_shared_editor_entry_point_with_the_seeded_source() {
        val definitionId = "def-code-script-1"
        val versionId = "ver-code-script-1"
        val definition = PlatformContentDefinition(
            id = definitionId,
            kind = "code_script",
            key = "welcome-script",
            displayName = "Welcome Script",
            currentVersion = 1,
            currentVersionId = versionId,
            createdAt = "2026-09-06T00:00:00Z",
        )
        val version = PlatformContentVersion(
            id = versionId,
            definitionId = definitionId,
            version = 1,
            contentHash = "abc123",
            payloadJson = codeScriptPayloadJson,
            publishedAt = "2026-09-06T00:00:00Z",
            draftedAt = "2026-09-06T00:00:00Z",
            draftedByPrincipalId = "principal-1",
        )
        val api = FakeContentApiForCodeScriptTest(
            definitions = listOf(definition),
            definitionDetail = PlatformContentDefinitionDetail(definition = definition, versions = listOf(version)),
        )
        val controller = AdminController(
            api = NoopAdminApiForCodeScriptTest(),
            iamApi = FakeIamApiForCodeScriptTest(),
            platformAdminApi = NoopPlatformAdminApiForCodeScriptTest(),
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

            // "Edit & compile" is a GlyphButton — its label is a hover tooltip + accessibility contentDescription,
            // not visible Text — and is the SAME control (SAME string resource) the tenant `CodeScriptsScreen`
            // renders for its own project editor entry point. A raw `JsonPayloadField` fallback would never
            // expose this control.
            onNodeWithContentDescription("Edit & compile").assertExists()

            // The seeded payload's `sourceCode` must have round-tripped through the real
            // `{"sourceCode": "..."}` decode (CodeScriptPayloadWire) — not stayed unread inside an opaque
            // JSON string — to prove this is genuine code-script authoring, not a read-only shell.
            assertTrue(
                onAllNodesWithText("hello from platform script", substring = true).fetchSemanticsNodes().isNotEmpty(),
                "the code script payload's seeded source must render through the shared editor surface",
            )

            // The generic JSON fallback field must NOT be present for this kind.
            onAllNodesWithText("Payload (JSON)").assertCountEquals(0)
        }
    }

    @Test
    fun code_script_payload_editor_drives_the_shared_project_editor_io_contract_and_captures_the_edited_source() {
        val fakeEditor = FakeProjectEditorIOForCodeScriptTest()
        var latestPayloadJson: String = """{"sourceCode":"bot.send('v1');"}"""

        runComposeUiTest {
            setContent {
                EnglishContent {
                    CodeScriptPayloadEditor(
                        payloadJson = latestPayloadJson,
                        onPayloadJsonChange = { latestPayloadJson = it },
                        projectEditor = fakeEditor,
                    )
                }
            }

            onNodeWithContentDescription("Edit & compile").performClick()
            waitForIdle()
        }

        assertTrue(fakeEditor.invoked, "clicking the entry point must open the shared ProjectEditorIO contract")
        assertEquals("script.ts", fakeEditor.capturedEntryPath)
        assertEquals("script", fakeEditor.capturedLanguage)
        assertEquals(mapOf("script.ts" to "bot.send('v1');"), fakeEditor.capturedInitialFiles)
        assertEquals(
            """{"sourceCode":"bot.send('edited by admin');"}""",
            latestPayloadJson,
            "the editor's compiled result must flow back into the draft payloadJson",
        )
    }
}

/** Records the exact [ProjectEditorIO] call the admin authoring surface makes, then simulates the operator
 * editing the entry file once and hitting "Save & Compile" before the (never actually opened) overlay
 * would close — proving the compiled result flows back to the caller exactly like the real Swing/iframe
 * implementations do. */
private class FakeProjectEditorIOForCodeScriptTest : ProjectEditorIO {
    var invoked: Boolean = false
    var capturedInitialFiles: Map<String, String>? = null
    var capturedEntryPath: String? = null
    var capturedLanguage: String? = null

    override suspend fun editAndCompile(
        title: String,
        initialFiles: Map<String, String>,
        entryPath: String,
        language: String,
        sdkTypes: String,
        eventSubscriptions: List<String>,
        compile: suspend (Map<String, String>) -> CompileFeedback,
    ) {
        invoked = true
        capturedInitialFiles = initialFiles
        capturedEntryPath = entryPath
        capturedLanguage = language
        compile(mapOf(entryPath to "bot.send('edited by admin');"))
    }
}

private class FakeContentApiForCodeScriptTest(
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
private class FakeIamApiForCodeScriptTest : PlatformIamApi {
    override suspend fun listRoles(): ApiResult<List<IamRole>> = ApiResult.Ok(emptyList())
    override suspend fun listPrincipals(): ApiResult<List<IamPrincipalSummary>> =
        ApiResult.Ok(listOf(IamPrincipalSummary(id = "principal-1", userId = "user-1", name = "Operator")))
    // The tab gates on the caller's OWN effective content keys, so a fake that answers "no permissions"
    // correctly renders the read-denied panel and this test would be asserting against an empty tab.
    // Grant the real key set an authoring operator holds.
    override suspend fun effectivePermissions(principalId: String, scopeChannelId: String?) =
        ApiResult.Ok(listOf("content:read", "content:author", "content:publish", "content:publish:force"))
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

private class NoopAdminApiForCodeScriptTest : AdminApi {
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

private class NoopPlatformAdminApiForCodeScriptTest : PlatformAdminApi {
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
