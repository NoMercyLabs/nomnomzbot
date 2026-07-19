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
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelMembership
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ManagementRole
import bot.nomnomz.dashboard.core.network.PermitGrant
import bot.nomnomz.dashboard.core.network.RolesApi
import bot.nomnomz.dashboard.core.network.UserSearchResult
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The Roles & Permits page's state-holder (the bot's IAM management, roles-permissions §5). Resolves the active
// channel, then loads its real management membership, its active per-user permit grants, and the per-action
// permission matrix from the backend (no fabricated entries) — the matrix is the closed catalogue a capability
// grant may target. It also drives the page's writes — assign a management role, grant a user a single
// capability, revoke either — each of which re-loads on success so the screen always reflects the backend's
// truth (which re-checks no-escalation on every write). The screen renders [state]; a retry / reconnect calls
// [load] again.
class RolesController(
    private val channelsApi: ChannelsApi,
    private val rolesApi: RolesApi,
) {
    private val _state: MutableStateFlow<RolesState> = MutableStateFlow(RolesState.Loading)

    /** The page render state: loading / ready (members + permits + grantable actions) / empty / error. */
    val state: StateFlow<RolesState> = _state.asStateFlow()

    // The channel the writes target — resolved by [load] and reused by every mutation so a write never has to
    // re-resolve the channel. Null until the first successful resolve.
    private var channelId: String? = null

    /** Resolve the active channel, then load its membership, permit grants, and grantable action keys. */
    suspend fun load() {
        // Only show the full-page loading state on first load; a refetch after a mutation keeps
        // the current content on screen (no flash) and swaps it when the new data arrives.
        if (_state.value !is RolesState.Ready) _state.value = RolesState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = RolesState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }
        channelId = channel.id

        val members: List<ChannelMembership> =
            when (val result: ApiResult<List<ChannelMembership>> = rolesApi.members(channel.id)) {
                is ApiResult.Failure -> {
                    _state.value = RolesState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        val permits: List<PermitGrant> =
            when (val result: ApiResult<List<PermitGrant>> = rolesApi.permits(channel.id)) {
                is ApiResult.Failure -> {
                    _state.value = RolesState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        // Only the permit-grantable actions are offered in the capability picker (the backend default-denies the
        // rest). The matrix is supporting context for the page, so a failure to load it doesn't sink the page —
        // the page still renders membership + permits with an empty grant catalogue and the capability flow off.
        val allActions: List<ActionPermission> =
            when (val result: ApiResult<List<ActionPermission>> = rolesApi.actionMatrix(channel.id)) {
                is ApiResult.Failure -> emptyList()
                is ApiResult.Ok -> result.value
            }

        _state.value =
            if (members.isEmpty() && permits.isEmpty()) {
                RolesState.Empty
            } else {
                RolesState.Ready(
                    members = members,
                    permits = permits,
                    grantableActions = allActions.filter { it.isGrantableViaPermit },
                    allActions = allActions,
                )
            }
    }

    /**
     * Search the platform's users for the viewer picker — the piece that lets the assign-role and grant-permit
     * flows reach a viewer who is NOT already a member. Best-effort: a failed search yields an empty list so the
     * picker shows "no matches" rather than sinking the page. The write that follows re-checks authorization.
     */
    suspend fun searchViewers(query: String): List<UserSearchResult> =
        when (val result: ApiResult<List<UserSearchResult>> = rolesApi.searchViewers(query)) {
            is ApiResult.Ok -> result.value
            is ApiResult.Failure -> emptyList()
        }

    /** Assign [userId] the management [role] (permanent membership), then reload so the member list reflects it. */
    suspend fun assignRole(userId: String, role: ManagementRole) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(rolesApi.assignRole(channel, userId, role))
    }

    /**
     * Grant [userId] a whole management [role] via a permit ([expiresAt] ISO-8601 or null; optional [reason]), then
     * reload so the permit list shows it. Distinct from [assignRole] — this is a delegated, optionally expiring lift.
     */
    suspend fun grantRole(userId: String, role: ManagementRole, expiresAt: String?, reason: String?) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(rolesApi.grantRole(channel, userId, role, expiresAt, reason))
    }

    /** Remove [userId]'s management role, then reload so they drop off the membership list. The screen confirms. */
    suspend fun removeRole(userId: String) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(rolesApi.removeRole(channel, userId))
    }

    /**
     * Grant [userId] the capability [actionKey] ([expiresAt] ISO-8601 or null; optional [reason]), then reload so
     * the permit list shows it.
     */
    suspend fun grantCapability(userId: String, actionKey: String, expiresAt: String?, reason: String?) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(rolesApi.grantCapability(channel, userId, actionKey, expiresAt, reason))
    }

    /**
     * Revoke a permit grant from [userId]: [actionKeyOrRole] selects which grant (an action key or a role token);
     * null revokes all of the user's active grants. Reloads on success. The screen confirms this first — revoking
     * elevated access is consequential.
     */
    suspend fun revokePermit(userId: String, actionKeyOrRole: String?) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(rolesApi.revokePermit(channel, userId, actionKeyOrRole))
    }

    /** Override the effective minimum level for [actionKey] to [level], clamped to the action's floor. */
    suspend fun setOverride(actionKey: String, level: Int) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(rolesApi.setOverride(channel, actionKey, level))
    }

    /** Reset [actionKey]'s override, restoring its built-in default floor. */
    suspend fun resetOverride(actionKey: String) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(rolesApi.resetOverride(channel, actionKey))
    }

    // A write either reloads (success) or surfaces its error over the current Ready state without losing it
    // (failure) — so a failed assign/grant/revoke leaves the page intact with a visible reason.
    private suspend fun afterWrite(result: ApiResult<Unit>) {
        when (result) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    private fun failWrite(detail: String) {
        val current: RolesState = _state.value
        _state.value =
            if (current is RolesState.Ready) current.copy(actionError = detail)
            else RolesState.Error(detail)
    }

    private companion object {
        const val NoChannelError: String = "No active channel — reconnect and try again."
    }
}

/** The Roles & Permits page render state. */
sealed interface RolesState {
    data object Loading : RolesState

    /**
     * The channel's IAM is listed: its management [members], its active per-user [permits], the
     * [grantableActions] a capability grant may target (permit-grantable action keys), and the full [allActions]
     * matrix used to display and manage per-action level overrides. [actionError] is non-null only when the last
     * assign/grant/revoke/override failed — surfaces as a banner while keeping the lists rendered.
     */
    data class Ready(
        val members: List<ChannelMembership>,
        val permits: List<PermitGrant>,
        val grantableActions: List<ActionPermission>,
        val allActions: List<ActionPermission> = emptyList(),
        val actionError: String? = null,
    ) : RolesState

    data object Empty : RolesState

    data class Error(val detail: String) : RolesState
}
