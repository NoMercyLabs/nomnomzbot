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

import bot.nomnomz.dashboard.core.connection.ActAsEndNotice
import bot.nomnomz.dashboard.core.connection.ActAsEndReason
import bot.nomnomz.dashboard.core.connection.ActAsSessionStore
import bot.nomnomz.dashboard.core.connection.ConnectionProfile
import bot.nomnomz.dashboard.core.connection.ImpersonationInfo
import bot.nomnomz.dashboard.core.connection.PersistedActAs
import bot.nomnomz.dashboard.core.connection.SessionStore
import bot.nomnomz.dashboard.core.connection.SessionTokens
import bot.nomnomz.dashboard.core.connection.toSessionUser
import bot.nomnomz.dashboard.core.feedback.Feedback
import bot.nomnomz.dashboard.core.navigation.AppReloader
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.AuthApi
import bot.nomnomz.dashboard.core.network.CurrentUser
import bot.nomnomz.dashboard.core.network.ImpersonationTokenDto
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.datetime.Clock
import kotlinx.datetime.Instant
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.shell_impersonation_ended_expired
import nomnomzbot.composeapp.generated.resources.shell_impersonation_identity_failed
import nomnomzbot.composeapp.generated.resources.shell_impersonation_revoke_failed

/**
 * Owns the whole act-as lifecycle so every entry point (the admin console, the Exit control, a rejected act-as
 * token, logout, a reload while acting) runs the same steps in the same order.
 *
 * Act-as is a full session swap, never an in-place patch: begin and exit both end in [AppReloader.reload], which
 * drops every in-memory cache, controller, socket and back-stack entry. The boot after the reload then resolves
 * the session exactly like a returning sign-in — of the target while the act-as marker is set, of the operator
 * after it is cleared. So the only operator trace while acting is the Exit control.
 *
 * Begin: prove the target token via `/me`, persist the act-as marker (with the operator's current location, for
 * Exit), reload to the target's default landing page.
 * Boot ([resume]): a live marker boots into the target's session; an expired or rejected one is dropped with a
 * notice and the boot falls through to the operator's own session.
 * Exit: restore the operator's token (re-minted from the refresh cookie after a reload, since the act-as token can
 * never revoke its own grant), revoke the grant, clear the marker, reload to where the operator started.
 *
 * Runs on an app-level [scope]: an Exit started from a composable still finishes.
 */
