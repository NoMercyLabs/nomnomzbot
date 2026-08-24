// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.integrations.state

import bot.nomnomz.dashboard.core.connection.SessionStore
import bot.nomnomz.dashboard.core.feedback.Feedback
import bot.nomnomz.dashboard.core.feedback.NoOpFeedback
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSpotifyCredentials
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.IntegrationsApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.spotify_credentials_cleared
import nomnomzbot.composeapp.generated.resources.spotify_credentials_save_error
import nomnomzbot.composeapp.generated.resources.spotify_credentials_saved

// The channel-scoped Spotify BYOC credential card's state-holder (S-BYOC-spotify-b). Unlike the app-level
// system credentials (TwitchAppCredentialsController / IntegrationsController.saveProviderCredentials, which
// register the FIRST/shared app client before OAuth can run at all), this is the PER-CHANNEL override that
// lets a streamer point `!sr` song requests at HER OWN Spotify app instead of the app-level default —
// `GET/PUT/DELETE …/channels/{channelId}/integrations/spotify/credentials` (commit 1d74b4fb). Resolution is
// server-side: channel-own credentials win, else the app-level default, else an honest failure — this card
// exists to surface that distinction, never to hide it (a streamer must be able to tell which is active).
//
// The secret is write-only end to end: [ChannelSpotifyCredentials.hasClientSecret] is the only signal ever
// read back — the value itself never round-trips, and [state] never retains a typed secret past a save/clear
// (the Ready state carries no secret field at all).
class SpotifyChannelCredentialsController(
    private val channelsApi: ChannelsApi,
    private val integrationsApi: IntegrationsApi,
    private val sessionStore: SessionStore,
    private val feedback: Feedback = NoOpFeedback,
) {
    private val _state: MutableStateFlow<SpotifyChannelCredentialsState> =
        MutableStateFlow(SpotifyChannelCredentialsState.Loading)

    /** The card's render state: loading / ready (own vs app-default + redirect URL) / error. */
    val state: StateFlow<SpotifyChannelCredentialsState> = _state.asStateFlow()

    private var channelId: String? = null

    /** Resolve the active channel, then read its Spotify BYOC state. */
    suspend fun load() {
        if (_state.value !is SpotifyChannelCredentialsState.Ready) {
            _state.value = SpotifyChannelCredentialsState.Loading
        }

        val id: String =
            channelId ?: when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = SpotifyChannelCredentialsState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value.id.also { channelId = it }
            }

        reload(id, saving = false)
    }

    /**
     * Save this channel's own Spotify client id + secret, then reload from the backend's re-read — never an
     * optimistic flip — so [SpotifyChannelCredentialsState.Ready.usingOwnCredentials] and [hasClientSecret]
     * always reflect what the server actually stored. The client id is required; a blank one is a client-side
     * guard that never reaches the backend.
     */
    suspend fun save(clientId: String, clientSecret: String) {
        val id: String = channelId ?: return
        val current: SpotifyChannelCredentialsState.Ready =
            _state.value as? SpotifyChannelCredentialsState.Ready ?: return

        val trimmedId: String = clientId.trim()
        if (trimmedId.isEmpty()) {
            _state.value = current.copy(saveError = SpotifySaveError.MissingClientId)
            return
        }

        _state.value = current.copy(saving = true, saveError = null)

        when (
            val result: ApiResult<ChannelSpotifyCredentials> =
                integrationsApi.saveSpotifyCredentials(id, trimmedId, clientSecret.trim())
        ) {
            is ApiResult.Failure -> {
                feedback.error(Res.string.spotify_credentials_save_error, result.error.message)
                _state.value =
                    current.copy(saving = false, saveError = SpotifySaveError.Backend(result.error.message))
            }
            is ApiResult.Ok -> {
                feedback.success(Res.string.spotify_credentials_saved)
                reload(id, saving = false)
            }
        }
    }

    /**
     * Clear this channel's own Spotify credentials, falling back to the app-level default (if any). The
     * caller confirms first (the card shows a confirm dialog before calling this) — this method itself just
     * performs the delete + re-read.
     */
    suspend fun clear() {
        val id: String = channelId ?: return
        val current: SpotifyChannelCredentialsState.Ready =
            _state.value as? SpotifyChannelCredentialsState.Ready ?: return

        _state.value = current.copy(saving = true, saveError = null)

        when (val result: ApiResult<ChannelSpotifyCredentials> = integrationsApi.clearSpotifyCredentials(id)) {
            is ApiResult.Failure -> {
                feedback.error(Res.string.spotify_credentials_save_error, result.error.message)
                _state.value =
                    current.copy(saving = false, saveError = SpotifySaveError.Backend(result.error.message))
            }
            is ApiResult.Ok -> {
                feedback.success(Res.string.spotify_credentials_cleared)
                reload(id, saving = false)
            }
        }
    }

    // Re-read the channel's Spotify BYOC state and rebuild the Ready state with the current
    // own-vs-app-default distinction + the redirect URL the user registers on their Spotify app.
    private suspend fun reload(id: String, saving: Boolean) {
        when (val result: ApiResult<ChannelSpotifyCredentials> = integrationsApi.spotifyCredentials(id)) {
            is ApiResult.Failure -> _state.value = SpotifyChannelCredentialsState.Error(result.error.message)
            is ApiResult.Ok ->
                _state.value =
                    SpotifyChannelCredentialsState.Ready(
                        usingOwnCredentials = result.value.clientId != null,
                        clientId = result.value.clientId,
                        hasClientSecret = result.value.hasClientSecret,
                        redirectUrl = redirectUrl(),
                        saving = saving,
                        saveError = null,
                    )
        }
    }

    // The exact OAuth redirect URL the operator registers on their Spotify app — the same generic
    // integrations callback every BYOC provider uses, rooted at the active backend base.
    private fun redirectUrl(): String? {
        val base: String = sessionStore.baseUrl()?.trimEnd('/') ?: return null
        return "$base/api/v1/integrations/spotify/callback"
    }
}

