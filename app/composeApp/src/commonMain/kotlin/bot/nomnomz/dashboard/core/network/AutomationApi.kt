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

// The typed automation-API facade — the channel's external API tokens (Streamer.bot-parity automation:
// external tools authenticate with these) and the one-time device pairing codes (stream-deck.md). Real data
// only: the backend lists the channel's stored tokens (never a fabricated secret). The state holder depends
// on this interface and fakes it in tests without HTTP.
//
// THE SECRET IS SHOWN EXACTLY ONCE: create + rotate return the plaintext [IssuedAutomationToken.secret] in
// their response and it is never retrievable again — the screen shows it in a copy-once dialog, then only ever
// the [AutomationToken.tokenPrefix].
//
// Backend routes (AutomationTokensController + AutomationPairingController):
//   GET    /api/v1/channels/{channelId}/automation/tokens               →  PaginatedResponse<AutomationTokenDto>
//   POST   /api/v1/channels/{channelId}/automation/tokens               →  StatusResponseDto<IssuedAutomationTokenDto>
//   POST   /api/v1/channels/{channelId}/automation/tokens/{id}/rotate   →  StatusResponseDto<IssuedAutomationTokenDto>
//   DELETE /api/v1/channels/{channelId}/automation/tokens/{id}          →  204 No Content
//   POST   /api/v1/automation/pair-codes                                →  StatusResponseDto<PairingCodeDto>
interface AutomationApi {
    /** The channel's automation tokens — active and revoked (a revoked token stays listed as a tombstone). */
    suspend fun tokens(channelId: String): ApiResult<List<AutomationToken>>

    /**
     * Issue a new token. The plaintext [IssuedAutomationToken.secret] is in the response ONCE; after that only
     * the [AutomationToken.tokenPrefix] is ever available.
     */
    suspend fun createToken(channelId: String, body: CreateAutomationTokenBody): ApiResult<IssuedAutomationToken>

    /** Rotate token [tokenId]: invalidates the old secret and returns a fresh one (shown once). */
    suspend fun rotateToken(channelId: String, tokenId: String): ApiResult<IssuedAutomationToken>

    /** Revoke token [tokenId] (a tombstone — the row stays listed with a revoked badge). */
    suspend fun revokeToken(channelId: String, tokenId: String): ApiResult<Unit>

    /**
     * Mint a one-time device pairing code (the tenant is resolved from the active channel). The device redeems
     * it itself; after pairing it simply appears in the token list. The code expires in ~5 minutes.
     */
    suspend fun mintPairCode(body: MintPairingCodeBody): ApiResult<PairingCode>
}

class RestAutomationApi(private val client: ApiClient) : AutomationApi {
    // The list is a PaginatedResponse (a flat `{ data: [...] }`), read with getDirect rather than getEnvelope.
    override suspend fun tokens(channelId: String): ApiResult<List<AutomationToken>> =
        when (
            val page: ApiResult<PaginatedEnvelope<AutomationToken>> =
                client.getDirect("api/v1/channels/$channelId/automation/tokens?page=1&pageSize=100")
        ) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }

    override suspend fun createToken(
        channelId: String,
        body: CreateAutomationTokenBody,
    ): ApiResult<IssuedAutomationToken> =
        client.postEnvelope("api/v1/channels/$channelId/automation/tokens", body)

    override suspend fun rotateToken(channelId: String, tokenId: String): ApiResult<IssuedAutomationToken> =
        client.postEnvelope("api/v1/channels/$channelId/automation/tokens/$tokenId/rotate")

    override suspend fun revokeToken(channelId: String, tokenId: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/channels/$channelId/automation/tokens/$tokenId")

    override suspend fun mintPairCode(body: MintPairingCodeBody): ApiResult<PairingCode> =
        client.postEnvelope("api/v1/automation/pair-codes", body)
}

/** The automation-token scope keys — the granular abilities a token grants (subset of these). */
object AutomationScope {
    const val Invoke: String = "invoke"
    const val Read: String = "read"
    const val Events: String = "events"
    const val Chat: String = "chat"

    /** The full ordered set the create dialog offers as checkboxes. */
    val all: List<String> = listOf(Invoke, Read, Events, Chat)
}

/**
 * One automation token (backend `AutomationTokenDto`) — the metadata surface, never the secret. [tokenPrefix]
 * is the visible short prefix (the full secret is only ever in the create/rotate response). [scopes] and
 * [allowedPipelineIds] constrain it; [revokedAt] non-null marks a revoked tombstone (still listed).
 */
@Serializable
data class AutomationToken(
    val id: String = "",
    val name: String = "",
    val tokenPrefix: String = "",
    val scopes: List<String> = emptyList(),
    val allowedPipelineIds: List<String> = emptyList(),
    val lastUsedAt: String? = null,
    val expiresAt: String? = null,
    val revokedAt: String? = null,
    val createdAt: String = "",
)

/**
 * A freshly-issued token (backend `IssuedAutomationTokenDto`): the plaintext [secret] shown EXACTLY ONCE plus
 * the [token] metadata row. The screen surfaces [secret] in a copy-once dialog, then discards it.
 */
@Serializable
data class IssuedAutomationToken(val secret: String = "", val token: AutomationToken = AutomationToken())

/**
 * The create-token body (backend `CreateAutomationTokenRequest`). [scopes] is a subset of [AutomationScope];
 * [allowedPipelineIds] optionally restricts an `invoke` token to specific pipelines; [expiresAt] is an
 * optional ISO-8601 expiry (null = never).
 */
@Serializable
data class CreateAutomationTokenBody(
    val name: String,
    val scopes: List<String>,
    val allowedPipelineIds: List<String>? = null,
    val expiresAt: String? = null,
)

/** A minted device pairing code (backend `PairingCodeDto`): the 8-char [code] and its [expiresAt] (~5 min). */
@Serializable
data class PairingCode(val code: String = "", val expiresAt: String = "")

/**
 * The mint-pairing-code body (backend `MintPairingCodeRequest`). [deviceLabel] names the device; [scopes]
 * default to invoke+events+read on the backend (add `chat` only via an explicit opt-in).
 */
@Serializable
data class MintPairingCodeBody(val deviceLabel: String, val scopes: List<String>)
