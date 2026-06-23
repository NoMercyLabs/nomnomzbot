// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.setup.state

import bot.nomnomz.dashboard.core.connection.ConnectLauncher
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.BotOAuthUrl
import bot.nomnomz.dashboard.core.network.SetupStep
import bot.nomnomz.dashboard.core.network.SetupWizard
import bot.nomnomz.dashboard.core.network.SystemApi
import bot.nomnomz.dashboard.core.network.SystemStatus
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The first-run setup wizard's state-holder (frontend.md §4 — a plain holder, not a ViewModel). It owns
// the self-describing wizard (loaded from the backend), the per-step credential inputs, and the busy /
// error state, and runs the REAL onboarding against the chosen backend through the typed [SystemApi]:
//
//   load() → GET …/setup/wizard + GET …/status  (the steps + the ready gate, rendered verbatim)
//   saveCredentials(step) → PUT …/setup/credentials/{provider} → reload (the step flips to complete)
//   connectBot() → GET …/setup/bot/oauth-url → open it → poll …/setup/bot/status → reload
//   finish() → run the streamer OAuth via [onReadyToSignIn] → POST …/setup/complete
//
// Nothing is faked: a step is "complete" only when the backend's reloaded wizard/status says so — never an
// optimistic local flip. The flow advances to the streamer sign-in only once [SystemStatus.ready] is true
// (Twitch app + platform bot both configured), which the backend computes.
class SetupController(
    private val systemApi: SystemApi,
    private val connectLauncher: ConnectLauncher,
    // Hand off to the streamer OAuth once setup is ready. Returns true when the session was established
    // (the gate advances to the shell); false leaves the wizard up with [SetupError.SignIn] surfaced.
    private val onReadyToSignIn: suspend () -> Boolean,
) {
    private val _state: MutableStateFlow<SetupState> = MutableStateFlow(SetupState.Loading)

    /** The screen's render state: loading / steps (with field inputs + ready flag) / error. */
    val state: StateFlow<SetupState> = _state.asStateFlow()

    // The user-entered values per step, keyed by "<stepKey>.<fieldKey>". Held outside the rendered state so
    // a reload (which replaces the wizard) doesn't wipe in-progress input the user hasn't saved yet.
    private val fieldValues: MutableMap<String, String> = mutableMapOf()

    /** Load the self-describing wizard + readiness and render its steps. */
    suspend fun load() {
        _state.value = SetupState.Loading
        reload(busy = null, error = null)
    }

    /** Edit one field's value (keyed by step + field), clearing any prior error. */
    fun onFieldChange(stepKey: String, fieldKey: String, value: String) {
        fieldValues[fieldKeyOf(stepKey, fieldKey)] = value
        val current: SetupState.Steps = _state.value as? SetupState.Steps ?: return
        _state.value = current.copy(values = fieldValues.toMap(), error = null)
    }

    /** The current value of a field (empty when untouched). */
    fun valueOf(stepKey: String, fieldKey: String): String = fieldValues[fieldKeyOf(stepKey, fieldKey)].orEmpty()

    /**
     * Save a `save_credentials` step's inputs (Twitch / Spotify / YouTube / Discord), then reload so the
     * step flips to complete from the backend's re-read — never an optimistic flip.
     */
    suspend fun saveCredentials(step: SetupStep) {
        val current: SetupState.Steps = _state.value as? SetupState.Steps ?: return
        _state.value = current.copy(busy = step.key, error = null)

        val clientId: String = valueOf(step.key, FIELD_CLIENT_ID).trim()
        val clientSecret: String = valueOf(step.key, FIELD_CLIENT_SECRET).trim()
        if (clientId.isEmpty() || clientSecret.isEmpty()) {
            _state.value = current.copy(busy = null, error = SetupError.MissingFields(step.key))
            return
        }

        val result: ApiResult<Unit> =
            when (step.key) {
                STEP_TWITCH ->
                    systemApi.saveTwitchCredentials(
                        clientId = clientId,
                        clientSecret = clientSecret,
                        botUsername = valueOf(step.key, FIELD_BOT_USERNAME).trim().ifEmpty { null },
                    )
                STEP_SPOTIFY -> systemApi.saveSpotifyCredentials(clientId, clientSecret)
                STEP_YOUTUBE -> systemApi.saveYouTubeCredentials(clientId, clientSecret)
                STEP_DISCORD -> systemApi.saveDiscordCredentials(clientId, clientSecret)
                else -> ApiResult.Failure(saveUnsupported(step.key))
            }

        when (result) {
            is ApiResult.Failure -> _state.value = current.copy(busy = null, error = SetupError.Save(step.key, result.error.message))
            is ApiResult.Ok -> reload(busy = null, error = null)
        }
    }

    /**
     * Run the platform-bot authorization: open the backend-issued authorize URL (the token vaults
     * server-side), then reload so the bot step reflects the backend's re-read status.
     */
    suspend fun connectBot() {
        val current: SetupState.Steps = _state.value as? SetupState.Steps ?: return
        _state.value = current.copy(busy = STEP_PLATFORM_BOT, error = null)

        val outcome: ApiResult<Unit> =
            connectLauncher.awaitConnect { _ ->
                when (val url: ApiResult<BotOAuthUrl> = systemApi.botOAuthUrl()) {
                    is ApiResult.Failure -> ApiResult.Failure(url.error)
                    is ApiResult.Ok -> ApiResult.Ok(url.value.oauthUrl)
                }
            }

        when (outcome) {
            is ApiResult.Failure -> _state.value = current.copy(busy = null, error = SetupError.Bot(outcome.error.message))
            is ApiResult.Ok -> reload(busy = null, error = null)
        }
    }

    /**
     * Setup is ready (Twitch app + platform bot configured): run the streamer OAuth, and on success mark
     * setup complete. The gate advances to the shell inside [onReadyToSignIn]; a failure surfaces
     * [SetupError.SignIn] and leaves the wizard up.
     */
    suspend fun finish() {
        val current: SetupState.Steps = _state.value as? SetupState.Steps ?: return
        if (!current.ready) return
        _state.value = current.copy(busy = SIGNING_IN, error = null)

        val signedIn: Boolean = onReadyToSignIn()
        if (!signedIn) {
            _state.value = current.copy(busy = null, error = SetupError.SignIn)
            return
        }
        // The streamer session is live; finalize setup so the credential endpoints lock to admins.
        systemApi.completeSetup()
    }

    // Re-read the wizard + readiness from the backend and rebuild the steps state. Preserves the user's
    // in-progress field values (held in [fieldValues]) so a save/reload doesn't clear untouched inputs.
    private suspend fun reload(busy: String?, error: SetupError?) {
        val wizard: SetupWizard =
            when (val result: ApiResult<SetupWizard> = systemApi.wizard()) {
                is ApiResult.Failure -> {
                    _state.value = SetupState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        val ready: Boolean =
            when (val result: ApiResult<SystemStatus> = systemApi.status()) {
                is ApiResult.Failure -> wizard.complete
                is ApiResult.Ok -> result.value.ready
            }

        _state.value =
            SetupState.Steps(
                steps = wizard.steps,
                values = fieldValues.toMap(),
                ready = ready,
                busy = busy,
                error = error,
            )
    }

    private fun saveUnsupported(stepKey: String): ApiError =
        ApiError(
            status = 0,
            code = "UNSUPPORTED_STEP",
            message = "No save action for step $stepKey.",
        )

    private companion object {
        const val STEP_TWITCH: String = "twitch_app"
        const val STEP_PLATFORM_BOT: String = "platform_bot"
        const val STEP_SPOTIFY: String = "spotify"
        const val STEP_YOUTUBE: String = "youtube"
        const val STEP_DISCORD: String = "discord"

        const val FIELD_CLIENT_ID: String = "clientId"
        const val FIELD_CLIENT_SECRET: String = "clientSecret"
        const val FIELD_BOT_USERNAME: String = "botUsername"

        // A reserved busy token for the final streamer sign-in (distinct from any step key).
        const val SIGNING_IN: String = "__signing_in__"

        fun fieldKeyOf(stepKey: String, fieldKey: String): String = "$stepKey.$fieldKey"
    }
}

/** The setup wizard's render state. */
sealed interface SetupState {
    data object Loading : SetupState

    /**
     * The wizard's steps rendered verbatim from the backend, plus the per-field [values] the user has
     * entered, the [ready] gate (true ⇒ the streamer sign-in is enabled), the [busy] step key (null when
     * idle; the reserved sign-in token while signing in), and the current [error].
     */
    data class Steps(
        val steps: List<SetupStep>,
        val values: Map<String, String>,
        val ready: Boolean,
        val busy: String?,
        val error: SetupError?,
    ) : SetupState

    data class Error(val detail: String) : SetupState
}

/** Why a setup action failed — mapped to a localized message in the screen. */
sealed interface SetupError {
    /** A required credential field was left blank for [stepKey]. */
    data class MissingFields(val stepKey: String) : SetupError

    /** Saving [stepKey]'s credentials failed. */
    data class Save(val stepKey: String, val detail: String) : SetupError

    /** The platform-bot authorization failed. */
    data class Bot(val detail: String) : SetupError

    /** The final streamer sign-in failed. */
    data object SignIn : SetupError
}
