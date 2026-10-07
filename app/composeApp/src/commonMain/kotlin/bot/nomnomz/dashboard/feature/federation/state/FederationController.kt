// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.federation.state

import bot.nomnomz.dashboard.core.feedback.Feedback
import bot.nomnomz.dashboard.core.feedback.NoOpFeedback
import bot.nomnomz.dashboard.core.network.AddPeerKeyBody
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.FederatedOptIn
import bot.nomnomz.dashboard.core.network.FederatedPeer
import bot.nomnomz.dashboard.core.network.FederationApi
import bot.nomnomz.dashboard.core.network.RegisterPeerBody
import bot.nomnomz.dashboard.core.network.UpsertOptInBody
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.federation_action_error

// The Federation page's state-holder. Loads the global peer list and the channel's opt-in list, then
// drives all mutations: register/trust/revoke peers, manage signing keys, upsert/remove opt-ins. Write
// failures announce on the shell-level [feedback] toast, not a local state field.
class FederationController(
    private val channelsApi: ChannelsApi,
    private val federationApi: FederationApi,
    private val feedback: Feedback = NoOpFeedback,
) {
    private val _state: MutableStateFlow<FederationState> = MutableStateFlow(FederationState.Loading)

    /** The page render state. */
    val state: StateFlow<FederationState> = _state.asStateFlow()

    private var channelId: String? = null

    /** Resolve the active channel, then load peers + opt-ins. */
    suspend fun load() {
        // Only show the full-page loading state on first load; a refetch after a mutation keeps
        // the current content on screen (no flash) and swaps it when the new data arrives.
        if (_state.value !is FederationState.Ready) _state.value = FederationState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = FederationState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }
        channelId = channel.id

        val peers: List<FederatedPeer> =
            when (val result: ApiResult<List<FederatedPeer>> = federationApi.listPeers()) {
                is ApiResult.Failure -> {
                    _state.value = FederationState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        val optIns: List<FederatedOptIn> =
            when (val result: ApiResult<List<FederatedOptIn>> = federationApi.listOptIns(channel.id)) {
                is ApiResult.Failure -> {
                    _state.value = FederationState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        _state.value = FederationState.Ready(peers = peers, optIns = optIns)
    }

    // ── Peer management ───────────────────────────────────────────────────────

    /** Register a peer. The failure goes back to the dialog that asked, which shows it in place. */
    suspend fun registerPeer(name: String, baseUrl: String): ApiResult<Unit> =
        afterDialogWrite(federationApi.registerPeer(RegisterPeerBody(name, baseUrl)).asUnit())

    suspend fun trustPeer(peerId: String) {
        when (val result: ApiResult<FederatedPeer> = federationApi.trustPeer(peerId)) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    /** Revoke a peer. The failure goes back to the confirm that asked, which shows it in place. */
    suspend fun revokePeer(peerId: String): ApiResult<Unit> = afterDialogWrite(federationApi.revokePeer(peerId).asUnit())

    suspend fun addPeerKey(peerId: String, keyId: String, publicKey: String) {
        when (val result: ApiResult<bot.nomnomz.dashboard.core.network.FederatedPeerKey> =
            federationApi.addPeerKey(peerId, AddPeerKeyBody(keyId, publicKey))
        ) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    suspend fun deactivatePeerKey(peerId: String, keyId: String) {
        when (val result: ApiResult<Unit> = federationApi.deactivatePeerKey(peerId, keyId)) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    // ── Opt-in management ─────────────────────────────────────────────────────

    /** Add or update an opt-in. The failure goes back to the form that asked, which shows it in place. */
    suspend fun upsertOptIn(peerId: String, capability: String, enabled: Boolean): ApiResult<Unit> {
        val channel: String = channelId ?: return noChannel()
        return afterDialogWrite(federationApi.upsertOptIn(channel, UpsertOptInBody(peerId, capability, enabled)).asUnit())
    }

    /** Flip an opt-in from its row switch. No dialog is open, so a failure announces on the shell toast. */
    suspend fun toggleOptIn(peerId: String, capability: String, enabled: Boolean) {
        val result: ApiResult<Unit> = upsertOptIn(peerId, capability, enabled)
        if (result is ApiResult.Failure) failWrite(result.error.message)
    }

    /** Remove an opt-in. The failure goes back to the confirm that asked, which shows it in place. */
    suspend fun removeOptIn(optInId: String): ApiResult<Unit> {
        val channel: String = channelId ?: return noChannel()
        return afterDialogWrite(federationApi.removeOptIn(channel, optInId))
    }

    private suspend fun afterDialogWrite(result: ApiResult<Unit>): ApiResult<Unit> {
        if (result is ApiResult.Ok) load()
        return result
    }

    private fun ApiResult<*>.asUnit(): ApiResult<Unit> =
        when (this) {
            is ApiResult.Ok -> ApiResult.Ok(Unit)
            is ApiResult.Failure -> this
        }

    private fun noChannel(): ApiResult<Unit> = ApiResult.Failure(ApiError(0, "NO_CHANNEL", "No active channel."))

    private fun failWrite(detail: String) {
        feedback.error(Res.string.federation_action_error, detail)
    }
}

/** The Federation page render state. */
sealed interface FederationState {
    data object Loading : FederationState

    data class Ready(
        val peers: List<FederatedPeer>,
        val optIns: List<FederatedOptIn>,
    ) : FederationState

    data class Error(val detail: String) : FederationState
}
