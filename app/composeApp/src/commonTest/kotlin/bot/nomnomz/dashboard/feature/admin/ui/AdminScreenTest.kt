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
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.hasSetTextAction
import androidx.compose.ui.test.isToggleable
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performImeAction
import androidx.compose.ui.test.performTextInput
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
import bot.nomnomz.dashboard.core.network.BeginTenantAccessBody
import bot.nomnomz.dashboard.core.network.CreatePrincipalBody
import bot.nomnomz.dashboard.core.network.FeatureFlag
import bot.nomnomz.dashboard.core.network.IamAuditEntry
import bot.nomnomz.dashboard.core.network.IamPrincipal
import bot.nomnomz.dashboard.core.network.IamPrincipalSummary
import bot.nomnomz.dashboard.core.network.IamRole
import bot.nomnomz.dashboard.core.network.IamRoleAssignment
import bot.nomnomz.dashboard.core.network.ImpersonationTokenDto
import bot.nomnomz.dashboard.core.network.InviteCode
import bot.nomnomz.dashboard.core.network.PaginatedEnvelope
import bot.nomnomz.dashboard.core.network.PlatformAdminApi
import bot.nomnomz.dashboard.core.network.PlatformEvent
import bot.nomnomz.dashboard.core.network.PlatformIamApi
import bot.nomnomz.dashboard.core.network.ReinstateTenantBody
import bot.nomnomz.dashboard.core.network.SuspendTenantBody
import bot.nomnomz.dashboard.core.network.TenantAccessGrant
import bot.nomnomz.dashboard.feature.admin.state.AdminController
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

// S-OWN08 regression: AdminController.load() has always set AdminState.error on a failed stats/channels/users/
// system fetch (state/AdminController.kt), but AdminScreen never rendered it anywhere — a failed initial load
// left the platform-admin panel silently empty with no indication anything had gone wrong (a "truthful data,
// not fake enforcement" violation: the failure state existed but was invisible). Mounts the real composable and
// reads what actually renders, the same class of regression ResourceLimitsSectionTest guards against elsewhere.
@OptIn(ExperimentalTestApi::class)
class AdminScreenTest {

    @Composable
    private fun EnglishContent(content: @Composable () -> Unit) {
        AppEnvironment(tag = "en") {
            NomNomzTheme { content() }
        }
    }

    @Test
    fun a_load_error_renders_as_a_visible_banner() = runComposeUiTest {
        setContent {
            EnglishContent {
                AdminLoadErrorBanner(error = "Failed to reach the platform admin API")
            }
        }

        assertTrue(
            onAllNodesWithText("Failed to reach the platform admin API").fetchSemanticsNodes().isNotEmpty(),
            "a set AdminState.error must render as a visible banner, not be silently dropped",
        )
    }

    @Test
    fun no_error_renders_nothing() = runComposeUiTest {
        setContent {
            EnglishContent {
                AdminLoadErrorBanner(error = null)
            }
        }

        assertTrue(
            onAllNodesWithText("Failed", substring = true).fetchSemanticsNodes().isEmpty(),
            "a null error must render no banner at all",
        )
    }

    // S-OWN08a: FeatureFlagsTab used to be read-only despite AdminController already exposing working
    // setFeatureFlag/setFeatureFlagOverride/deleteFeatureFlagOverride wrappers. Proves the toggle actually
    // reaches the backend call with the right flag key/value, not just that a switch renders.
    @OptIn(ExperimentalTestApi::class)
    @Test
    fun tapping_the_flag_toggle_calls_set_feature_flag_with_the_flipped_value() {
        val api = RecordingFeatureFlagAdminApi(
            initialFlags = listOf(
                FeatureFlag(key = "new-dashboard", description = "New dashboard UI", isEnabledGlobally = false, rolloutPercentage = 50),
            ),
        )
        val controller = AdminController(api = api, iamApi = NoopPlatformIamApi(), platformAdminApi = NoopPlatformAdminApi())
        runTest { controller.load() }

        runComposeUiTest {
            setContent {
                EnglishContent {
                    FeatureFlagsTab(state = controller.state.value, controller = controller)
                }
            }

            onNode(isToggleable()).performClick()
            waitForIdle()
        }

        assertEquals(1, api.setFeatureFlagCalls.size)
        val request: AdminSetFeatureFlagRequest = api.setFeatureFlagCalls.single()
        assertEquals("new-dashboard", request.key)
        assertEquals(true, request.isEnabledGlobally, "tapping the off switch must flip it on")
        assertEquals(50, request.rolloutPercentage, "the existing rollout percentage must be preserved, not reset")
    }