class ActAsCoordinator(
    private val sessionStore: SessionStore,
    private val authApi: AuthApi,
    private val revokeSession: suspend (accessGrantId: String) -> ApiResult<Unit>,
    private val actAsStore: ActAsSessionStore,
    private val reloader: AppReloader,
    private val feedback: Feedback,
    private val scope: CoroutineScope,
    private val clock: Clock = Clock.System,
) {
    private val lifecycle: Mutex = Mutex()

    /**
     * Act as the user [token] was minted for. Returns false (after undoing the swap and revoking the grant) when
     * the target identity cannot be read — the app is never reloaded into a session that cannot load.
     */
    suspend fun begin(token: ImpersonationTokenDto, fallbackDisplayName: String): Boolean =
        lifecycle.withLock {
            val returnLocation: String = reloader.currentLocation()
            val displayName: String = token.user.displayName.ifBlank { fallbackDisplayName }
            sessionStore.beginImpersonation(
                targetAccessToken = token.accessToken,
                targetDisplayName = displayName,
                expiresAt = Instant.parse(token.expiresAt),
                accessGrantId = token.sessionId,
            )
            if (authApi.me() is ApiResult.Failure) {
                feedback.error(Res.string.shell_impersonation_identity_failed)
                sessionStore.endImpersonation()
                revokeOrReport(token.sessionId)?.let { notice -> feedback.error(Res.string.shell_impersonation_revoke_failed, notice.detail ?: "") }
                return@withLock false
            }
            actAsStore.write(
                PersistedActAs(
                    accessToken = token.accessToken,
                    expiresAt = token.expiresAt,
                    accessGrantId = token.sessionId,
                    displayName = displayName,
                    returnLocation = returnLocation,
                ),
            )
            reloader.reload(location = "")
            true
        }

    /**
     * Boot step: when an act-as marker is set, open the target's session on [profile] instead of the operator's.
     * Returns true when the target's session is live. An expired or rejected marker is dropped with a notice and
     * returns false, so the caller restores the operator's own session.
     */
    suspend fun resume(profile: ConnectionProfile, operatorTokens: SessionTokens?): Boolean =
        lifecycle.withLock {
            val persisted: PersistedActAs = actAsStore.read() ?: return@withLock false
            val expiresAt: Instant = Instant.parse(persisted.expiresAt)
            if (clock.now() >= expiresAt) {
                dropMarker(ActAsEndNotice(ActAsEndReason.Expired))
                return@withLock false
            }
            sessionStore.resumeImpersonation(
                profile = profile,
                operatorTokens = operatorTokens,
                targetAccessToken = persisted.accessToken,
                targetDisplayName = persisted.displayName,
                expiresAt = expiresAt,
                accessGrantId = persisted.accessGrantId,
            )
            when (val me: ApiResult<CurrentUser> = authApi.me()) {
                is ApiResult.Ok -> {
                    sessionStore.commitActAs(me.value.toSessionUser())
                    true
                }
                is ApiResult.Failure -> {
                    sessionStore.clearActiveSession()
                    dropMarker(ActAsEndNotice(ActAsEndReason.Expired))
                    false
                }
            }
        }

    /** Show the notice an act-as session left for this boot (it ended across a reload). Shown once. */
    fun surfacePendingNotice() {
        val notice: ActAsEndNotice = actAsStore.takeNotice() ?: return
        when (notice.reason) {
            ActAsEndReason.Expired -> feedback.error(Res.string.shell_impersonation_ended_expired)
            ActAsEndReason.RevokeFailed -> feedback.error(Res.string.shell_impersonation_revoke_failed, notice.detail ?: "")
        }
    }

    /** Leave act-as and reload the app as the operator. A no-op when not acting. */
    suspend fun exit() {
        lifecycle.withLock { endLocked(reason = null, reload = true) }
    }

    /**
     * Leave act-as WITHOUT a reload, for a caller that tears the session down itself right after (logout). The
     * grant is still revoked on the operator's token.
     */
    suspend fun exitWithoutReload() {
        lifecycle.withLock { endLocked(reason = null, reload = false) }
    }

    /** [exit] on the app scope, for callers whose own scope may be torn down. */
    fun exitInBackground(): Job = scope.launch { exit() }

    /**
     * The act-as token was rejected (expired, or revoked server-side): end the session through the same exit
     * path and tell the operator why. Never installs the operator's refreshed token under the target's name.
     */
    fun onActAsTokenRejected(): Job =
        scope.launch {
            lifecycle.withLock {
                if (sessionStore.isActingAs) endLocked(reason = ActAsEndReason.Expired, reload = true)
            }
        }

    private suspend fun endLocked(reason: ActAsEndReason?, reload: Boolean) {
        val persisted: PersistedActAs? = actAsStore.read()
        val ended: ImpersonationInfo = sessionStore.endImpersonation() ?: return
        actAsStore.clear()
        // After a reload the operator's token is not in memory: re-mint it from their refresh credential (the web
        // HttpOnly cookie; the desktop vault's refresh token) — the act-as token cannot revoke its own grant.
        if (sessionStore.accessToken() == null) {
            val operatorRefresh: String? = sessionStore.loadPersisted()?.tokens?.refreshToken
            sessionStore.refreshOrExpire { authApi.refresh(operatorRefresh) }
        }
        val revokeFailure: ActAsEndNotice? = revokeOrReport(ended.accessGrantId)
        if (!reload) {
            revokeFailure?.let { feedback.error(Res.string.shell_impersonation_revoke_failed, it.detail ?: "") }
            return
        }
        // The reload drops the in-memory feedback bus, so the reason travels with the reload. An expiry outranks a
        // failed revoke: a token the server already rejected has nothing left to revoke.
        val notice: ActAsEndNotice? = reason?.let { ActAsEndNotice(it) } ?: revokeFailure
        notice?.let(actAsStore::putNotice)
        reloader.reload(location = persisted?.returnLocation ?: "")
    }

    private suspend fun revokeOrReport(accessGrantId: String): ActAsEndNotice? =
        when (val revoked: ApiResult<Unit> = revokeSession(accessGrantId)) {
            is ApiResult.Ok -> null
            is ApiResult.Failure -> ActAsEndNotice(ActAsEndReason.RevokeFailed, revoked.error.message)
        }

    private fun dropMarker(notice: ActAsEndNotice) {
        actAsStore.clear()
        actAsStore.putNotice(notice)
    }
}
