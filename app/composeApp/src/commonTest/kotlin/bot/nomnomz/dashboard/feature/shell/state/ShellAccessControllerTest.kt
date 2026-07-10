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
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.CommunityStanding as WireStanding
import bot.nomnomz.dashboard.core.network.ManagementRole as WireRole
import bot.nomnomz.dashboard.core.network.PermitGrant
import bot.nomnomz.dashboard.core.network.ResolvedAccess
import bot.nomnomz.dashboard.core.network.RolesApi
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ParticipantStanding
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
        // so it routes to the participant rung instead of the management dashboard.
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
    fun a_role_less_subscriber_resolves_to_the_subscriber_participant_standing() = runTest {
        // A pure viewer with no management role still carries a Plane-A community standing — the participant rung
        // unlocks from it. A subscriber must resolve to ParticipantStanding.Subscriber (not Everyone), so the
        // surface can light up sub-only lanes; the management role stays null (they get the participant shell).
        val controller =
            ShellAccessController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeRolesApi(
                    access =
                        ApiResult.Ok(
                            resolvedAccess(
                                role = null,
                                level = 20,
                                standing = WireStanding.Subscriber,
                                capabilities = listOf("economy:transfer:write"),
                            )
                        )
                ),
            )

        controller.load()

        val resolved: ShellAccess.Resolved = controller.state.value as ShellAccess.Resolved
        assertEquals(null, resolved.role)
        assertEquals(ParticipantStanding.Subscriber, resolved.standing)
        // The caller's own GUID and permit capabilities thread through for the participant self-service.
        assertEquals("caller", resolved.userId)
        assertEquals(listOf("economy:transfer:write"), resolved.capabilities)
        assertEquals("ch1", resolved.channelId)
    }

    @Test
    fun a_plain_viewer_resolves_to_the_everyone_standing() = runTest {
        val controller =
            ShellAccessController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeRolesApi(
                    access = ApiResult.Ok(resolvedAccess(role = null, level = 0, standing = WireStanding.Everyone))
                ),
            )

        controller.load()

        val resolved: ShellAccess.Resolved = controller.state.value as ShellAccess.Resolved
        assertEquals(ParticipantStanding.Everyone, resolved.standing)
        // A plain viewer holds no transfer capability — the store's transfer affordance stays off.
        assertTrue(resolved.capabilities.isEmpty())
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
        val resolved: ShellAccess.Resolved = state as ShellAccess.Resolved
        assertEquals(null, resolved.role)
        // Fail closed to the LEAST-privileged participant: no role, lowest standing, no capabilities, no channel.
        assertEquals(ParticipantStanding.Everyone, resolved.standing)
        assertTrue(resolved.capabilities.isEmpty())
    }

    @Test
    fun a_failed_effective_me_call_fails_closed_to_a_viewer() = runTest {
        val controller =
            ShellAccessController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeRolesApi(access = ApiResult.Failure(ApiError(500, "ERR", "boom"))),
            )

        controller.load()

        val resolved: ShellAccess.Resolved = controller.state.value as ShellAccess.Resolved
        assertEquals(null, resolved.role)
        // Fail closed carries NO held keys — the shell must never surface a management page off a failed resolve.
        assertTrue(resolved.heldActionKeys.isEmpty())
    }

    @Test
    fun the_resolved_access_carries_the_backends_held_action_keys_as_a_set() = runTest {
        // heldActionKeys is what the shell gates page/action visibility on — it must thread through from
        // /effective/me into the resolved state (deduplicated into a Set) so a broadcaster-LOWERED page can surface
        // to a role-less caller. A VIP the broadcaster delegated Commands + quote-editing to carries exactly those
        // keys, with no management role.
        val controller =
            ShellAccessController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeRolesApi(
                    access =
                        ApiResult.Ok(
                            resolvedAccess(
                                role = null,
                                level = 40,
                                standing = WireStanding.Vip,
                                heldActionKeys = listOf("commands:read", "quotes:read", "quotes:write"),
                            )
                        )
                ),
            )

        controller.load()

        val resolved: ShellAccess.Resolved = controller.state.value as ShellAccess.Resolved
        assertEquals(null, resolved.role)
        assertEquals(setOf("commands:read", "quotes:read", "quotes:write"), resolved.heldActionKeys)
    }
}

// ── Builders ────────────────────────────────────────────────────────────────

private fun resolvedAccess(
    role: WireRole?,
    level: Int,
    standing: WireStanding = WireStanding.Everyone,
    capabilities: List<String> = emptyList(),
    heldActionKeys: List<String> = emptyList(),
): ResolvedAccess =
    ResolvedAccess(
        userId = "caller",
        broadcasterId = "ch1",
        effectiveLevel = level,
        communityStanding = standing.wire,
        communityLevel = standing.level,
        managementRole = role?.wire,
        managementLevel = role?.level ?: 0,
        permitCapabilities = capabilities,
        winningSource = if (role == null) "community" else "management",
        heldActionKeys = heldActionKeys,
    )

// ── Fakes ───────────────────────────────────────────────────────────────────

private class FakeChannelsApi(private val result: ApiResult<ChannelSummary>) : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = result

    override suspend fun list(): ApiResult<List<ChannelSummary>> = ApiResult.Ok(emptyList())

    override suspend fun join(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun leave(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun reset(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun deleteChannel(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun channelScopes(channelId: String) = error("stub")
    override suspend fun startChannelBotConnect(channelId: String) = error("stub")
    override suspend fun channelBotStatus(channelId: String) = error("stub")
    override suspend fun disconnectChannelBot(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun moderatedChannels(): ApiResult<List<ModeratedChannel>> = ApiResult.Ok(emptyList())
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

    override suspend fun setOverride(channelId: String, actionKey: String, level: Int): ApiResult<Unit> =
        ApiResult.Ok(Unit)

    override suspend fun resetOverride(channelId: String, actionKey: String): ApiResult<Unit> =
        ApiResult.Ok(Unit)
}
