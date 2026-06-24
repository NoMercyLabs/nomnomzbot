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

import bot.nomnomz.dashboard.core.feedback.Feedback
import bot.nomnomz.dashboard.core.feedback.NoOpFeedback
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.SystemApi
import bot.nomnomz.dashboard.core.network.SystemStatus
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.twitch_app_save_error
import nomnomzbot.composeapp.generated.resources.twitch_app_saved

// The dashboard-side "Twitch application" credential state-holder: the SAME platform-app-credentials flow the
// first-run wizard drives (SetupController.saveCredentials → PUT …/setup/credentials/twitch → the vaulted
// store), surfaced as an in-dashboard management card so a signed-in admin can configure or repoint their
// PERSONAL Twitch client (BYOC) without hand-editing config. It reads the live readiness (whether the bot is
// running on the shared client vs a personally-configured one) from the same [SystemApi.status] the wizard
// reads, exposes the EXACT redirect URL the user must register (derived from the active backend base URL),
// and writes through the wizard's own credential endpoint — the bearer the shared [ApiClient] attaches lets
// the backend's post-setup admin gate pass, so this is the supported way to overwrite live OAuth credentials.
//
// Secret is OPTIONAL by design: the bot fully functions on the client id alone via the secret-free device-code
// flow (the shared public client or the user's own client id). A secret is purely an enhancement that unlocks
// the smoother one-tap redirect sign-in — the bot is never gated on it, so [save] requires only a client id.
//
// Nothing is faked: the configured/shared distinction comes only from the backend's re-read status; a save is
// reflected by reloading that status, never an optimistic local flip.
class TwitchAppCredentialsController(
    private val systemApi: SystemApi,
    // The active backend's base URL — the redirect URL the user registers on their Twitch app is rooted here,
    // so a different connection (self-host localhost vs a remote operator URL) shows the correct address.
    private val baseUrlProvider: () -> String?,
    private val feedback: Feedback = NoOpFeedback,
) {
    private val _state: MutableStateFlow<TwitchAppCredentialsState> =
        MutableStateFlow(TwitchAppCredentialsState.Loading)

    /** The card's render state: loading / ready (with the current configuration + redirect URL) / error. */
    val state: StateFlow<TwitchAppCredentialsState> = _state.asStateFlow()

    /** Read the live system status to render whether a personal Twitch app is configured or the shared client is in use. */
    suspend fun load() {
        _state.value = TwitchAppCredentialsState.Loading
        reload(saving = false)
    }

    /**
     * Persist the user's personal Twitch app credentials through the wizard's own endpoint, then reload so the
     * configured/shared state reflects the backend's re-read. The client id is required; the secret is optional
     * (blank ⇒ device-code-only, no secret stored) — never gate the save on the secret. A blank client id is a
     * client-side guard that never reaches the backend. On success the feedback host announces it; a failure
     * surfaces inline on the Ready state and on the feedback host, without discarding the user's typed values.
     */
    suspend fun save(clientId: String, clientSecret: String) {
        val current: TwitchAppCredentialsState.Ready =
            _state.value as? TwitchAppCredentialsState.Ready ?: return

        val id: String = clientId.trim()
        if (id.isEmpty()) {
            _state.value = current.copy(saveError = SaveError.MissingClientId)
            return
        }

        _state.value = current.copy(saving = true, saveError = null)

        // The secret rides as-is (the backend stores an empty secret as "no secret" — the device-code path
        // the bot defaults to needs none); only the id is mandatory.
        val result: ApiResult<Unit> =
            systemApi.saveTwitchCredentials(
                clientId = id,
                clientSecret = clientSecret.trim(),
                botUsername = null,
            )

        when (result) {
            is ApiResult.Failure -> {
                feedback.error(Res.string.twitch_app_save_error, result.error.message)
                _state.value =
                    current.copy(saving = false, saveError = SaveError.Backend(result.error.message))
            }
            is ApiResult.Ok -> {
                feedback.success(Res.string.twitch_app_saved)
                // Re-read the status so "configured" reflects the backend, not an optimistic flip.
                reload(saving = false)
            }
        }
    }

    // Re-read the live status and rebuild the Ready state with the current configuration + the redirect URL the
    // user registers on their Twitch app. A status read failure renders the error state (with the detail).
    private suspend fun reload(saving: Boolean) {
        when (val result: ApiResult<SystemStatus> = systemApi.status()) {
            is ApiResult.Failure -> _state.value = TwitchAppCredentialsState.Error(result.error.message)
            is ApiResult.Ok ->
                _state.value =
                    TwitchAppCredentialsState.Ready(
                        configured = result.value.checks.twitchApp.ok,
                        redirectUrl = redirectUrl(),
                        saving = saving,
                        saveError = null,
                    )
        }
    }

    // The exact OAuth Redirect URL the user pastes into their Twitch app — rooted at the active backend so the
    // address is correct for THIS connection. Null when no backend is active (the card then hides the chip).
    private fun redirectUrl(): String? {
        val base: String = baseUrlProvider()?.trimEnd('/') ?: return null
        return "$base$REDIRECT_PATH"
    }

    private companion object {
        const val REDIRECT_PATH: String = "/api/v1/auth/twitch/callback"
    }
}

/** The Twitch-app credential card's render state. */
sealed interface TwitchAppCredentialsState {
    data object Loading : TwitchAppCredentialsState

    /**
     * The live configuration the card renders over. [configured] is true when a personal Twitch app is set
     * (the backend reports the Twitch app as ready) — false means the bot runs on the shared public client.
     * [redirectUrl] is the exact address the user must register on their Twitch app (null only when no backend
     * is active). [saving] is true while a write is in flight; [saveError] carries the last save failure.
     */
    data class Ready(
        val configured: Boolean,
        val redirectUrl: String?,
        val saving: Boolean = false,
        val saveError: SaveError? = null,
    ) : TwitchAppCredentialsState

    data class Error(val detail: String) : TwitchAppCredentialsState
}

/** Why a Twitch-app credential save failed — mapped to a localized message in the card. */
sealed interface SaveError {
    /** The required client id was left blank (the secret is optional, so it never triggers this). */
    data object MissingClientId : SaveError

    /** The backend rejected the save (e.g. not an admin after setup completed, or a network failure). */
    data class Backend(val detail: String) : SaveError
}
