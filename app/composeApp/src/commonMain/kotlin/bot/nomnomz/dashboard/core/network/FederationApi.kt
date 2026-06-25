// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.network

import kotlinx.serialization.Serializable

// The typed federation facade — two planes:
//   1. Global peer management (FederationController, /federation/peers) — list/register/trust/revoke peers and
//      add/deactivate their signing keys. Broadcaster-only.
//   2. Per-channel opt-in management (ChannelFederationController, /channels/{channelId}/federation/opt-ins) —
//      the channel's federated capability subscriptions (upsert → enable; DELETE → disable).
//
// Backend routes:
//   GET    /api/v1/federation/peers                              →  PaginatedResponse<FederatedPeerDto>
//   POST   /api/v1/federation/peers                             →  StatusResponseDto<FederatedPeerDto>
//   GET    /api/v1/federation/peers/{peerId}                    →  StatusResponseDto<FederatedPeerDto>
//   POST   /api/v1/federation/peers/{peerId}/trust              →  StatusResponseDto<FederatedPeerDto>
//   POST   /api/v1/federation/peers/{peerId}/revoke             →  StatusResponseDto<FederatedPeerDto>
//   POST   /api/v1/federation/peers/{peerId}/keys               →  StatusResponseDto<FederatedPeerKeyDto>
//   DELETE /api/v1/federation/peers/{peerId}/keys/{keyId}       →  204 No Content
//   GET    /api/v1/channels/{channelId}/federation/opt-ins      →  PaginatedResponse<FederatedOptInDto>
//   PUT    /api/v1/channels/{channelId}/federation/opt-ins      →  StatusResponseDto<FederatedOptInDto>
//   DELETE /api/v1/channels/{channelId}/federation/opt-ins/{id} →  204 No Content
interface FederationApi {
    // ── Peers ─────────────────────────────────────────────────────────────────
    suspend fun listPeers(): ApiResult<List<FederatedPeer>>
    suspend fun registerPeer(body: RegisterPeerBody): ApiResult<FederatedPeer>
    suspend fun trustPeer(peerId: String): ApiResult<FederatedPeer>
    suspend fun revokePeer(peerId: String): ApiResult<FederatedPeer>
    suspend fun addPeerKey(peerId: String, body: AddPeerKeyBody): ApiResult<FederatedPeerKey>
    suspend fun deactivatePeerKey(peerId: String, keyId: String): ApiResult<Unit>

    // ── Channel opt-ins ───────────────────────────────────────────────────────
    suspend fun listOptIns(channelId: String): ApiResult<List<FederatedOptIn>>
    suspend fun upsertOptIn(channelId: String, body: UpsertOptInBody): ApiResult<FederatedOptIn>
    suspend fun removeOptIn(channelId: String, optInId: String): ApiResult<Unit>
}

class RestFederationApi(private val client: ApiClient) : FederationApi {

    override suspend fun listPeers(): ApiResult<List<FederatedPeer>> =
        when (
            val page: ApiResult<PaginatedEnvelope<FederatedPeer>> =
                client.getDirect("api/v1/federation/peers?page=1&pageSize=25")
        ) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }

    override suspend fun registerPeer(body: RegisterPeerBody): ApiResult<FederatedPeer> =
        client.postEnvelope("api/v1/federation/peers", body)

    override suspend fun trustPeer(peerId: String): ApiResult<FederatedPeer> =
        client.postEnvelope("api/v1/federation/peers/$peerId/trust", Unit)

    override suspend fun revokePeer(peerId: String): ApiResult<FederatedPeer> =
        client.postEnvelope("api/v1/federation/peers/$peerId/revoke", Unit)

    override suspend fun addPeerKey(peerId: String, body: AddPeerKeyBody): ApiResult<FederatedPeerKey> =
        client.postEnvelope("api/v1/federation/peers/$peerId/keys", body)

    override suspend fun deactivatePeerKey(peerId: String, keyId: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/federation/peers/$peerId/keys/$keyId")

    override suspend fun listOptIns(channelId: String): ApiResult<List<FederatedOptIn>> =
        when (
            val page: ApiResult<PaginatedEnvelope<FederatedOptIn>> =
                client.getDirect("api/v1/channels/$channelId/federation/opt-ins?page=1&pageSize=25")
        ) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }

    override suspend fun upsertOptIn(channelId: String, body: UpsertOptInBody): ApiResult<FederatedOptIn> =
        client.putEnvelope("api/v1/channels/$channelId/federation/opt-ins", body)

    override suspend fun removeOptIn(channelId: String, optInId: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/channels/$channelId/federation/opt-ins/$optInId")
}

/** A trusted federation peer (backend `FederatedPeerDto`). */
@Serializable
data class FederatedPeer(
    val id: String = "",
    val name: String = "",
    val baseUrl: String = "",
    val isTrusted: Boolean = false,
    val isRevoked: Boolean = false,
    val createdAt: String = "",
    val updatedAt: String = "",
)

/** A peer's registered signing key (backend `FederatedPeerKeyDto`). */
@Serializable
data class FederatedPeerKey(
    val id: String = "",
    val peerId: String = "",
    val keyId: String = "",
    val isActive: Boolean = true,
    val createdAt: String = "",
)

/** A channel's federated capability opt-in (backend `FederatedOptInDto`). */
@Serializable
data class FederatedOptIn(
    val id: String = "",
    val peerId: String = "",
    val peerName: String = "",
    val capability: String = "",
    val isEnabled: Boolean = false,
    val createdAt: String = "",
    val updatedAt: String = "",
)

/** Register a new peer. */
@Serializable
data class RegisterPeerBody(val name: String, val baseUrl: String)

/** Add a signing key to a peer. */
@Serializable
data class AddPeerKeyBody(val keyId: String, val publicKey: String)

/** Enable or update a federated opt-in. */
@Serializable
data class UpsertOptInBody(val peerId: String, val capability: String, val isEnabled: Boolean = true)