    // Every write in AdminController's "feature flags & billing" block discarded its ApiResult and reloaded
    // regardless, so a 403 on a flag toggle or a tier grant left the panel looking like nothing had happened.
    // They now share writeThenReload, which sets actionError the way the IAM writes always have.
    @Test
    fun a_rejected_flag_write_surfaces_the_error_instead_of_reloading_silently() = runTest {
        val api = RecordingFeatureFlagAdminApi(
            initialFlags = listOf(
                FeatureFlag(key = "new-dashboard", description = "New dashboard UI", isEnabledGlobally = false, rolloutPercentage = 50),
            ),
            setFeatureFlagFailure = ApiError(status = 403, code = null, message = "Not permitted on this deployment"),
        )
        val controller = AdminController(api = api, iamApi = NoopPlatformIamApi(), platformAdminApi = NoopPlatformAdminApi())

        controller.setFeatureFlag(
            AdminSetFeatureFlagRequest(key = "new-dashboard", isEnabledGlobally = true, rolloutPercentage = 50),
        )

        assertEquals(
            "Not permitted on this deployment",
            controller.state.value.actionError,
            "a rejected admin write must surface its error, not be swallowed",
        )
    }

    // S-OWN08b: the admin Channels/Users lists had no search — unbounded, unsearchable on any real deployment.
    // Proves submitting the search box actually reaches AdminApi.getChannels/getUsers with the typed value, not
    // just that a text field renders.
    @OptIn(ExperimentalTestApi::class)
    @Test
    fun submitting_the_channel_search_calls_get_channels_with_the_typed_value() {
        val api = RecordingListSearchAdminApi()
        val controller = AdminController(api = api, iamApi = NoopPlatformIamApi(), platformAdminApi = NoopPlatformAdminApi())

        runComposeUiTest {
            setContent {
                EnglishContent {
                    ChannelsTab(state = controller.state.value, controller = controller)
                }
            }

            onAllNodes(matcher = hasSetTextAction())[0].performTextInput("pixelqueen")
            onAllNodes(matcher = hasSetTextAction())[0].performImeAction()
            waitForIdle()
        }

        assertEquals(listOf<String?>("pixelqueen"), api.channelSearchCalls, "the submitted search text must reach AdminApi.getChannels")
    }

    @OptIn(ExperimentalTestApi::class)
    @Test
    fun submitting_the_user_search_calls_get_users_with_the_typed_value() {
        val api = RecordingListSearchAdminApi()
        val controller = AdminController(api = api, iamApi = NoopPlatformIamApi(), platformAdminApi = NoopPlatformAdminApi())

        runComposeUiTest {
            setContent {
                EnglishContent {
                    UsersTab(state = controller.state.value, controller = controller)
                }
            }

            onAllNodes(matcher = hasSetTextAction())[0].performTextInput("rockhound")
            onAllNodes(matcher = hasSetTextAction())[0].performImeAction()
            waitForIdle()
        }

        assertEquals(listOf<String?>("rockhound"), api.userSearchCalls, "the submitted search text must reach AdminApi.getUsers")
    }

    // S-OWN08-DESIGN-PARITY: Overview/Channels/Users/System/Billing rendered raw Row/Column/Card list markup
    // with no shared empty-state primitive, unlike AdminTenantsTab/AdminIamTab which use EmptyLine with
    // tab-specific copy. Proves each of the five tabs now renders EmptyLine (not a bespoke Text) when its list
    // is empty, matching the reference tabs' pattern.
    @OptIn(ExperimentalTestApi::class)
    @Test
    fun overview_tab_renders_empty_line_for_empty_registry_and_log() {
        val controller = AdminController(api = RecordingListSearchAdminApi(), iamApi = NoopPlatformIamApi(), platformAdminApi = NoopPlatformAdminApi())

        runComposeUiTest {
            setContent {
                EnglishContent {
                    OverviewTab(state = controller.state.value)
                }
            }

            assertTrue(
                onAllNodesWithText("No live channel updates yet.").fetchSemanticsNodes().isNotEmpty(),
                "an empty registry must render EmptyLine with registry-specific copy",
            )
            assertTrue(
                onAllNodesWithText("No operator events yet.").fetchSemanticsNodes().isNotEmpty(),
                "an empty operator log must render EmptyLine with log-specific copy",
            )
        }
    }

