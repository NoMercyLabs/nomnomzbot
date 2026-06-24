// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.shell.state

import bot.nomnomz.dashboard.core.network.ActionPermission
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelMembership
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ManagementRole as WireRole
import bot.nomnomz.dashboard.core.network.PermitGrant
import bot.nomnomz.dashboard.core.network.ResolvedAccess
import bot.nomnomz.dashboard.core.network.RolesApi
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// Proves the shell's role resolver — the seam that REPLACES the App.kt `role = Broadcaster` hardcode. It resolves
// the active channel, fetches the caller's own /effective/me, and surfaces the real Plane-B ManagementRole? the
// shell gates on. The consequence under test: the role the shell renders is the BACKEND's resolved role, not a
// constant — a viewer resolves to null (no management role → participation-only surface), a delegated manager to
// their actual rung, and a transient failure fails closed to a viewer rather than leaking the broadcaster surface.
class ShellAccessControllerTest {

    @Test
    fun resolves_the_broadcaster_role_from_effective_me() = runTest {
        val controller =
            ShellAccessController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeRolesApi(access = ApiResult.Ok(resolvedAccess(role = WireRole.Broadcaster, level = 40))),
            )

        controller.load()

        val state: ShellAccess = controller.state.value
        assertTrue(state is ShellAccess.Resolved)
        assertEquals(ManagementRole.Broadcaster, (state as ShellAccess.Resolved).role)
    }

    @Test
    fun resolves_an_editor_role_from_effective_me() = runTest {
        val controller =
            ShellAccessController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeRolesApi(access = ApiResult.Ok(resolvedAccess(role = WireRole.Editor, level = 30))),
            )

        controller.load()

        assertEquals(ManagementRole.Editor, (controller.state.value as ShellAccess.Resolved).role)
    }

    @Test
    fun resolves_a_moderator_role_from_effective_me() = runTest {
        val controller =
            ShellAccessController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeRolesApi(access = ApiResult.Ok(resolvedAccess(role = WireRole.Moderator, level = 10))),
            )

        controller.load()

        assertEquals(ManagementRole.Moderator, (controller.state.value as ShellAccess.Resolved).role)
    }

    @Test
    fun a_role_less_caller_resolves_to_a_viewer_with_a_null_management_role() = runTest {
        // /effective/me returns managementRole = null (a pure viewer). The shell must see null — not a default —
        // so it routes to the participation-only surface instead of the management dashboard.
        val controller =
            ShellAccessController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeRolesApi(access = ApiResult.Ok(resolvedAccess(role = null, level = 0))),
            )

        controller.load()

        val state: ShellAccess = controller.state.value
        assertTrue(state is ShellAccess.Resolved)
        assertEquals(null, (state as ShellAccess.Resolved).role)
    }

    @Test
    fun the_effective_me_call_targets_the_resolved_channel() = runTest {
        val rolesApi = FakeRolesApi(access = ApiResult.Ok(resolvedAccess(role = WireRole.Moderator, level = 10)))
        val controller = ShellAccessController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch42"))), rolesApi)

        controller.load()

        assertEquals(listOf("ch42"), rolesApi.effectiveMeCalls)
    }

    @Test
    fun a_failure_to_resolve_the_channel_fails_closed_to_a_viewer() = runTest {
        // No channel / transient backend error must NEVER fall through to the broadcaster surface. Fail closed:
        // the caller is treated as a viewer (no management role) until a resolve succeeds.
        val controller =
            ShellAccessController(
                FakeChannelsApi(ApiResult.Failure(ApiError(404, "NO_CHANNEL", "none"))),
                FakeRolesApi(access = ApiResult.Ok(resolvedAccess(role = WireRole.Broadcaster, level = 40))),
            )

        controller.load()

        val state: ShellAccess = controller.state.value
        assertTrue(state is ShellAccess.Resolved)
        assertEquals(null, (state as ShellAccess.Resolved).role)
    }

    @Test
    fun a_failed_effective_me_call_fails_closed_to_a_viewer() = runTest {
        val controller =
            ShellAccessController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeRolesApi(access = ApiResult.Failure(ApiError(500, "ERR", "boom"))),
            )

        controller.load()

        assertEquals(null, (controller.state.value as ShellAccess.Resolved).role)
    }
}

// ── Builders ────────────────────────────────────────────────────────────────

private fun resolvedAccess(role: WireRole?, level: Int): ResolvedAccess =
    ResolvedAccess(
        userId = "caller",
        broadcasterId = "ch1",
        effectiveLevel = level,
        managementRole = role?.wire,
        managementLevel = role?.level ?: 0,
        winningSource = if (level == 0) "community" else "management",
    )

// ── Fakes ───────────────────────────────────────────────────────────────────

private class FakeChannelsApi(private val result: ApiResult<ChannelSummary>) : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = result
}

private class FakeRolesApi(private val access: ApiResult<ResolvedAccess>) : RolesApi {
    val effectiveMeCalls: MutableList<String> = mutableListOf()

    override suspend fun effectiveMe(channelId: String): ApiResult<ResolvedAccess> {
        effectiveMeCalls.add(channelId)
        return access
    }

    // Unused by ShellAccessController.
    override suspend fun members(channelId: String): ApiResult<List<ChannelMembership>> =
        ApiResult.Ok(emptyList())

    override suspend fun permits(channelId: String): ApiResult<List<PermitGrant>> =
        ApiResult.Ok(emptyList())

    override suspend fun actionMatrix(channelId: String): ApiResult<List<ActionPermission>> =
        ApiResult.Ok(emptyList())

    override suspend fun assignRole(channelId: String, userId: String, role: WireRole): ApiResult<Unit> =
        ApiResult.Ok(Unit)

    override suspend fun removeRole(channelId: String, userId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun grantCapability(
        channelId: String,
        userId: String,
        actionKey: String,
        reason: String?,
    ): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun revokePermit(
        channelId: String,
        userId: String,
        actionKeyOrRole: String?,
    ): ApiResult<Unit> = ApiResult.Ok(Unit)
}
