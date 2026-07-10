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

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.CommunityStanding as WireStanding
import bot.nomnomz.dashboard.core.network.ResolvedAccess
import bot.nomnomz.dashboard.core.network.RolesApi
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ParticipantStanding
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The shell's role resolver — the seam that REPLACES the old App.kt `role = ManagementRole.Broadcaster` hardcode.
// On session establish it resolves the active channel, fetches the caller's own `/effective/me`, and surfaces the
// REAL Plane-B [ManagementRole]? the shell gates on (roles-permissions.md §3.2; frontend-ia.md §7). A null role is
// a viewer (no management role) → the shell shows the participation-only surface, never the management dashboard.
//
// Fail-closed: a missing channel or a transient backend error resolves to a VIEWER (null role), never the
// broadcaster surface — the dashboard must never over-expose management pages because a probe blipped. The
// backend re-checks every write regardless (the frontend gate is UX, not the security boundary).
class ShellAccessController(
    private val channelsApi: ChannelsApi,
    private val rolesApi: RolesApi,
) {
    private val _state: MutableStateFlow<ShellAccess> = MutableStateFlow(ShellAccess.Loading)

    /** The shell's role state: loading until the first resolve, then the caller's effective management role. */
    val state: StateFlow<ShellAccess> = _state.asStateFlow()

    /**
     * Resolve the active channel, then the caller's own effective access. Fails closed to a participant.
     *
     * Runs on session establish AND on every active-channel change (the App keys a LaunchedEffect on it) AND on a
     * live permission change pushed over SignalR. It does NOT blank to [ShellAccess.Loading] first: the shell holds
     * the PREVIOUS channel's resolved access until the new probe lands, and [bot.nomnomz.dashboard.feature.shell.ui.ShellScreen]
     * renders a neutral "switching" state whenever the resolved [ShellAccess.Resolved.channelId] doesn't match the
     * active channel — so a switch never flashes the old channel's (possibly higher) role, while a same-channel
     * re-resolve (a live grant/revoke) swaps in place with no splash flash and no channel-roster refetch.
     */
    suspend fun load() {
        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                // No channel resolves (none onboarded / transient blip): fail closed to a role-less participant at
                // the lowest standing with no channel context — the shell renders the participant rung, whose
                // screens surface the "no channel" error rather than ever flashing a management surface.
                is ApiResult.Failure -> {
                    _state.value =
                        ShellAccess.Resolved(
                            channelId = "",
                            userId = null,
                            role = null,
                            standing = ParticipantStanding.Everyone,
                            capabilities = emptyList(),
                            heldActionKeys = emptySet(),
                        )
                    return
                }
                is ApiResult.Ok -> result.value
            }

        _state.value =
            when (val result: ApiResult<ResolvedAccess> = rolesApi.effectiveMe(channel.id)) {
                // A failed resolve fails closed to a participant at the LOWEST standing (Everyone): the base
                // participation surface, never an over-granted management or sub-only view.
                is ApiResult.Failure ->
                    ShellAccess.Resolved(
                        channelId = channel.id,
                        userId = null,
                        role = null,
                        standing = ParticipantStanding.Everyone,
                        capabilities = emptyList(),
                        heldActionKeys = emptySet(),
                    )
                is ApiResult.Ok ->
                    ShellAccess.Resolved(
                        channelId = channel.id,
                        userId = result.value.userId,
                        role = result.value.role.toShellRole(),
                        standing = result.value.standing.toShellStanding(),
                        capabilities = result.value.permitCapabilities,
                        heldActionKeys = result.value.heldActionKeys.toSet(),
                    )
            }
    }
}

/** The shell's resolved-access state — Loading under the boot probe, then the caller's effective access. */
sealed interface ShellAccess {
    data object Loading : ShellAccess

    /**
     * The caller's resolved access on [channelId]. The shell gates the MANAGEMENT rung on [role] (null = a
     * participant with no Plane-B role) and the PARTICIPANT rung on [standing] (the Plane-A community rung the
     * participant surface unlocks from — always present, even for a role-less viewer). [userId] is the caller's
     * platform GUID the participant self-service addresses its own records by; [capabilities] are the per-user
     * permit action keys that light up capability-gated affordances (e.g. `economy:transfer:write`).
     *
     * [heldActionKeys] is the broader, UI-facing set the shell gates page/action VISIBILITY on: every action key
     * the caller actually CLEARS on this channel — folding in the broadcaster's per-action overrides, unlike
     * [role]/[capabilities] which don't. It is what lets a broadcaster-LOWERED page (e.g. `commands:read` dropped
     * to VIP) surface to a role-less caller, and what the Quotes page reads to gate `quotes:write` / `quotes:delete`.
     */
    data class Resolved(
        val channelId: String,
        val userId: String?,
        val role: ManagementRole?,
        val standing: ParticipantStanding,
        val capabilities: List<String>,
        val heldActionKeys: Set<String>,
    ) : ShellAccess
}

/**
 * Map the network [WireStanding] to the shell's participant standing by ladder [level] (shared by both), so the
 * rung the network layer calls `Subscriber` lands on the shell's `Subscriber`. An unknown level fails closed to
 * `Everyone` (the least-privileged) rather than over-unlocking.
 */
private fun WireStanding.toShellStanding(): ParticipantStanding =
    when (this.level) {
        WireStanding.Subscriber.level -> ParticipantStanding.Subscriber
        WireStanding.Vip.level -> ParticipantStanding.Vip
        WireStanding.Artist.level -> ParticipantStanding.Artist
        WireStanding.Moderator.level -> ParticipantStanding.Moderator
        else -> ParticipantStanding.Everyone
    }

/**
 * Map the network [bot.nomnomz.dashboard.core.network.ManagementRole]? to the shell's gate enum by ladder [level]
 * (10/20/30/40 — shared by both), so the rung the network layer calls `LeadModerator` lands on the shell's
 * `SuperMod`. Null (a viewer) stays null. An unknown level fails closed to null rather than over-granting.
 */
private fun bot.nomnomz.dashboard.core.network.ManagementRole?.toShellRole(): ManagementRole? =
    when (this?.level) {
        ManagementRole.Moderator.level -> ManagementRole.Moderator
        ManagementRole.SuperMod.level -> ManagementRole.SuperMod
        ManagementRole.Editor.level -> ManagementRole.Editor
        ManagementRole.Broadcaster.level -> ManagementRole.Broadcaster
        else -> null
    }