    @OptIn(ExperimentalTestApi::class)
    @Test
    fun channels_tab_renders_empty_line_with_channel_specific_copy() {
        val controller = AdminController(api = RecordingListSearchAdminApi(), iamApi = NoopPlatformIamApi(), platformAdminApi = NoopPlatformAdminApi())

        runComposeUiTest {
            setContent {
                EnglishContent {
                    ChannelsTab(state = controller.state.value, controller = controller)
                }
            }

            assertTrue(
                onAllNodesWithText("No channels match.").fetchSemanticsNodes().isNotEmpty(),
                "an empty channel list must render EmptyLine with channel-specific copy",
            )
        }
    }

    // S-OWN08: the Users tab was read-only — id, login, role, channel count, and nothing an operator
    // could do or even learn. Whether a person holds platform access lives in IAM, keyed by principal,
    // so the one question a platform admin actually asks of a user row had no answer on the row. These
    // prove the answer is now there AND that it is derived from the real principal list rather than
    // assumed: a user with no principal must not read as "has access", and a user whose principal is
    // deactivated must not read as if their role still works.

    @OptIn(ExperimentalTestApi::class)
    @Test
    fun a_user_with_no_iam_principal_is_shown_as_having_no_platform_access() {
        val controller = AdminController(api = RecordingListSearchAdminApi(), iamApi = NoopPlatformIamApi(), platformAdminApi = NoopPlatformAdminApi())
        val state = controller.state.value.copy(
            users = listOf(AdminUser(id = "u1", login = "rockhound", displayName = "Rockhound", role = "viewer", channelCount = 1, createdAt = "2026-01-01T00:00:00Z")),
        )

        runComposeUiTest {
            setContent { EnglishContent { UsersTab(state = state, controller = controller) } }

            assertTrue(
                onAllNodesWithText("No platform access").fetchSemanticsNodes().isNotEmpty(),
                "a user with no matching IAM principal must say so, not stay silent",
            )
            assertTrue(
                onAllNodesWithText("Grant platform access").fetchSemanticsNodes().isNotEmpty(),
                "the action that follows from having no access must be on the row",
            )
        }
    }

    @OptIn(ExperimentalTestApi::class)
    @Test
    fun a_user_with_a_principal_shows_their_real_role_names_and_defers_to_iam() {
        val controller = AdminController(api = RecordingListSearchAdminApi(), iamApi = NoopPlatformIamApi(), platformAdminApi = NoopPlatformAdminApi())
        val state = controller.state.value.copy(
            users = listOf(AdminUser(id = "u1", login = "rockhound", displayName = "Rockhound", role = "viewer", channelCount = 1, createdAt = "2026-01-01T00:00:00Z")),
            principals = listOf(
                IamPrincipalSummary(
                    id = "p1",
                    userId = "u1",
                    name = "Rockhound",
                    activeAssignments = listOf(
                        IamRoleAssignment(id = "a1", principalId = "p1", roleId = "r1", roleName = "Support", createdAt = "2026-01-01T00:00:00Z"),
                    ),
                )
            ),
        )

        runComposeUiTest {
            setContent { EnglishContent { UsersTab(state = state, controller = controller) } }

            assertTrue(
                onAllNodesWithText("Support").fetchSemanticsNodes().isNotEmpty(),
                "the row must name the role the principal actually holds",
            )
            assertTrue(
                onAllNodesWithText("Manage in IAM").fetchSemanticsNodes().isNotEmpty(),
                "editing stays in IAM — this row points there rather than forking the editor",
            )
            assertTrue(
                onAllNodesWithText("Grant platform access").fetchSemanticsNodes().isEmpty(),
                "a user who already has access must not be offered a grant",
            )
        }
    }

    @OptIn(ExperimentalTestApi::class)
    @Test
    fun a_deactivated_principal_is_called_out_rather_than_reading_as_working_access() {
        // "Holds a role" and "can currently use it" are different facts. Showing only the first is the
        // confident kind of wrong: an operator would think access is live when it is switched off.
        val controller = AdminController(api = RecordingListSearchAdminApi(), iamApi = NoopPlatformIamApi(), platformAdminApi = NoopPlatformAdminApi())
        val state = controller.state.value.copy(
            users = listOf(AdminUser(id = "u1", login = "rockhound", displayName = "Rockhound", role = "viewer", channelCount = 1, createdAt = "2026-01-01T00:00:00Z")),
            principals = listOf(
                IamPrincipalSummary(id = "p1", userId = "u1", name = "Rockhound", isActive = false)
            ),
        )

        runComposeUiTest {
            setContent { EnglishContent { UsersTab(state = state, controller = controller) } }

            assertTrue(
                onAllNodesWithText("Inactive").fetchSemanticsNodes().isNotEmpty(),
                "a deactivated principal must be visible on the row",
            )
        }
    }

