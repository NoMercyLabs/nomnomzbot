// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.roles.state

import bot.nomnomz.dashboard.core.network.ActionPermission
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelMembership
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ManagementRole
import bot.nomnomz.dashboard.core.network.PermitGrant
import bot.nomnomz.dashboard.core.network.PermitGrantType
import bot.nomnomz.dashboard.core.network.ResolvedAccess
import bot.nomnomz.dashboard.core.network.RolesApi
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// Proves the Roles & Permits page state machine the screen renders: resolve the active channel, surface the real
// management membership + active permit grants + the permit-grantable action keys (no members and no permits as
// Empty, a failure of channel/members/permits as Error), and manage each — assign a management role, grant a
// capability, revoke a permit. Each write must hit the right backend route with the resolved channel, reload on
// success so the lists reflect the backend's truth, and surface a failure over the intact lists. The screen is a
// pure projection of this, so testing it proves the page acts on real IAM data and degrades cleanly.
class RolesControllerTest {

    @Test
    fun load_surfaces_members_permits_and_only_grantable_actions() = runTest {
        val controller =
            RolesController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeRolesApi(
                    membersResults =
                        listOf(
                            ApiResult.Ok(
                                listOf(
                                    membership(id = "m1", userId = "u1", name = "Stoney_Eagle", role = ManagementRole.Broadcaster),
                                    membership(id = "m2", userId = "u2", name = "Mod Two", role = ManagementRole.Moderator),
                                )
                            )
                        ),
                    permitsResult =
                        ApiResult.Ok(
                            listOf(capabilityGrant(id = "p1", userId = "u2", actionKey = "stream:title:set"))
                        ),
                    matrixResult =
                        ApiResult.Ok(
                            listOf(
                                action(key = "stream:title:set", grantable = true),
                                action(key = "roles:manage", grantable = false),
                            )
                        ),
                ),
            )

        controller.load()

