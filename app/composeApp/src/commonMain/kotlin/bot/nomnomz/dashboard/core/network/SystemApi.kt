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
//   GET  /api/v1/system/status                          → StatusResponseDto<SystemStatusDto>
//   GET  /api/v1/system/setup/wizard                    → StatusResponseDto<SetupWizardDto>
//   PUT  /api/v1/system/setup/credentials/twitch        (clientId, clientSecret, botUsername?)
//   PUT  /api/v1/system/setup/credentials/{provider}    (clientId, clientSecret) — spotify/discord/youtube
//                                                          today; a future login platform (kick/twitter/…)
//                                                          registers the SAME shape, no new client code.
//   GET  /api/v1/system/setup/bot/oauth-url             → StatusResponseDto<{ oauthUrl }>
//   GET  /api/v1/system/setup/bot/status                → StatusResponseDto<BotStatusDto>
//   POST /api/v1/system/setup/complete
interface SystemApi {
    /** System readiness — the [SystemStatus.onboardingComplete] gate the app routes onboarding vs. login off. */
    suspend fun status(): ApiResult<SystemStatus>

    /** The self-describing wizard the UI renders the whole first-run flow from. */
    suspend fun wizard(): ApiResult<SetupWizard>

    /** Save the platform Twitch app credentials (Client ID/Secret + optional bot username) — the one step with
     * a shape of its own (secret-optional, extra bot-username field). */
    suspend fun saveTwitchCredentials(
        clientId: String,
        clientSecret: String,
        botUsername: String?,
    ): ApiResult<Unit>

    /**
     * Save a generic provider's app credentials by its step [provider] key (`spotify`, `discord`, `youtube`
     * today). PUTs to `/api/v1/system/setup/credentials/{provider}` — the same route shape every
     * `save_credentials` step in the wizard uses, so a NEW step (a future kick/twitter/youtube LOGIN
     * platform's app-credential step) needs no new method here: the backend adding the step to its wizard
     * response is enough for the client to render and save it.
     */
    suspend fun saveCredentials(provider: String, clientId: String, clientSecret: String): ApiResult<Unit>

    /**
     * Record the operator's explicit decision to use the shared NomNomzBot public Twitch app instead of BYOC —
     * one click, no fields. POSTs `/api/v1/system/setup/credentials/twitch/use-shared`. This is what completes
     * the Twitch step (and onboarding) on a fresh install that never saves its own credentials: the shipped
     * client id resolving on its own is never enough — a deliberate choice is required.
     */
    suspend fun useSharedTwitchApp(): ApiResult<Unit>

    /** The authorize URL to open for the platform bot account; the callback vaults the token server-side. */
    suspend fun botOAuthUrl(): ApiResult<BotOAuthUrl>

    /** The authoritative platform-bot connection status — confirms the bot step after the dance. */
    suspend fun botStatus(): ApiResult<BotStatus>

    /** Finalize first-run setup, locking the credential endpoints to platform admins thereafter. */
    suspend fun completeSetup(): ApiResult<Unit>

    /** The full pronoun catalogue; anonymous — available before login (for Me screen pronoun picker). */
    suspend fun pronouns(): ApiResult<List<PronounOption>>
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

    override suspend fun saveCredentials(
        provider: String,
        clientId: String,
        clientSecret: String,
    ): ApiResult<Unit> =
        client.putUnit(
            "api/v1/system/setup/credentials/$provider",
            SaveCredentialsBody(clientId = clientId, clientSecret = clientSecret),
        )

    override suspend fun useSharedTwitchApp(): ApiResult<Unit> =
        client.postUnit("api/v1/system/setup/credentials/twitch/use-shared")

    override suspend fun botOAuthUrl(): ApiResult<BotOAuthUrl> =
        client.getEnvelope("api/v1/system/setup/bot/oauth-url")

    override suspend fun botStatus(): ApiResult<BotStatus> =
        client.getEnvelope("api/v1/system/setup/bot/status")

    override suspend fun completeSetup(): ApiResult<Unit> =
        client.postUnit("api/v1/system/setup/complete")

    override suspend fun pronouns(): ApiResult<List<PronounOption>> =
        client.getEnvelope("api/v1/system/pronouns")
}
