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
import bot.nomnomz.dashboard.core.connection.ActAsTokenCustody
import bot.nomnomz.dashboard.core.connection.ConnectionProfile
import bot.nomnomz.dashboard.core.connection.SessionStore
import bot.nomnomz.dashboard.core.connection.SessionTokens
import bot.nomnomz.dashboard.core.connection.toSessionUser
import bot.nomnomz.dashboard.core.feedback.Feedback
import bot.nomnomz.dashboard.core.navigation.AppReloader
import bot.nomnomz.dashboard.core.network.ActAsSessionInfo
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.AuthApi
import bot.nomnomz.dashboard.core.network.AuthPayload
import bot.nomnomz.dashboard.core.network.CurrentUser
import bot.nomnomz.dashboard.core.network.ImpersonationTokenDto
import bot.nomnomz.dashboard.feature.connect.state.ActAsConnectHooks
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.datetime.Instant
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.shell_impersonation_ended_expired
import nomnomzbot.composeapp.generated.resources.shell_impersonation_exit_failed
import nomnomzbot.composeapp.generated.resources.shell_impersonation_identity_failed
import nomnomzbot.composeapp.generated.resources.shell_impersonation_revoke_failed

/**
 * Owns the whole act-as lifecycle so every entry point (the admin console, the Exit control, a rejected act-as
 * token, logout, a reload while acting) runs the same steps in the same order.
 *
 * Act-as is a full session swap, never an in-place patch: begin and exit both end in [AppReloader.reload], which
 * drops every in-memory cache, controller, socket and back-stack entry. WHO the next boot is comes from the server
 * alone: `/auth/refresh` answers with the impersonated user (and `impersonation`) while the support session is
 * open, and with the operator once it is over. The client keeps no act-as marker of its own:
 *   Web:     the act-as token rides the HttpOnly `nnz_act_as` cookie the impersonate call set — never JS storage.
 *   Desktop: the act-as token is held in process memory ([ActAsTokenCustody]) and sent in the refresh body.
 * The only non-secret state kept across the reload is where the operator started (for Exit) and a one-shot notice.
 *
 * Runs on an app-level [scope]: an Exit started from a composable still finishes.
 */
