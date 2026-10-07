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

import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.CommunityStanding as WireStanding
import bot.nomnomz.dashboard.core.network.ResolvedAccess
import bot.nomnomz.dashboard.core.network.RolesApi
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ParticipantStanding
import kotlinx.coroutines.delay
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

    private val _unreachable: MutableStateFlow<Boolean> = MutableStateFlow(false)

    /**
     * True once [UNREACHABLE_AFTER_PROBES] consecutive probes failed transiently: the shell then swaps the plain
     * splash for the "cannot reach the server, retrying" screen with a Retry-now button. Any definitive answer resets it.
     */
    val unreachable: StateFlow<Boolean> = _unreachable.asStateFlow()

    private var failedProbes: Int = 0

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
                // A TRANSIENT blip (network drop, a momentary 5xx) is not proof the caller has no channel — only a
                // DEFINITIVE failure (401/403/404/…) is. Distinguishing them is S050's "effectiveMe transient
                // failure = retry state": a blip must show as RETRYING, never flash the same fail-closed viewer
                // UI a real "you have no channel" answer would. [ShellAccess.Retrying] keeps whatever the shell
                // was already showing on screen — the caller (App.kt) re-invokes [load] shortly after.
                is ApiResult.Failure ->
                    if (result.error.isTransient()) {
                        markTransientFailure()
                        return
                    } else if (result.error.isNoChannel()) {
                        // The one definitive answer that IS "you are a viewer": the account has no onboarded
                        // channel. Role-less participant at the lowest standing with no channel context — the
                        // participant rung surfaces the "no channel" state, never a management surface.
                        settle(
                            ShellAccess.Resolved(
                                channelId = "",
                                userId = null,
                                role = null,
                                standing = ParticipantStanding.Everyone,
                                capabilities = emptyList(),
                                heldActionKeys = emptySet(),
                            )
                        )
                        return
                    } else {
                        // Any other definitive failure (401/403/…) is a failed READ, not a viewer verdict: the
                        // shell shows an error with Retry. Still fail-closed — Failed unlocks no surface.
                        settle(ShellAccess.Failed(result.error.reason()))
                        return
                    }
                is ApiResult.Ok -> result.value
            }

        when (val result: ApiResult<ResolvedAccess> = rolesApi.effectiveMe(channel.id)) {
            is ApiResult.Failure ->
                if (result.error.isTransient()) {
                    // Same distinction as above, once the channel itself is known: a blip fetching the
                    // caller's OWN effective role must not masquerade as "you are just a viewer here".
                    markTransientFailure()
                } else {
                    // A definitive failed resolve of the caller's own access is an error the user must see and
                    // can retry — never silently downgraded to a viewer. Failed unlocks no surface (fail-closed).
                    settle(ShellAccess.Failed(result.error.reason()))
                }
            is ApiResult.Ok ->
                settle(
                    ShellAccess.Resolved(
                        channelId = channel.id,
                        userId = result.value.userId,
                        role = result.value.role.toShellRole(),
                        standing = result.value.standing.toShellStanding(),
                        capabilities = result.value.permitCapabilities,
                        heldActionKeys = result.value.heldActionKeys.toSet(),
                    )
                )
        }
    }

    // A definitive answer (resolved or failed) ends the retry streak.
    private fun settle(next: ShellAccess) {
        failedProbes = 0
        _unreachable.value = false
        _state.value = next
    }

    private fun markTransientFailure() {
        failedProbes += 1
        _unreachable.value = failedProbes >= UNREACHABLE_AFTER_PROBES
        _state.value = ShellAccess.Retrying
    }

    /**
     * Re-probes while the state stays [ShellAccess.Retrying], waiting [firstDelayMs] and doubling up to
     * [maxDelayMs]. It loops rather than re-probing once: a repeat blip sets the same Retrying value, which a
     * StateFlow does not emit again, so a one-shot retry keyed on the state left the shell on its splash forever
     * when a deploy kept the backend away for more than one probe.
     */
    suspend fun retryWhileTransient(firstDelayMs: Long = RETRY_FIRST_DELAY_MS, maxDelayMs: Long = RETRY_MAX_DELAY_MS) {
        var wait: Long = firstDelayMs
        while (_state.value is ShellAccess.Retrying) {
            delay(wait)
            load()
            wait = (wait * 2).coerceAtMost(maxDelayMs)
        }
    }
}

// Short enough that a momentary blip self-heals quickly; the cap keeps a backend that is really down from being
// hammered while still picking it up within half a minute of its return.
private const val RETRY_FIRST_DELAY_MS: Long = 2_000L
private const val RETRY_MAX_DELAY_MS: Long = 30_000L
private const val UNREACHABLE_AFTER_PROBES: Int = 2

private const val NO_CHANNEL_CODE: String = "NO_CHANNEL"

// "No onboarded channel" is the backend's genuine viewer answer; every other 4xx is a failed read.
private fun ApiError.isNoChannel(): Boolean = status == 404 && code == NO_CHANNEL_CODE

// A short technical reason for the error screen: the backend's error code, else the HTTP status.
private fun ApiError.reason(): String = code ?: "HTTP $status"

/**
 * A network/backend blip (no response at all, or the backend's own 5xx) versus a definitive answer
 * (401/403/404/…) the backend actually computed. Only the latter may be treated as "the caller really has no
 * access" — a blip must retry, never masquerade as a real answer (S050).
 */
private fun ApiError.isTransient(): Boolean = status == 0 || status >= 500

/** The shell's resolved-access state — Loading under the boot probe, then the caller's effective access. */
sealed interface ShellAccess {
    data object Loading : ShellAccess

    /**
     * A transient failure resolving the channel or the caller's effective access (a network blip or a momentary
     * backend 5xx) — distinct from both [Loading] (first boot, nothing resolved yet) and a definitive
     * [Resolved] fail-closed viewer (a real "you have no access" answer). The caller re-probes shortly after
     * landing here; the shell renders a neutral retry surface rather than ever showing the fail-closed viewer
     * UI for what might just be a blip.
     */
    data object Retrying : ShellAccess

    /**
     * A DEFINITIVE failure (401/403/404/…) reading the caller's own access. Not a viewer verdict: the shell shows
     * an error with a primary Retry and a secondary Sign out, and renders no management or participant surface
     * from it (fail-closed). [reason] is the backend error code, else the HTTP status.
     */
    data class Failed(val reason: String) : ShellAccess

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