    @OptIn(ExperimentalTestApi::class)
    @Test
    fun granting_platform_access_creates_the_principal_with_the_chosen_role() {
        val iam = RecordingPromoteIamApi()
        val controller = AdminController(api = RecordingListSearchAdminApi(), iamApi = iam, platformAdminApi = NoopPlatformAdminApi())
        val state = controller.state.value.copy(
            users = listOf(AdminUser(id = "u1", login = "rockhound", displayName = "Rockhound", role = "viewer", channelCount = 1, createdAt = "2026-01-01T00:00:00Z")),
            roles = listOf(IamRole(id = "r1", name = "Support")),
        )

        runComposeUiTest {
            setContent { EnglishContent { UsersTab(state = state, controller = controller) } }

            onAllNodesWithText("Grant platform access")[0].performClick()
            waitForIdle()
            // Open the role picker (its button shows the em-dash placeholder until something is chosen)
            // and take the only role offered.
            onAllNodesWithText("—")[0].performClick()
            waitForIdle()
            onAllNodesWithText("Support")[0].performClick()
            waitForIdle()
            // The dialog's own confirm is the LAST node carrying this label (the row button is the first).
            val confirms = onAllNodesWithText("Grant platform access").fetchSemanticsNodes()
            onAllNodesWithText("Grant platform access")[confirms.size - 1].performClick()
            waitForIdle()
        }

        assertEquals(1, iam.createdPrincipals.size, "confirming the dialog must create the principal")
        val created = iam.createdPrincipals.first()
        assertEquals("u1", created.userId, "the principal must be created for the row user")
        assertEquals(listOf("r1"), created.roleIds, "the chosen role must be the one granted")
        assertEquals(0, created.principalType, "granting a person access creates an employee principal, not a service account")
    }

    @OptIn(ExperimentalTestApi::class)
    @Test
    fun users_tab_renders_empty_line_with_user_specific_copy() {
        val controller = AdminController(api = RecordingListSearchAdminApi(), iamApi = NoopPlatformIamApi(), platformAdminApi = NoopPlatformAdminApi())

        runComposeUiTest {
            setContent {
                EnglishContent {
                    UsersTab(state = controller.state.value, controller = controller)
                }
            }

            assertTrue(
                onAllNodesWithText("No users match.").fetchSemanticsNodes().isNotEmpty(),
                "an empty user list must render EmptyLine with user-specific copy",
            )
        }
    }

    @OptIn(ExperimentalTestApi::class)
    @Test
    fun system_tab_renders_empty_line_for_empty_health_list() {
        val controller = AdminController(api = RecordingListSearchAdminApi(), iamApi = NoopPlatformIamApi(), platformAdminApi = NoopPlatformAdminApi())

        runComposeUiTest {
            setContent {
                EnglishContent {
                    SystemTab(state = controller.state.value)
                }
            }

            assertTrue(
                onAllNodesWithText("No health checks reported.").fetchSemanticsNodes().isNotEmpty(),
                "an empty service health list must render EmptyLine with system-specific copy",
            )
        }
    }

    @OptIn(ExperimentalTestApi::class)
    @Test
    fun billing_tab_renders_empty_line_with_invite_specific_copy() {
        val controller = AdminController(api = RecordingListSearchAdminApi(), iamApi = NoopPlatformIamApi(), platformAdminApi = NoopPlatformAdminApi())

        runComposeUiTest {
            setContent {
                EnglishContent {
                    BillingTab(state = controller.state.value, controller = controller)
                }
            }

            assertTrue(
                onAllNodesWithText("No invite codes yet.").fetchSemanticsNodes().isNotEmpty(),
                "an empty invite code list must render EmptyLine with billing-specific copy",
            )
        }
    }
}

// ─── Fakes ─────────────────────────────────────────────────────────────────

