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

import bot.nomnomz.dashboard.core.connection.ConnectLauncher
import bot.nomnomz.dashboard.core.connection.SessionStore
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.BotAuthApi
import bot.nomnomz.dashboard.core.network.BotStatus
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.IntegrationStatus
import bot.nomnomz.dashboard.core.network.IntegrationsApi
import bot.nomnomz.dashboard.core.network.OAuthStart
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The integrations/onboarding screen's state-holder (frontend.md §4 — a plain holder, not a ViewModel).
// It owns the bot-account connect and the Spotify / YouTube / Discord connects, all against the REAL
// backend. The token always lands SERVER-SIDE, so every connect is: open the backend-issued authorize
// URL → wait for / return from the browser → re-read the authoritative status. Nothing is mocked.
//
// The connects are driven through the shared [OAuthLauncher] seam (desktop loopback / web redirect):
//   - Bot:            backend GET …/auth/twitch/bot returns the authorize URL; the callback signals the
//                     loopback with `bot_connected=true` (no token); we confirm via the bot status read.
//   - Spotify/YouTube: backend POST …/integrations/{p}/connect returns the PROVIDER authorize URL; the
//                     provider→backend callback vaults the token then returns to our redirect; we refresh.
//   - Discord:        backend GET …/integrations/discord/callback/start is opened directly. Its callback
//                     redirects to the configured frontend (it ignores a loopback redirect), so on desktop
//                     the loopback signal never fires — we open the URL and the user refreshes. (Backend
//                     gap: Discord start should accept + honor a loopback redirect like the others.)
class IntegrationsController(
    private val sessionStore: SessionStore,
    private val channelsApi: ChannelsApi,
    private val botAuthApi: BotAuthApi,
    private val integrationsApi: IntegrationsApi,
    private val connectLauncher: ConnectLauncher,
) {
    private val _state: MutableStateFlow<IntegrationsState> =
        MutableStateFlow(IntegrationsState.Loading)

    /** The screen's render state: loading / ready (with per-provider rows) / error. */
    val state: StateFlow<IntegrationsState> = _state.asStateFlow()

    private var channelId: String? = null

    /** Resolve the active channel, then load the bot + integration statuses. */
    suspend fun load() {
        _state.value = IntegrationsState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = IntegrationsState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }
        channelId = channel.id

        refresh()
    }

    /** Re-read every status and rebuild the ready state. Used after any connect/disconnect returns. */
    suspend fun refresh() {
        val id: String = channelId ?: return

        val bot: BotConnection =
            when (val result: ApiResult<BotStatus> = botAuthApi.status()) {
                is ApiResult.Ok -> BotConnection(result.value.connected, result.value.displayName ?: result.value.login)
                is ApiResult.Failure -> BotConnection(connected = false, accountName = null)
            }

        val providers: List<ProviderConnection> =
            when (val result: ApiResult<List<IntegrationStatus>> = integrationsApi.status(id)) {
                is ApiResult.Ok -> result.value.map { it.toProviderConnection() }
                is ApiResult.Failure -> emptyList()
            }

        _state.value = IntegrationsState.Ready(bot = bot, providers = providers, busy = null)
    }

    /** Run the bot-account connect, then refresh. */
    suspend fun connectBot() {
        withBusy(BusyTarget.Bot) {
            connectLauncher.awaitConnect { redirect ->
                botAuthApi.start(redirect).mapToAuthorizeUrl()
            }
        }
    }

    /** Run a Spotify/YouTube connect for [provider] with [scopeSetKey], then refresh. */
    suspend fun connectProvider(provider: String, scopeSetKey: String) {
        val id: String = channelId ?: return
        withBusy(BusyTarget.Provider(provider)) {
            connectLauncher.awaitConnect { redirect ->
                integrationsApi
                    .startGenericConnect(
                        channelId = id,
                        provider = provider,
                        scopeSetKey = scopeSetKey,
                        returnUrl = redirect.ifBlank { null },
                    )
                    .mapToAuthorizeUrl()
            }
        }
    }

    /** Open the Discord connect URL directly (no loopback signal — see class note), then refresh. */
    suspend fun connectDiscord() {
        val id: String = channelId ?: return
        val base: String = sessionStore.baseUrl() ?: return
        withBusy(BusyTarget.Provider("discord")) {
            connectLauncher.awaitConnect { _ ->
                ApiResult.Ok(integrationsApi.discordStartUrl(base, id))
            }
        }
    }

    /** Disconnect a provider (spotify/youtube/discord), then refresh. */
    suspend fun disconnect(provider: String) {
        val id: String = channelId ?: return
        withBusy(BusyTarget.Provider(provider)) {
            if (provider.equals("discord", ignoreCase = true)) integrationsApi.disconnectDiscord(id)
            else integrationsApi.disconnectGeneric(id, provider)
        }
    }

    /**
     * Mark a target busy, run [action], then re-read status regardless of outcome (the authoritative
     * connected state always comes from the backend, never an optimistic local flip — no fakes).
     */
    private suspend fun withBusy(target: BusyTarget, action: suspend () -> ApiResult<*>) {
        val ready: IntegrationsState.Ready = _state.value as? IntegrationsState.Ready ?: return
        _state.value = ready.copy(busy = target)
        action()
        refresh()
    }

    private fun IntegrationStatus.toProviderConnection(): ProviderConnection =
        ProviderConnection(
            provider = provider,
            connected = connected,
            accountName = accountName,
            needsReauth = needsReauth,
        )

    /** Project an [OAuthStart] result to just its authorize URL for the launcher to open. */
    private fun ApiResult<OAuthStart>.mapToAuthorizeUrl(): ApiResult<String> =
        when (this) {
            is ApiResult.Failure -> ApiResult.Failure(error)
            is ApiResult.Ok -> ApiResult.Ok(value.authorizeUrl)
        }
}

/** The integrations screen render state. */
sealed interface IntegrationsState {
    data object Loading : IntegrationsState

    data class Ready(
        val bot: BotConnection,
        val providers: List<ProviderConnection>,
        val busy: BusyTarget?,
    ) : IntegrationsState

    data class Error(val detail: String) : IntegrationsState
}

/** The platform-shared bot account connection, surfaced to the screen. */
data class BotConnection(val connected: Boolean, val accountName: String?)

/** One provider row (Spotify / YouTube / Discord) the screen renders. */
data class ProviderConnection(
    val provider: String,
    val connected: Boolean,
    val accountName: String?,
    val needsReauth: Boolean,
)

/** Which row is mid-operation, so the screen can disable just that row's actions. */
sealed interface BusyTarget {
    data object Bot : BusyTarget

    data class Provider(val provider: String) : BusyTarget
}
