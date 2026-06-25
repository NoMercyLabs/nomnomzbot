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

import bot.nomnomz.dashboard.core.network.AddPeerKeyBody
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

// The Federation page's state-holder. Loads the global peer list and the channel's opt-in list, then
// drives all mutations: register/trust/revoke peers, manage signing keys, upsert/remove opt-ins.
class FederationController(
    private val channelsApi: ChannelsApi,
    private val federationApi: FederationApi,
) {
    private val _state: MutableStateFlow<FederationState> = MutableStateFlow(FederationState.Loading)

    /** The page render state. */
    val state: StateFlow<FederationState> = _state.asStateFlow()

    private var channelId: String? = null

    /** Resolve the active channel, then load peers + opt-ins. */
    suspend fun load() {
        _state.value = FederationState.Loading

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

    suspend fun registerPeer(name: String, baseUrl: String) {
        when (val result: ApiResult<FederatedPeer> = federationApi.registerPeer(RegisterPeerBody(name, baseUrl))) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    suspend fun trustPeer(peerId: String) {
        when (val result: ApiResult<FederatedPeer> = federationApi.trustPeer(peerId)) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    suspend fun revokePeer(peerId: String) {
        when (val result: ApiResult<FederatedPeer> = federationApi.revokePeer(peerId)) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

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

    suspend fun upsertOptIn(peerId: String, capability: String, enabled: Boolean) {
        val channel: String = channelId ?: return failWrite("No active channel.")
        when (val result: ApiResult<FederatedOptIn> = federationApi.upsertOptIn(channel, UpsertOptInBody(peerId, capability, enabled))) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    suspend fun removeOptIn(optInId: String) {
        val channel: String = channelId ?: return failWrite("No active channel.")
        when (val result: ApiResult<Unit> = federationApi.removeOptIn(channel, optInId)) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    private fun failWrite(detail: String) {
        val current: FederationState = _state.value
        _state.value =
            if (current is FederationState.Ready) current.copy(actionError = detail)
            else FederationState.Error(detail)
    }
}

/** The Federation page render state. */
sealed interface FederationState {
    data object Loading : FederationState

    data class Ready(
        val peers: List<FederatedPeer>,
        val optIns: List<FederatedOptIn>,
        val actionError: String? = null,
    ) : FederationState

    data class Error(val detail: String) : FederationState
}