class ActAsCoordinator(
    private val sessionStore: SessionStore,
    private val authApi: AuthApi,
    // Ends a support session with the OPERATOR's token — only for a begin whose target could not be loaded.
    private val revokeSession: suspend (accessGrantId: String) -> ApiResult<Unit>,
    private val actAsStore: ActAsSessionStore,
    private val tokenCustody: ActAsTokenCustody,
    private val reloader: AppReloader,
    private val feedback: Feedback,
    private val scope: CoroutineScope,
) : ActAsConnectHooks {
    private val lifecycle: Mutex = Mutex()

    // Separate from [lifecycle]: a renewal runs inside the 401 retry of a request Exit itself makes.
    private val renewal: Mutex = Mutex()

    // Set once this app instance has handed the session back to the operator; it is about to reload, so nothing
    // else (a late 401, a second Exit) may act on the dying act-as state.
    private var handedOver: Boolean = false

    /**
     * Act as the user [token] was minted for. The impersonate call already gave the act-as token to its custody
     * (the HttpOnly cookie on web); this proves it via `/me`, keeps it on native, remembers where the operator is,
     * and reloads. Returns false (after undoing the swap and revoking the grant) when the target identity cannot be
     * read — the app is never reloaded into a session that cannot load.
     */
    suspend fun begin(token: ImpersonationTokenDto, fallbackDisplayName: String): Boolean =
        lifecycle.withLock {
            val returnLocation: String = reloader.currentLocation()
            sessionStore.beginImpersonation(
                targetAccessToken = token.accessToken,
                targetDisplayName = token.user.displayName.ifBlank { fallbackDisplayName },
                expiresAt = Instant.parse(token.expiresAt),
                accessGrantId = token.sessionId,
            )
            if (authApi.me() is ApiResult.Failure) {
                feedback.error(Res.string.shell_impersonation_identity_failed)
                sessionStore.endImpersonation()
                val revoked: ApiResult<Unit> = revokeSession(token.sessionId)
                if (revoked is ApiResult.Failure) {
                    feedback.error(Res.string.shell_impersonation_revoke_failed, revoked.error.message)
                }
                return@withLock false
            }
            tokenCustody.hold(token.accessToken)
            actAsStore.writeReturnLocation(returnLocation)
            reloader.reload(location = "")
            true
        }

    override fun heldActAsToken(): String? = tokenCustody.read()

    /**
     * Boot step: `/auth/refresh` answered with an open act-as session — open it on [profile] instead of the
     * operator's. True when the target's session is live; false (nothing held) when its identity cannot be read.
     */
    override suspend fun resume(profile: ConnectionProfile, operatorTokens: SessionTokens?, answer: AuthPayload): Boolean =
        lifecycle.withLock {
            val acting: ActAsSessionInfo = answer.impersonation ?: return@withLock false
            sessionStore.resumeImpersonation(
                profile = profile,
                operatorTokens = operatorTokens,
                targetAccessToken = answer.accessToken,
                targetDisplayName = answer.user?.displayName.orEmpty(),
                expiresAt = Instant.parse(acting.expiresAt),
                accessGrantId = acting.sessionId,
            )
            tokenCustody.hold(answer.accessToken)
            when (val me: ApiResult<CurrentUser> = authApi.me()) {
                is ApiResult.Ok -> {
                    sessionStore.commitActAs(me.value.toSessionUser())
                    true
                }
                is ApiResult.Failure -> {
                    sessionStore.clearActiveSession()
                    tokenCustody.clear()
                    false
                }
            }
        }

    /**
     * Boot step: the refresh answered with the operator. Any act-as state still around from before (this tab
     * started acting, or the desktop still held a token) means the session ended while the app was away — say so.
     */
    override fun onOperatorSession() {
        val wasActing: Boolean = actAsStore.readReturnLocation() != null || tokenCustody.read() != null
        tokenCustody.clear()
        actAsStore.clear()
        if (wasActing) actAsStore.putNotice(ActAsEndNotice(ActAsEndReason.Expired))
    }

    override fun onLoggedOutWhileActing() {
        handedOver = true
        tokenCustody.clear()
        actAsStore.clear()
        reloader.reload(location = "")
    }

    /** Show the notice an act-as session left for this boot (it ended across a reload). Shown once. */
    fun surfacePendingNotice() {
        val notice: ActAsEndNotice = actAsStore.takeNotice() ?: return
        when (notice.reason) {
            ActAsEndReason.Expired -> feedback.error(Res.string.shell_impersonation_ended_expired)
        }
    }

    /**
     * Leave act-as: `POST /auth/impersonation/exit` with the act-as token ends the support session server-side and
     * hands back the operator (web: the refresh cookie; native: its rotated refresh token, vaulted here), then the
     * app reloads where the operator started. A failure keeps the operator acting and says so — reloading would
     * only come back as the target, since the server still holds the session open.
     */
    suspend fun exit() {
        lifecycle.withLock {
            if (!sessionStore.isActingAs || handedOver) return@withLock
            val operatorRefresh: String? = sessionStore.loadPersisted()?.tokens?.refreshToken
            when (val exited: ApiResult<AuthPayload?> = authApi.exitImpersonation(operatorRefresh)) {
                is ApiResult.Ok -> {
                    exited.value?.let { vaultOperator(it, operatorRefresh) }
                    handOver(notice = null)
                }
                // A 401 already ran [renew], which handed the operator back if the session was over.
                is ApiResult.Failure ->
                    if (!handedOver) feedback.error(Res.string.shell_impersonation_exit_failed, exited.error.message)
            }
        }
    }

    /** [exit] on the app scope, for callers whose own scope may be torn down. */
    fun exitInBackground(): Job = scope.launch { exit() }

    /**
     * The act-as token was rejected or ran out (the 401 refresher, or the session's end time passing): ask the
     * server who this session is now. Still open → hold the re-minted act-as token and return true (the request is
     * retried as the target). Over → the server answered with the operator: reload as them, with a notice. Never
     * installs the operator's token under the target's name.
     */
    suspend fun renew(): Boolean =
        renewal.withLock {
            if (!sessionStore.isActingAs || handedOver) return@withLock false
            val operatorRefresh: String? = sessionStore.loadPersisted()?.tokens?.refreshToken
            when (val answer: ApiResult<AuthPayload> = authApi.refresh(operatorRefresh, tokenCustody.read())) {
                is ApiResult.Ok -> {
                    val acting: ActAsSessionInfo? = answer.value.impersonation
                    if (acting != null) {
                        tokenCustody.hold(answer.value.accessToken)
                        sessionStore.renewActAs(answer.value.accessToken, Instant.parse(acting.expiresAt))
                        return@withLock true
                    }
                    vaultOperator(answer.value, operatorRefresh)
                    handOver(ActAsEndNotice(ActAsEndReason.Expired))
                    false
                }
                is ApiResult.Failure -> {
                    // A blip proves nothing; a real rejection means neither session is left — the reload says so.
                    if (!isTransient(answer.error)) handOver(ActAsEndNotice(ActAsEndReason.Expired))
                    false
                }
            }
        }

    /** [renew] on the app scope — for the timer that fires when the support session's end time passes. */
    fun onActAsTokenRejected(): Job = scope.launch { renew() }

    // The operator's own session came back in a response: native vaults its rotated refresh token so the reload
    // restores as the operator (web keeps it in the HttpOnly cookie, and its vault is a no-op).
    private suspend fun vaultOperator(operator: AuthPayload, previousRefresh: String?) {
        sessionStore.vaultOperatorTokens(
            SessionTokens(
                accessToken = operator.accessToken,
                refreshToken = operator.refreshToken ?: previousRefresh,
            ),
        )
    }

    private fun handOver(notice: ActAsEndNotice?) {
        handedOver = true
        tokenCustody.clear()
        val returnLocation: String = actAsStore.readReturnLocation() ?: ""
        actAsStore.clear()
        notice?.let(actAsStore::putNotice)
        reloader.reload(location = returnLocation)
    }

    private fun isTransient(error: ApiError): Boolean = error.status == 0 || error.status >= 500
}