/** The Spotify channel-credentials card's render state. */
sealed interface SpotifyChannelCredentialsState {
    data object Loading : SpotifyChannelCredentialsState

    /**
     * [usingOwnCredentials] is the whole point of this card: true = this channel's `!sr` resolves against ITS
     * OWN Spotify app; false = it falls back to the app-level default. [clientId] is safe to show (null when
     * using the default). [hasClientSecret] is the only secret-configured signal — the secret value itself
     * never appears here. [redirectUrl] is the exact address to register (null only when no backend is active).
     */
    data class Ready(
        val usingOwnCredentials: Boolean,
        val clientId: String?,
        val hasClientSecret: Boolean,
        val redirectUrl: String?,
        val saving: Boolean = false,
        val saveError: SpotifySaveError? = null,
    ) : SpotifyChannelCredentialsState

    data class Error(val detail: String) : SpotifyChannelCredentialsState
}

/** Why a Spotify channel-credential save/clear failed. */
sealed interface SpotifySaveError {
    data object MissingClientId : SpotifySaveError

    data class Backend(val detail: String) : SpotifySaveError
}

/**
 * The channel-scoped Spotify BYOC Gate-2 action keys — read the card's state on [ReadAction], write (save or
 * clear) on [WriteAction]. Gated on the caller's RESOLVED held action keys (`ResolvedAccess.heldActionKeys`),
 * never a raw management role, so the backend's per-action authorization (floor + overrides + permits) is the
 * single source of truth the client mirrors.
 */
object SpotifyChannelCredentialsAccess {
    const val ReadAction: String = "integration:read"
    const val WriteAction: String = "integration:write"

    /** Whether the caller may see the card at all — they hold [ReadAction] in their resolved held keys. */
    fun canRead(heldActionKeys: Set<String>): Boolean = ReadAction in heldActionKeys

    /** Whether the caller may save/clear credentials — they hold [WriteAction] in their resolved held keys. */
    fun canWrite(heldActionKeys: Set<String>): Boolean = WriteAction in heldActionKeys
}
