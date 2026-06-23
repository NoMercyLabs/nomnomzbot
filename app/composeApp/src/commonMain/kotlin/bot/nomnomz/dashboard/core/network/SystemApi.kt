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

// The typed first-run setup facade (frontend.md §3.1) — the only integration point the onboarding wizard
// uses to read system readiness and save the platform's app credentials. Every call is ANONYMOUS (the
// system must be configurable before any user can sign in), so these run against the chosen base URL with
// no bearer. The state holder depends on this interface (the existing "depend on interfaces" convention),
// so it fakes the API in tests without HTTP.
//
// Backend routes (SystemController, all [AllowAnonymous] during the first-run window):
//   GET  /api/v1/system/status                         → StatusResponseDto<SystemStatusDto>
//   GET  /api/v1/system/setup/wizard                   → StatusResponseDto<SetupWizardDto>
//   PUT  /api/v1/system/setup/credentials/twitch       (clientId, clientSecret, botUsername?)
//   PUT  /api/v1/system/setup/credentials/spotify      (clientId, clientSecret)
//   PUT  /api/v1/system/setup/credentials/youtube      (clientId, clientSecret)
//   PUT  /api/v1/system/setup/credentials/discord      (clientId, clientSecret)
//   GET  /api/v1/system/setup/bot/oauth-url            → StatusResponseDto<{ oauthUrl }>
//   GET  /api/v1/system/setup/bot/status               → StatusResponseDto<BotStatusDto>
//   POST /api/v1/system/setup/complete
interface SystemApi {
    /** System readiness — the [SystemStatus.ready] gate the onboarding flow routes on. */
    suspend fun status(): ApiResult<SystemStatus>

    /** The self-describing wizard the UI renders the whole first-run flow from. */
    suspend fun wizard(): ApiResult<SetupWizard>

    /** Save the platform Twitch app credentials (Client ID/Secret + optional bot username). */
    suspend fun saveTwitchCredentials(
        clientId: String,
        clientSecret: String,
        botUsername: String?,
    ): ApiResult<Unit>

    /** Save the optional Spotify app credentials. */
    suspend fun saveSpotifyCredentials(clientId: String, clientSecret: String): ApiResult<Unit>

    /** Save the optional YouTube app credentials. */
    suspend fun saveYouTubeCredentials(clientId: String, clientSecret: String): ApiResult<Unit>

    /** Save the optional Discord app credentials. */
    suspend fun saveDiscordCredentials(clientId: String, clientSecret: String): ApiResult<Unit>

    /** The authorize URL to open for the platform bot account; the callback vaults the token server-side. */
    suspend fun botOAuthUrl(): ApiResult<BotOAuthUrl>

    /** The authoritative platform-bot connection status — confirms the bot step after the dance. */
    suspend fun botStatus(): ApiResult<BotStatus>

    /** Finalize first-run setup, locking the credential endpoints to platform admins thereafter. */
    suspend fun completeSetup(): ApiResult<Unit>
}

class RestSystemApi(private val client: ApiClient) : SystemApi {

    override suspend fun status(): ApiResult<SystemStatus> =
        client.getEnvelope("api/v1/system/status")

    override suspend fun wizard(): ApiResult<SetupWizard> =
        client.getEnvelope("api/v1/system/setup/wizard")

    override suspend fun saveTwitchCredentials(
        clientId: String,
        clientSecret: String,
        botUsername: String?,
    ): ApiResult<Unit> =
        client.putUnit(
            "api/v1/system/setup/credentials/twitch",
            SaveTwitchCredentialsBody(
                clientId = clientId,
                clientSecret = clientSecret,
                botUsername = botUsername?.ifBlank { null },
            ),
        )

    override suspend fun saveSpotifyCredentials(
        clientId: String,
        clientSecret: String,
    ): ApiResult<Unit> = saveGenericCredentials("spotify", clientId, clientSecret)

    override suspend fun saveYouTubeCredentials(
        clientId: String,
        clientSecret: String,
    ): ApiResult<Unit> = saveGenericCredentials("youtube", clientId, clientSecret)

    override suspend fun saveDiscordCredentials(
        clientId: String,
        clientSecret: String,
    ): ApiResult<Unit> = saveGenericCredentials("discord", clientId, clientSecret)

    private suspend fun saveGenericCredentials(
        provider: String,
        clientId: String,
        clientSecret: String,
    ): ApiResult<Unit> =
        client.putUnit(
            "api/v1/system/setup/credentials/$provider",
            SaveCredentialsBody(clientId = clientId, clientSecret = clientSecret),
        )

    override suspend fun botOAuthUrl(): ApiResult<BotOAuthUrl> =
        client.getEnvelope("api/v1/system/setup/bot/oauth-url")

    override suspend fun botStatus(): ApiResult<BotStatus> =
        client.getEnvelope("api/v1/system/setup/bot/status")

    override suspend fun completeSetup(): ApiResult<Unit> =
        client.postUnit("api/v1/system/setup/complete")
}
