// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.settings.state

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.AuthApi
import bot.nomnomz.dashboard.core.network.DeviceLoginPoll
import bot.nomnomz.dashboard.core.network.ScopeRegrantStart
import bot.nomnomz.dashboard.core.network.TwitchDiagnosticsApi
import bot.nomnomz.dashboard.core.network.TwitchScopeDiagnostics
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The Settings page's "Permissions" section (S070): reads the real backend scope/feature matrix
// (TwitchScopeDiagnosticsController's GET /scopes) so the streamer sees, per declared scope, which feature(s)
// it gates and whether the connection already holds it — never a hardcoded list. Re-grant reuses the EXACT
// mechanism IntegrationsController's missing-scope banner uses: POST /regrant mints a Device Code Flow handle
// requesting (granted ∪ missing), the screen shows the user code + verification URL, and this controller polls
// the normal streamer device poll (AuthApi.pollDeviceLogin) until approval, denial, or expiry — no new OAuth
// flow, no manual back-fill.
class PermissionsController(
    private val diagnosticsApi: TwitchDiagnosticsApi,
    private val authApi: AuthApi,
) {
    private val _state: MutableStateFlow<PermissionsState> = MutableStateFlow(PermissionsState.Loading)

    /** The page render state: loading / ready (with the scope matrix) / error. */
    val state: StateFlow<PermissionsState> = _state.asStateFlow()

    /** Guards the START call itself, mirroring IntegrationsController.regrantScopes' single-flight guard. */
    private var regrantStarting: Boolean = false

    /** Load the real scope/feature matrix for the current channel's Twitch connection. */
    suspend fun load() {
        if (_state.value !is PermissionsState.Ready) _state.value = PermissionsState.Loading

        when (val result: ApiResult<TwitchScopeDiagnostics> = diagnosticsApi.scopeDiagnostics()) {
            is ApiResult.Failure -> _state.value = PermissionsState.Error(result.error.message)
            is ApiResult.Ok -> _state.value = PermissionsState.Ready(matrix = result.value)
        }
    }

    /**
     * Start the one-click additive scope re-grant (granted ∪ missing) via the shared diagnostics endpoint,
     * then poll the normal streamer device poll until the operator approves at twitch.tv/activate. On
     * approval the widened grant reconciles server-side, so the matrix is re-loaded (the granted flags flip).
     */
    suspend fun regrant() {
        val ready: PermissionsState.Ready = _state.value as? PermissionsState.Ready ?: return
        if (ready.regrant != null || regrantStarting) return // single-flight.

        regrantStarting = true
        try {
            when (val start: ApiResult<ScopeRegrantStart> = diagnosticsApi.startRegrant()) {
                is ApiResult.Failure ->
                    _state.value = ready.copy(regrantError = start.error.message)
                is ApiResult.Ok -> {
                    _state.value =
                        ready.copy(
                            regrantError = null,
                            regrant = PermissionsRegrantState(
                                userCode = start.value.userCode,
                                verificationUri = start.value.verificationUri,
                            ),
                        )
                    pollRegrant(start.value)
                }
            }
        } finally {
            regrantStarting = false
        }
    }

    /** Dismiss the re-grant panel without waiting; the next [load] re-reads the real granted state. */
    fun cancelRegrant() {
        val ready: PermissionsState.Ready = _state.value as? PermissionsState.Ready ?: return
        _state.value = ready.copy(regrant = null)
    }

    private suspend fun pollRegrant(start: ScopeRegrantStart) {
        val intervalMs: Long = start.interval.coerceAtLeast(1).toLong() * 1000L
        val deadlineMs: Long = start.expiresIn.coerceAtLeast(1).toLong() * 1000L
        var elapsedMs: Long = 0

        while (elapsedMs < deadlineMs && (_state.value as? PermissionsState.Ready)?.regrant != null) {
            delay(intervalMs)
            elapsedMs += intervalMs

            when (val poll: ApiResult<DeviceLoginPoll> = authApi.pollDeviceLogin(deviceCode = start.deviceCode)) {
                is ApiResult.Failure -> Unit // tolerate transient failures until the code's deadline.
                is ApiResult.Ok ->
                    when (poll.value.status) {
                        DEVICE_AUTHORIZED -> {
                            cancelRegrant()
                            load()
                            return
                        }
                        DEVICE_EXPIRED, DEVICE_DENIED, DEVICE_ERROR -> {
                            cancelRegrant()
                            return
                        }
                        else -> Unit // pending / slow_down — keep polling.
                    }
            }
        }
        cancelRegrant()
    }

    private companion object {
        const val DEVICE_AUTHORIZED = "authorized"
        const val DEVICE_EXPIRED = "expired"
        const val DEVICE_DENIED = "denied"
        const val DEVICE_ERROR = "error"
    }
}

/** The Permissions section render state. */
sealed interface PermissionsState {
    data object Loading : PermissionsState

    /**
     * The loaded scope/feature matrix plus any in-flight re-grant. [regrant] is non-null while a device
     * re-grant is awaiting approval; [regrantError] surfaces a failed START (e.g. nothing missing).
     */
    data class Ready(
        val matrix: TwitchScopeDiagnostics,
        val regrant: PermissionsRegrantState? = null,
        val regrantError: String? = null,
    ) : PermissionsState

    data class Error(val detail: String) : PermissionsState
}

/** The in-flight device re-grant panel: the user code + verification URL the screen shows and opens. */
data class PermissionsRegrantState(val userCode: String, val verificationUri: String)