        val state: RolesState = controller.state.value
        assertTrue(state is RolesState.Ready)
        val ready: RolesState.Ready = state as RolesState.Ready
        // Membership is surfaced with its typed role.
        assertEquals(listOf("u1", "u2"), ready.members.map { it.userId })
        assertEquals(ManagementRole.Broadcaster, ready.members.first().managementRole)
        // The active permit grant is surfaced and reads as a capability grant on its action key.
        assertEquals(1, ready.permits.size)
        assertEquals(PermitGrantType.Capability, ready.permits.first().type)
        assertEquals("stream:title:set", ready.permits.first().capabilityActionKey)
        // Only the permit-grantable action key is offered (the default-deny one is filtered out).
        assertEquals(listOf("stream:title:set"), ready.grantableActions.map { it.actionKey })
        assertNull(ready.actionError)
    }

    @Test
    fun load_errors_when_no_channel_resolves() = runTest {
        val controller =
            RolesController(
                FakeChannelsApi(ApiResult.Failure(ApiError(404, "NO_CHANNEL", "none onboarded"))),
                FakeRolesApi(membersResults = listOf(ApiResult.Ok(emptyList()))),
            )

        controller.load()

        val state: RolesState = controller.state.value
        assertTrue(state is RolesState.Error)
        assertEquals("none onboarded", (state as RolesState.Error).detail)
    }

    @Test
    fun load_errors_when_the_members_call_fails() = runTest {
        val controller =
            RolesController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeRolesApi(membersResults = listOf(ApiResult.Failure(ApiError(500, "ERR", "boom")))),
            )

        controller.load()

        val state: RolesState = controller.state.value
        assertTrue(state is RolesState.Error)
        assertEquals("boom", (state as RolesState.Error).detail)
    }

    @Test
    fun load_errors_when_the_permits_call_fails() = runTest {
        val controller =
            RolesController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeRolesApi(
                    membersResults = listOf(ApiResult.Ok(listOf(membership(id = "m1", userId = "u1", name = "U", role = ManagementRole.Moderator)))),
                    permitsResult = ApiResult.Failure(ApiError(503, "ERR", "permits down")),
                ),
            )

        controller.load()

        val state: RolesState = controller.state.value
        assertTrue(state is RolesState.Error)
        assertEquals("permits down", (state as RolesState.Error).detail)
    }

    @Test
    fun load_is_empty_when_there_are_no_members_and_no_permits() = runTest {
        val controller =
            RolesController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeRolesApi(
                    membersResults = listOf(ApiResult.Ok(emptyList())),
                    permitsResult = ApiResult.Ok(emptyList()),
                ),
            )

        controller.load()

        assertTrue(controller.state.value is RolesState.Empty)
    }

    @Test
    fun load_keeps_the_page_when_the_matrix_call_fails() = runTest {
        // The matrix is supporting context: its failure must NOT sink the page — membership + permits still show,
        // with an empty grant catalogue (the capability flow is simply unavailable).
        val controller =
            RolesController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeRolesApi(
                    membersResults = listOf(ApiResult.Ok(listOf(membership(id = "m1", userId = "u1", name = "U", role = ManagementRole.Editor)))),
                    permitsResult = ApiResult.Ok(emptyList()),
                    matrixResult = ApiResult.Failure(ApiError(500, "ERR", "matrix down")),
                ),
            )

        controller.load()

        val state: RolesState = controller.state.value
        assertTrue(state is RolesState.Ready)
        assertEquals(listOf("u1"), (state as RolesState.Ready).members.map { it.userId })
        assertTrue(state.grantableActions.isEmpty())
    }

    @Test
    fun assign_role_calls_the_roles_route_then_reloads_the_updated_member() = runTest {
        val member = membership(id = "m1", userId = "u1", name = "Viewer One", role = ManagementRole.Moderator)
        val rolesApi =
            FakeRolesApi(
                // The reload after the assign returns the member at the new role.
                membersResults =
                    listOf(
                        ApiResult.Ok(listOf(member)),
                        ApiResult.Ok(listOf(member.copy(role = ManagementRole.Editor.wire))),
                    )
            )
        val controller = RolesController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), rolesApi)

        controller.load()
        controller.assignRole("u1", ManagementRole.Editor)

        // The write hit the roles route with the resolved channel, the user, and the chosen role.
        assertEquals(listOf(Triple("ch1", "u1", ManagementRole.Editor)), rolesApi.assignCalls)
        // The list reloaded and the member's role now reflects the assignment.
        val state: RolesState = controller.state.value
        assertTrue(state is RolesState.Ready)
        assertEquals(ManagementRole.Editor, (state as RolesState.Ready).members.first().managementRole)
        assertNull(state.actionError)
    }

    @Test
    fun assign_role_surfaces_the_error_and_keeps_the_list_when_it_fails() = runTest {
        val member = membership(id = "m1", userId = "u1", name = "Viewer One", role = ManagementRole.Moderator)
        val rolesApi =
            FakeRolesApi(
                membersResults = listOf(ApiResult.Ok(listOf(member))),
                assignResult = ApiResult.Failure(ApiError(403, "FORBIDDEN", "No escalation.")),
            )
        val controller = RolesController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), rolesApi)

        controller.load()
        controller.assignRole("u1", ManagementRole.Broadcaster)

        assertEquals(listOf(Triple("ch1", "u1", ManagementRole.Broadcaster)), rolesApi.assignCalls)
        val state: RolesState = controller.state.value
        assertTrue(state is RolesState.Ready)
        // The list is intact (still a Moderator) and the failure is surfaced.
        assertEquals(ManagementRole.Moderator, (state as RolesState.Ready).members.first().managementRole)
        assertEquals("No escalation.", state.actionError)
        // Only the initial load fetched members; the failed write did not trigger a reload.
        assertEquals(1, rolesApi.membersCalls)
    }

    @Test
    fun remove_role_calls_the_delete_route_then_reloads_without_the_member() = runTest {
        val member = membership(id = "m1", userId = "u1", name = "Mod", role = ManagementRole.Moderator)
        val keeper = membership(id = "m2", userId = "u2", name = "Keeper", role = ManagementRole.Editor)
        val rolesApi =
            FakeRolesApi(
                membersResults =
                    listOf(
                        ApiResult.Ok(listOf(member, keeper)),
                        ApiResult.Ok(listOf(keeper)),
                    )
            )
        val controller = RolesController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), rolesApi)

        controller.load()
        controller.removeRole("u1")

        assertEquals(listOf("ch1" to "u1"), rolesApi.removeCalls)
        val state: RolesState = controller.state.value
        assertTrue(state is RolesState.Ready)
        // The removed member dropped off; the other stays.
        assertEquals(listOf("u2"), (state as RolesState.Ready).members.map { it.userId })
        assertNull(state.actionError)
    }

    @Test
    fun grant_capability_calls_the_capability_route_then_reloads_with_the_new_permit() = runTest {
        val member = membership(id = "m1", userId = "u1", name = "Editor", role = ManagementRole.Editor)
        val grant = capabilityGrant(id = "p1", userId = "u1", actionKey = "stream:title:set")
        val rolesApi =
            FakeRolesApi(
                membersResults = listOf(ApiResult.Ok(listOf(member))),
                // The first permits read is empty; after the grant the reload returns the new grant.
                permitsResults = listOf(ApiResult.Ok(emptyList()), ApiResult.Ok(listOf(grant))),
                matrixResult = ApiResult.Ok(listOf(action(key = "stream:title:set", grantable = true))),
            )
        val controller = RolesController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), rolesApi)

        controller.load()
        controller.grantCapability("u1", "stream:title:set", "Trusted editor")

        // The write hit the capability route with the resolved channel, the user, the key, and the reason.
        assertEquals(
            listOf(GrantCall("ch1", "u1", "stream:title:set", "Trusted editor")),
            rolesApi.grantCalls,
        )
        // The list reloaded and the new permit is now present, reading as a capability grant on its key.
        val state: RolesState = controller.state.value
        assertTrue(state is RolesState.Ready)
        val permits: List<PermitGrant> = (state as RolesState.Ready).permits
        assertEquals(1, permits.size)
        assertEquals(PermitGrantType.Capability, permits.first().type)
        assertEquals("stream:title:set", permits.first().capabilityActionKey)
        assertNull(state.actionError)
    }

    @Test
    fun grant_capability_surfaces_the_error_and_keeps_the_lists_when_it_fails() = runTest {
        val member = membership(id = "m1", userId = "u1", name = "Editor", role = ManagementRole.Editor)
        val rolesApi =
            FakeRolesApi(
                membersResults = listOf(ApiResult.Ok(listOf(member))),
                permitsResults = listOf(ApiResult.Ok(emptyList())),
                matrixResult = ApiResult.Ok(listOf(action(key = "ban:user", grantable = true))),
                grantResult = ApiResult.Failure(ApiError(403, "FORBIDDEN", "Above your level.")),
            )
        val controller = RolesController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), rolesApi)

        controller.load()
        controller.grantCapability("u1", "ban:user", null)

        assertEquals(listOf(GrantCall("ch1", "u1", "ban:user", null)), rolesApi.grantCalls)
        val state: RolesState = controller.state.value
        assertTrue(state is RolesState.Ready)
        // The lists are intact (no permit was added) and the failure is surfaced.
        assertTrue((state as RolesState.Ready).permits.isEmpty())
        assertEquals("Above your level.", state.actionError)
        assertEquals(1, rolesApi.membersCalls)
    }

    @Test
    fun revoke_permit_calls_the_revoke_route_then_reloads_without_the_grant() = runTest {
        val member = membership(id = "m1", userId = "u1", name = "Editor", role = ManagementRole.Editor)
        val grant = capabilityGrant(id = "p1", userId = "u1", actionKey = "stream:title:set")
        val rolesApi =
            FakeRolesApi(
                membersResults = listOf(ApiResult.Ok(listOf(member))),
                permitsResults = listOf(ApiResult.Ok(listOf(grant)), ApiResult.Ok(emptyList())),
            )
        val controller = RolesController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), rolesApi)

        controller.load()
        controller.revokePermit("u1", "stream:title:set")

        // The write hit the revoke route with the resolved channel, the user, and the selector.
        assertEquals(
            listOf(Triple<String, String, String?>("ch1", "u1", "stream:title:set")),
            rolesApi.revokeCalls,
        )
        // The list reloaded and the grant is gone.
        val state: RolesState = controller.state.value
        assertTrue(state is RolesState.Ready)
        assertTrue((state as RolesState.Ready).permits.isEmpty())
        assertNull(state.actionError)
    }

    @Test
    fun revoke_permit_surfaces_the_error_and_keeps_the_grant_when_it_fails() = runTest {
        val member = membership(id = "m1", userId = "u1", name = "Editor", role = ManagementRole.Editor)
        val grant = capabilityGrant(id = "p1", userId = "u1", actionKey = "stream:title:set")
        val rolesApi =
            FakeRolesApi(
                membersResults = listOf(ApiResult.Ok(listOf(member))),
                permitsResults = listOf(ApiResult.Ok(listOf(grant))),
                revokeResult = ApiResult.Failure(ApiError(403, "FORBIDDEN", "Not allowed.")),
            )
        val controller = RolesController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), rolesApi)

        controller.load()
        controller.revokePermit("u1", "stream:title:set")

        assertEquals(
            listOf(Triple<String, String, String?>("ch1", "u1", "stream:title:set")),
            rolesApi.revokeCalls,
        )
        val state: RolesState = controller.state.value
        assertTrue(state is RolesState.Ready)
        // The grant is still present and the failure is surfaced.
        assertEquals(listOf("p1"), (state as RolesState.Ready).permits.map { it.id })
        assertEquals("Not allowed.", state.actionError)
        assertEquals(1, rolesApi.membersCalls)
    }
}

