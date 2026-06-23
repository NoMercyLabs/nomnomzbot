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
// Status: GET …/integrations/status reports the generic providers (Spotify/YouTube). Discord is reported
// by the older IntegrationsController list and is folded into [status] here from that list, so the screen
// gets one unified status set across all three. (Backend gap — see report.)
interface IntegrationsApi {
    /** Per-provider status across all three providers (Spotify/YouTube + Discord folded in). */
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

    override suspend fun status(channelId: String): ApiResult<List<IntegrationStatus>> {
        val generic: ApiResult<List<IntegrationStatus>> =
            client.getEnvelope("api/v1/channels/$channelId/integrations/status")
        if (generic is ApiResult.Failure) return generic

        val base: List<IntegrationStatus> = (generic as ApiResult.Ok).value
        // Fold in Discord from the legacy list (best-effort: a failure leaves Discord absent rather than
        // failing the whole screen).
        val discord: IntegrationStatus? = readDiscordStatus(channelId)
        return ApiResult.Ok(if (discord != null) base + discord else base)
    }

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

    // The legacy IntegrationsController list is the only surface that reports Discord connectivity. We
    // read it solely to surface Discord; Spotify/YouTube come from the authoritative generic status.
    private suspend fun readDiscordStatus(channelId: String): IntegrationStatus? {
        val list: ApiResult<LegacyIntegrationsResponse> =
            client.getEnvelope("api/v1/channels/$channelId/integrations")
        if (list !is ApiResult.Ok) return null
        val discord: LegacyIntegration =
            list.value.integrations.firstOrNull { it.id.equals("discord", ignoreCase = true) }
                ?: return null
        return IntegrationStatus(
            provider = "discord",
            connected = discord.connected,
            accountName = discord.connectedAs,
        )
    }
}
