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
import bot.nomnomz.dashboard.core.network.ResolvedAccess
import bot.nomnomz.dashboard.core.network.RolesApi
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
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

    /** Resolve the active channel, then the caller's own effective access. Fails closed to a viewer. */
    suspend fun load() {
        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = ShellAccess.Resolved(role = null)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        _state.value =
            when (val result: ApiResult<ResolvedAccess> = rolesApi.effectiveMe(channel.id)) {
                is ApiResult.Failure -> ShellAccess.Resolved(role = null)
                is ApiResult.Ok -> ShellAccess.Resolved(role = result.value.role.toShellRole())
            }
    }
}

/** The shell's resolved-role state — Loading under the boot probe, then the caller's effective management role. */
sealed interface ShellAccess {
    data object Loading : ShellAccess

    /** The resolved Plane-B [role] the shell gates on; null = a viewer (no management role). */
    data class Resolved(val role: ManagementRole?) : ShellAccess
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