// ── Builders ────────────────────────────────────────────────────────────────

private fun membership(
    id: String,
    userId: String,
    name: String,
    role: ManagementRole,
): ChannelMembership =
    ChannelMembership(
        id = id,
        userId = userId,
        username = name,
        role = role.wire,
        levelValue = role.level,
        source = 2,
    )

private fun capabilityGrant(id: String, userId: String, actionKey: String): PermitGrant =
    PermitGrant(
        id = id,
        userId = userId,
        username = "U-$userId",
        grantType = PermitGrantType.Capability.wire,
        capabilityActionKey = actionKey,
        grantedByUserId = "owner",
    )

private fun action(key: String, grantable: Boolean): ActionPermission =
    ActionPermission(actionDefinitionId = "def-$key", actionKey = key, isGrantableViaPermit = grantable)

// ── Fakes ───────────────────────────────────────────────────────────────────

private class FakeChannelsApi(private val result: ApiResult<ChannelSummary>) : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = result

    override suspend fun list(): ApiResult<List<ChannelSummary>> = ApiResult.Ok(emptyList())

    override suspend fun join(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun leave(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun reset(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun deleteChannel(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
}

/** A captured capability-grant call (channel, user, action key, reason) — a Triple can't carry four fields. */
private data class GrantCall(
    val channelId: String,
    val userId: String,
    val actionKey: String,
    val reason: String?,
)

private class FakeRolesApi(
    private val membersResults: List<ApiResult<List<ChannelMembership>>>,
    private val permitsResults: List<ApiResult<List<PermitGrant>>> = listOf(ApiResult.Ok(emptyList())),
    private val matrixResult: ApiResult<List<ActionPermission>> = ApiResult.Ok(emptyList()),
    private val assignResult: ApiResult<Unit> = ApiResult.Ok(Unit),
    private val removeResult: ApiResult<Unit> = ApiResult.Ok(Unit),
    private val grantResult: ApiResult<Unit> = ApiResult.Ok(Unit),
    private val revokeResult: ApiResult<Unit> = ApiResult.Ok(Unit),
) : RolesApi {
    // Single-result convenience overloads via named args; the sequences let a reload return a changed page.
    constructor(
        membersResults: List<ApiResult<List<ChannelMembership>>>,
        permitsResult: ApiResult<List<PermitGrant>>,
        matrixResult: ApiResult<List<ActionPermission>> = ApiResult.Ok(emptyList()),
    ) : this(membersResults = membersResults, permitsResults = listOf(permitsResult), matrixResult = matrixResult)

    var membersCalls: Int = 0
        private set

    private var permitsCalls: Int = 0

    val assignCalls: MutableList<Triple<String, String, ManagementRole>> = mutableListOf()
    val removeCalls: MutableList<Pair<String, String>> = mutableListOf()
    val grantCalls: MutableList<GrantCall> = mutableListOf()
    val revokeCalls: MutableList<Triple<String, String, String?>> = mutableListOf()

    override suspend fun effectiveMe(channelId: String): ApiResult<ResolvedAccess> =
        ApiResult.Ok(
            ResolvedAccess(userId = "caller", broadcasterId = channelId, managementRole = null)
        )

    override suspend fun members(channelId: String): ApiResult<List<ChannelMembership>> {
        val index: Int = minOf(membersCalls, membersResults.lastIndex)
        membersCalls += 1
        return membersResults[index]
    }

    override suspend fun permits(channelId: String): ApiResult<List<PermitGrant>> {
        val index: Int = minOf(permitsCalls, permitsResults.lastIndex)
        permitsCalls += 1
        return permitsResults[index]
    }

    override suspend fun actionMatrix(channelId: String): ApiResult<List<ActionPermission>> = matrixResult

    override suspend fun assignRole(
        channelId: String,
        userId: String,
        role: ManagementRole,
    ): ApiResult<Unit> {
        assignCalls.add(Triple(channelId, userId, role))
        return assignResult
    }

    override suspend fun removeRole(channelId: String, userId: String): ApiResult<Unit> {
        removeCalls.add(channelId to userId)
        return removeResult
    }

    override suspend fun grantCapability(
        channelId: String,
        userId: String,
        actionKey: String,
        reason: String?,
    ): ApiResult<Unit> {
        grantCalls.add(GrantCall(channelId, userId, actionKey, reason))
        return grantResult
    }

    override suspend fun revokePermit(
        channelId: String,
        userId: String,
        actionKeyOrRole: String?,
    ): ApiResult<Unit> {
        revokeCalls.add(Triple(channelId, userId, actionKeyOrRole))
        return revokeResult
    }
}
