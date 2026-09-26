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

import bot.nomnomz.dashboard.core.connection.ImpersonationInfo
import bot.nomnomz.dashboard.core.connection.SessionStore
import bot.nomnomz.dashboard.core.connection.toSessionUser
import bot.nomnomz.dashboard.core.feedback.Feedback
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.AuthApi
import bot.nomnomz.dashboard.core.network.CurrentUser
import bot.nomnomz.dashboard.core.network.ImpersonationTokenDto
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.datetime.Instant
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.shell_impersonation_ended_expired
import nomnomzbot.composeapp.generated.resources.shell_impersonation_identity_failed
import nomnomzbot.composeapp.generated.resources.shell_impersonation_revoke_failed

/**
 * Owns the whole act-as lifecycle so every entry point (the admin console, the banner's Exit, a rejected act-as
 * token, logout) runs the same steps in the same order. While acting, everything the shell shows is the target's:
 * identity, channel list and selection, role, accent and live hubs. The only operator trace is the Exit control.
 *
 * Begin: swap the session onto the target (SessionStore clears the channel and bumps the identity generation),
 * then re-resolve identity → roster → role → hubs.
 * Exit: restore the operator session FIRST, so the revoke call carries the operator token (the act-as token can
 * never revoke its own grant), then revoke and surface a failed revoke, then re-resolve back to the operator.
 *
 * Runs on an app-level [scope]: an Exit started from a composable that the re-resolve unmounts still finishes.
 */
class ActAsCoordinator(
    private val sessionStore: SessionStore,
    private val authApi: AuthApi,
    private val revokeSession: suspend (accessGrantId: String) -> ApiResult<Unit>,
    private val reloadRoster: suspend () -> Unit,
    private val resolveAccess: suspend () -> Unit,
    private val reconnectHubs: suspend () -> Unit,
    private val applyAccent: (String?) -> Unit,
    private val clearReauthPrompt: () -> Unit,
    private val feedback: Feedback,
    private val scope: CoroutineScope,
) {
    private val lifecycle: Mutex = Mutex()

    /**
     * Act as the user [token] was minted for. Returns false (after undoing the swap and revoking the grant) when
     * the target identity cannot be read — a session holding the target token under the operator's identity must
     * never be left behind.
     */
    suspend fun begin(token: ImpersonationTokenDto, fallbackDisplayName: String): Boolean =
        lifecycle.withLock {
            sessionStore.beginImpersonation(
                targetAccessToken = token.accessToken,
                targetDisplayName = token.user.displayName.ifBlank { fallbackDisplayName },
                expiresAt = Instant.parse(token.expiresAt),
                accessGrantId = token.sessionId,
            )
            // The operator's own Twitch re-auth prompt must not follow them into someone else's session.
            clearReauthPrompt()
            if (!reResolve()) {
                feedback.error(Res.string.shell_impersonation_identity_failed)
                endLocked()
                return@withLock false
            }
            true
        }

    /** Leave act-as and return to the operator. A no-op when not acting. */
    suspend fun exit() {
        lifecycle.withLock { endLocked() }
    }

    /** [exit] on the app scope, for callers whose own scope may be torn down by the re-resolve. */
    fun exitInBackground(): Job = scope.launch { exit() }

    /**
     * The act-as token was rejected (expired, or revoked server-side): end the session through the same exit
     * path and tell the operator why. Never falls back to the operator's refresh token under the target's name.
     */
    fun onActAsTokenRejected(): Job =
        scope.launch {
            val wasActing: Boolean = sessionStore.isActingAs
            exit()
            if (wasActing) feedback.error(Res.string.shell_impersonation_ended_expired)
        }

    private suspend fun endLocked() {
        val ended: ImpersonationInfo = sessionStore.endImpersonation() ?: return
        when (val revoked: ApiResult<Unit> = revokeSession(ended.accessGrantId)) {
            is ApiResult.Ok -> Unit
            is ApiResult.Failure -> feedback.error(Res.string.shell_impersonation_revoke_failed, revoked.error.message)
        }
        reResolve()
    }

    // Identity first (profile name, avatar, platform-admin flag, accent), then the roster (which picks the channel
    // for the now-current identity), then the role on that channel, then the live hubs on the new token.
    private suspend fun reResolve(): Boolean {
        val identityResolved: Boolean =
            when (val me: ApiResult<CurrentUser> = authApi.me()) {
                is ApiResult.Ok -> {
                    sessionStore.setUser(me.value.toSessionUser())
                    applyAccent(me.value.color)
                    true
                }
                is ApiResult.Failure -> false
            }
        reloadRoster()
        resolveAccess()
        reconnectHubs()
        return identityResolved
    }
}