private class RecordingFeatureFlagAdminApi(
    initialFlags: List<FeatureFlag>,
    private val setFeatureFlagFailure: ApiError? = null,
) : AdminApi {
    private val flags: MutableList<FeatureFlag> = initialFlags.toMutableList()
    val setFeatureFlagCalls: MutableList<AdminSetFeatureFlagRequest> = mutableListOf()

    override suspend fun getStats(): ApiResult<AdminStats> = ApiResult.Ok(AdminStats(0, 0, 0, "ok", 0, 0))
    override suspend fun getChannels(search: String?, page: Int, pageSize: Int): ApiResult<PaginatedEnvelope<AdminChannel>> =
        ApiResult.Ok(PaginatedEnvelope(emptyList()))
    override suspend fun getUsers(search: String?, page: Int, pageSize: Int): ApiResult<PaginatedEnvelope<AdminUser>> =
        ApiResult.Ok(PaginatedEnvelope(emptyList()))
    override suspend fun getSystem(): ApiResult<AdminSystem> = ApiResult.Ok(AdminSystem("ok", emptyList(), "1.0", 0, 0.0))
    override suspend fun getHealth(): ApiResult<List<AdminServiceHealth>> = ApiResult.Ok(emptyList())
    override suspend fun getEvents(): ApiResult<List<PlatformEvent>> = ApiResult.Ok(emptyList())
    override suspend fun getFeatureFlags(): ApiResult<List<FeatureFlag>> = ApiResult.Ok(flags.toList())

    override suspend fun setFeatureFlag(body: AdminSetFeatureFlagRequest): ApiResult<FeatureFlag> {
        setFeatureFlagCalls += body
        setFeatureFlagFailure?.let { return ApiResult.Failure(it) }
        val updated = FeatureFlag(
            key = body.key,
            description = body.description,
            isEnabledGlobally = body.isEnabledGlobally,
            rolloutPercentage = body.rolloutPercentage,
            minTierKey = body.minTierKey,
            requiresConsent = body.requiresConsent,
            deploymentMode = body.deploymentMode,
        )
        flags.removeAll { it.key == body.key }
        flags += updated
        return ApiResult.Ok(updated)
    }

    override suspend fun setFeatureFlagOverride(flagKey: String, broadcasterId: String, body: AdminSetFeatureFlagOverrideRequest): ApiResult<Unit> =
        ApiResult.Ok(Unit)
    override suspend fun deleteFeatureFlagOverride(flagKey: String, broadcasterId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun getInviteCodes(page: Int, pageSize: Int): ApiResult<PaginatedEnvelope<InviteCode>> =
        ApiResult.Ok(PaginatedEnvelope(emptyList()))
    override suspend fun createInviteCode(body: AdminCreateInviteCodeRequest): ApiResult<InviteCode> =
        ApiResult.Ok(InviteCode("id", "code", body.maxRedemptions, 0, body.grantsFoundersBadge))
    override suspend fun revokeInviteCode(inviteCodeId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun grantTier(broadcasterId: String, body: AdminGrantTierRequest): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun grantFounderBadge(broadcasterId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun impersonate(subjectUserId: String, accessGrantId: String, justification: String): ApiResult<ImpersonationTokenDto> =
        ApiResult.Failure(ApiError(500, null, "not stubbed"))
    override suspend fun endImpersonation(accessGrantId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
}

/** Records every search value AdminController forwards to [getChannels]/[getUsers] — S-OWN08b. */
private class RecordingListSearchAdminApi : AdminApi {
    val channelSearchCalls: MutableList<String?> = mutableListOf()
    val userSearchCalls: MutableList<String?> = mutableListOf()

    override suspend fun getStats(): ApiResult<AdminStats> = ApiResult.Ok(AdminStats(0, 0, 0, "ok", 0, 0))
    override suspend fun getChannels(search: String?, page: Int, pageSize: Int): ApiResult<PaginatedEnvelope<AdminChannel>> {
        channelSearchCalls += search
        return ApiResult.Ok(PaginatedEnvelope(emptyList()))
    }
    override suspend fun getUsers(search: String?, page: Int, pageSize: Int): ApiResult<PaginatedEnvelope<AdminUser>> {
        userSearchCalls += search
        return ApiResult.Ok(PaginatedEnvelope(emptyList()))
    }
    override suspend fun getSystem(): ApiResult<AdminSystem> = ApiResult.Ok(AdminSystem("ok", emptyList(), "1.0", 0, 0.0))
    override suspend fun getHealth(): ApiResult<List<AdminServiceHealth>> = ApiResult.Ok(emptyList())
    override suspend fun getEvents(): ApiResult<List<PlatformEvent>> = ApiResult.Ok(emptyList())
    override suspend fun getFeatureFlags(): ApiResult<List<FeatureFlag>> = ApiResult.Ok(emptyList())
    override suspend fun setFeatureFlag(body: AdminSetFeatureFlagRequest): ApiResult<FeatureFlag> =
        ApiResult.Failure(ApiError(500, null, "not stubbed"))
    override suspend fun setFeatureFlagOverride(flagKey: String, broadcasterId: String, body: AdminSetFeatureFlagOverrideRequest): ApiResult<Unit> =
        ApiResult.Ok(Unit)
    override suspend fun deleteFeatureFlagOverride(flagKey: String, broadcasterId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun getInviteCodes(page: Int, pageSize: Int): ApiResult<PaginatedEnvelope<InviteCode>> =
        ApiResult.Ok(PaginatedEnvelope(emptyList()))
    override suspend fun createInviteCode(body: AdminCreateInviteCodeRequest): ApiResult<InviteCode> =
        ApiResult.Ok(InviteCode("id", "code", body.maxRedemptions, 0, body.grantsFoundersBadge))
    override suspend fun revokeInviteCode(inviteCodeId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun grantTier(broadcasterId: String, body: AdminGrantTierRequest): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun grantFounderBadge(broadcasterId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun impersonate(subjectUserId: String, accessGrantId: String, justification: String): ApiResult<ImpersonationTokenDto> =
        ApiResult.Failure(ApiError(500, null, "not stubbed"))
    override suspend fun endImpersonation(accessGrantId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
}

private class NoopPlatformIamApi : PlatformIamApi {
    override suspend fun listRoles(): ApiResult<List<IamRole>> = ApiResult.Ok(emptyList())
    override suspend fun listPrincipals(): ApiResult<List<IamPrincipalSummary>> = ApiResult.Ok(emptyList())
    override suspend fun effectivePermissions(principalId: String, scopeChannelId: String?): ApiResult<List<String>> =
        ApiResult.Ok(emptyList())
    override suspend fun createPrincipal(body: CreatePrincipalBody): ApiResult<IamPrincipal> =
        ApiResult.Ok(IamPrincipal(id = "p", name = body.displayName))
    override suspend fun deactivatePrincipal(principalId: String, reason: String?): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun reactivatePrincipal(principalId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun assignRole(body: AssignRoleBody): ApiResult<IamRoleAssignment> =
        ApiResult.Ok(IamRoleAssignment(id = "a", principalId = body.principalId, roleId = body.roleId, roleName = "role", createdAt = "2026-01-01T00:00:00Z"))
    override suspend fun revokeAssignment(assignmentId: String, reason: String?): ApiResult<Unit> = ApiResult.Ok(Unit)
}

/** Records what the grant dialog actually sent, so the test asserts the created principal, not a click. */
private class RecordingPromoteIamApi : PlatformIamApi by NoopPlatformIamApi() {
    val createdPrincipals: MutableList<CreatePrincipalBody> = mutableListOf()

    override suspend fun createPrincipal(body: CreatePrincipalBody): ApiResult<IamPrincipal> {
        createdPrincipals += body
        return ApiResult.Ok(IamPrincipal(id = "p1", name = body.displayName))
    }
}

private class NoopPlatformAdminApi : PlatformAdminApi {
    override suspend fun listTenants(search: String?, status: String?, isLive: Boolean?, page: Int, pageSize: Int) =
        ApiResult.Ok(PaginatedEnvelope(emptyList<bot.nomnomz.dashboard.core.network.AdminTenant>()))
    override suspend fun getTenant(broadcasterId: String): ApiResult<bot.nomnomz.dashboard.core.network.AdminTenantDetail> =
        ApiResult.Failure(ApiError(404, null, "not stubbed"))
    override suspend fun suspendTenant(broadcasterId: String, body: SuspendTenantBody): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun reinstateTenant(broadcasterId: String, body: ReinstateTenantBody): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun beginAccess(broadcasterId: String, body: BeginTenantAccessBody): ApiResult<TenantAccessGrant> =
        ApiResult.Failure(ApiError(500, null, "not stubbed"))
    override suspend fun endAccess(accessGrantId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun searchAudit(
        principalId: String?,
        targetBroadcasterId: String?,
        permission: String?,
        outcome: String?,
        from: String?,
        to: String?,
        page: Int,
        pageSize: Int,
    ): ApiResult<PaginatedEnvelope<IamAuditEntry>> = ApiResult.Ok(PaginatedEnvelope(emptyList()))
}
