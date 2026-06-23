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

// The typed integrations facade. Integration tokens are stored SERVER-SIDE, so the client only ever:
//   (1) reads per-provider status, (2) starts a connect (and opens the resulting URL in the browser),
//   (3) disconnects. It never sees a provider token.
//
// Two start mechanisms, because the backend models the providers two ways:
//   - Spotify / YouTube (generic vaulted flow, IntegrationOAuthController):
//       POST /api/v1/channels/{channelId}/integrations/{provider}/connect  (Authorization: Bearer + body
//         { scopeSetKey, returnUrl })  →  StatusResponseDto<OAuthStartDto>.
//       The returned authorizeUrl points at the PROVIDER; the client opens it; the provider redirects to
//       the backend callback, which vaults the token and redirects back to returnUrl.
//   - Discord (bespoke bot-install flow, DiscordOAuthController):
//       GET /api/v1/channels/{channelId}/integrations/discord/callback/start  ([AllowAnonymous] redirect).
//       The client opens this URL directly (no bearer needed); the backend redirects to Discord, vaults
//       the bot token on callback, then redirects back to the frontend.
//
// Status: GET …/integrations/status is the unified read model — one call reports all three providers
// (Spotify/YouTube + Discord).
interface IntegrationsApi {
    /** Per-provider status across all three providers (Spotify/YouTube + Discord), from the unified endpoint. */
    suspend fun status(channelId: String): ApiResult<List<IntegrationStatus>>

    /** Start the Spotify/YouTube connect: the authenticated POST returns the provider authorize URL. */
    suspend fun startGenericConnect(
        channelId: String,
        provider: String,
        scopeSetKey: String,
        returnUrl: String?,
    ): ApiResult<OAuthStart>

    /** The absolute Discord connect-start URL the client opens directly in the browser. */
    fun discordStartUrl(baseUrl: String, channelId: String): String

    /** Disconnect a generic provider (Spotify/YouTube) — revokes + crypto-shreds the vaulted token. */
    suspend fun disconnectGeneric(channelId: String, provider: String): ApiResult<Unit>

    /** Disconnect Discord — severs every linked guild for this channel (legacy IntegrationsController). */
    suspend fun disconnectDiscord(channelId: String): ApiResult<Unit>
}

class RestIntegrationsApi(private val client: ApiClient) : IntegrationsApi {

    override suspend fun status(channelId: String): ApiResult<List<IntegrationStatus>> =
        client.getEnvelope("api/v1/channels/$channelId/integrations/status")

    override suspend fun startGenericConnect(
        channelId: String,
        provider: String,
        scopeSetKey: String,
        returnUrl: String?,
    ): ApiResult<OAuthStart> =
        client.postEnvelope(
            "api/v1/channels/$channelId/integrations/$provider/connect",
            ConnectIntegrationBody(scopeSetKey = scopeSetKey, returnUrl = returnUrl),
        )

    override fun discordStartUrl(baseUrl: String, channelId: String): String =
        "${baseUrl.trimEnd('/')}/api/v1/channels/$channelId/integrations/discord/callback/start"

    override suspend fun disconnectGeneric(channelId: String, provider: String): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/integrations/$provider/disconnect")

    override suspend fun disconnectDiscord(channelId: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/channels/$channelId/integrations/discord")
}
