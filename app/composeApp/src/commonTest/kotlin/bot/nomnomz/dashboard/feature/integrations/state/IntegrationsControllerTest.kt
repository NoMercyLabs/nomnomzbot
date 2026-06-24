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
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.BotAuthApi
import bot.nomnomz.dashboard.core.network.BotStatus
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.IntegrationStatus
import bot.nomnomz.dashboard.core.network.IntegrationsApi
import bot.nomnomz.dashboard.core.network.OAuthStart
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// Proves the integrations onboarding state machine — the behavior the screen renders and the connect
// flows the user drives. These assert the resulting STATE and the SIDE EFFECTS (which authorize URL the
// launcher was asked to open, that the backend status is re-read after a connect), not surface calls.
// The token always lands server-side, so a connect is proven by the status RE-READ flipping the row to
// connected — never by an optimistic local flip.
class IntegrationsControllerTest {

    private val channel = ChannelSummary(id = "chan-guid-1", login = "stoney_eagle", displayName = "Stoney_Eagle")

    private fun controller(
        channels: ChannelsApi,
        bot: BotAuthApi,
        integrations: IntegrationsApi,
        launcher: ConnectLauncher,
    ): IntegrationsController {
        val session = SessionStore(FakeVault())
        return IntegrationsController(session, channels, bot, integrations, launcher)
    }

    @Test
    fun load_maps_bot_and_provider_statuses_into_the_ready_state() = runTest {
        val controller =
            controller(
                channels = FakeChannelsApi(ApiResult.Ok(channel)),
                bot = FakeBotAuthApi(status = BotStatus(connected = true, displayName = "NomNomzBot")),
                integrations =
                    FakeIntegrationsApi(
                        status =
                            listOf(
                                IntegrationStatus("spotify", connected = true, accountName = "stoney"),
                                IntegrationStatus("youtube", connected = false),
                                IntegrationStatus("discord", connected = true, accountName = "My Server"),
                            )
                    ),
                launcher = FakeConnectLauncher(),
            )

        controller.load()

        val ready: IntegrationsState.Ready = controller.state.value as IntegrationsState.Ready
        // Bot row reflects the backend status, display name preferred over login.
        assertTrue(ready.bot.connected)
        assertEquals("NomNomzBot", ready.bot.accountName)
        // Each provider row carries the backend's connected flag + account.
        assertEquals(true, ready.providers.row("spotify").connected)
        assertEquals("stoney", ready.providers.row("spotify").accountName)
        assertEquals(false, ready.providers.row("youtube").connected)
        assertEquals(true, ready.providers.row("discord").connected)
        assertEquals("My Server", ready.providers.row("discord").accountName)
        // No row is mid-operation once loaded.
        assertNull(ready.busy)
    }

    @Test
    fun load_surfaces_an_error_when_no_channel_is_resolved() = runTest {
        val controller =
            controller(
                channels = FakeChannelsApi(ApiResult.Failure(ApiError(404, "NO_CHANNEL", "No channel."))),
                bot = FakeBotAuthApi(BotStatus(connected = false)),
                integrations = FakeIntegrationsApi(emptyList()),
                launcher = FakeConnectLauncher(),
            )

        controller.load()

        val error: IntegrationsState.Error = controller.state.value as IntegrationsState.Error
        assertEquals("No channel.", error.detail)
    }

    @Test
    fun connect_bot_opens_the_backend_authorize_url_then_reflects_connected() = runTest {
        val bot = FakeBotAuthApi(status = BotStatus(connected = false), authorizeUrl = "https://id.twitch.tv/authorize?bot")
        val launcher = FakeConnectLauncher()
        val controller =
            controller(
                channels = FakeChannelsApi(ApiResult.Ok(channel)),
                bot = bot,
                integrations = FakeIntegrationsApi(emptyList()),
                launcher = launcher,
            )
        controller.load()
        assertFalse((controller.state.value as IntegrationsState.Ready).bot.connected)

        // The backend flips the bot to connected once the dance completes (the loopback signal carries no
        // token — the connection is proven by the status re-read).
        bot.status = BotStatus(connected = true, displayName = "NomNomzBot")
        controller.connectBot()

        // The launcher was driven to open the exact URL the backend issued for the loopback redirect.
        assertEquals("https://id.twitch.tv/authorize?bot", launcher.openedUrl)
        assertEquals("http://127.0.0.1:5757/cb", bot.startedWithRedirect)
        // The row now reflects the backend's connected status (re-read, not optimistic).
        val ready: IntegrationsState.Ready = controller.state.value as IntegrationsState.Ready
        assertTrue(ready.bot.connected)
        assertEquals("NomNomzBot", ready.bot.accountName)
        assertNull(ready.busy)
    }

    @Test
    fun connect_provider_starts_with_the_loopback_return_url_and_reflects_connected() = runTest {
        val integrations =
            FakeIntegrationsApi(
                status = listOf(IntegrationStatus("spotify", connected = false)),
                authorizeUrl = "https://accounts.spotify.com/authorize",
            )
        val launcher = FakeConnectLauncher()
        val controller =
            controller(
                channels = FakeChannelsApi(ApiResult.Ok(channel)),
                bot = FakeBotAuthApi(BotStatus(connected = false)),
                integrations = integrations,
                launcher = launcher,
            )
        controller.load()

        // The provider becomes connected server-side after the vault hop.
        integrations.statusAfter = listOf(IntegrationStatus("spotify", connected = true, accountName = "stoney"))
        controller.connectProvider("spotify", "spotify.playback")

        // The connect was started for the right channel/provider/scope-set, with the loopback as returnUrl.
        assertEquals("chan-guid-1", integrations.startedChannelId)
        assertEquals("spotify", integrations.startedProvider)
        assertEquals("spotify.playback", integrations.startedScopeSet)
        assertEquals("http://127.0.0.1:5757/cb", integrations.startedReturnUrl)
        // The browser was sent to the provider authorize URL the backend returned.
        assertEquals("https://accounts.spotify.com/authorize", launcher.openedUrl)
        // Status re-read flips the row to connected.
        val ready: IntegrationsState.Ready = controller.state.value as IntegrationsState.Ready
        assertTrue(ready.providers.row("spotify").connected)
        assertEquals("stoney", ready.providers.row("spotify").accountName)
    }

    @Test
    fun disconnect_provider_calls_through_and_reflects_disconnected() = runTest {
        val integrations =
            FakeIntegrationsApi(status = listOf(IntegrationStatus("spotify", connected = true, accountName = "stoney")))
        val controller =
            controller(
                channels = FakeChannelsApi(ApiResult.Ok(channel)),
                bot = FakeBotAuthApi(BotStatus(connected = false)),
                integrations = integrations,
                launcher = FakeConnectLauncher(),
            )
        controller.load()
        assertTrue((controller.state.value as IntegrationsState.Ready).providers.row("spotify").connected)

        integrations.statusAfter = listOf(IntegrationStatus("spotify", connected = false))
        controller.disconnect("spotify")

        assertEquals("spotify", integrations.disconnectedGenericProvider)
        assertFalse((controller.state.value as IntegrationsState.Ready).providers.row("spotify").connected)
    }
}

private fun List<ProviderConnection>.row(provider: String): ProviderConnection =
    first { it.provider.equals(provider, ignoreCase = true) }

// ── Fakes ─────────────────────────────────────────────────────────────────────

private class FakeChannelsApi(private val result: ApiResult<ChannelSummary>) : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = result
}

private class FakeBotAuthApi(
    var status: BotStatus,
    private val authorizeUrl: String = "https://id.twitch.tv/authorize?bot",
) : BotAuthApi {
    var startedWithRedirect: String? = null

    override suspend fun start(loopbackRedirect: String): ApiResult<OAuthStart> {
        startedWithRedirect = loopbackRedirect
        return ApiResult.Ok(OAuthStart(authorizeUrl = authorizeUrl, state = "state-nonce"))
    }

    override suspend fun status(): ApiResult<BotStatus> = ApiResult.Ok(status)
}

private class FakeIntegrationsApi(
    status: List<IntegrationStatus>,
    private val authorizeUrl: String = "https://provider/authorize",
) : IntegrationsApi {
    // `statusAfter` lets a test model the backend connecting/disconnecting between the initial load and
    // the post-action refresh, so the re-read (not an optimistic flip) is what changes the row.
    private val initial: List<IntegrationStatus> = status
    var statusAfter: List<IntegrationStatus>? = null

    var startedChannelId: String? = null
    var startedProvider: String? = null
    var startedScopeSet: String? = null
    var startedReturnUrl: String? = null
    var disconnectedGenericProvider: String? = null
    var disconnectedDiscord: Boolean = false

    override suspend fun status(channelId: String): ApiResult<List<IntegrationStatus>> =
        ApiResult.Ok(statusAfter ?: initial)

    override suspend fun startGenericConnect(
        channelId: String,
        provider: String,
        scopeSetKey: String,
        returnUrl: String?,
    ): ApiResult<OAuthStart> {
        startedChannelId = channelId
        startedProvider = provider
        startedScopeSet = scopeSetKey
        startedReturnUrl = returnUrl
        return ApiResult.Ok(OAuthStart(authorizeUrl = authorizeUrl, state = "state-nonce"))
    }

    override fun discordStartUrl(baseUrl: String, channelId: String): String =
        "$baseUrl/api/v1/channels/$channelId/integrations/discord/callback/start"

    override suspend fun disconnectGeneric(channelId: String, provider: String): ApiResult<Unit> {
        disconnectedGenericProvider = provider
        return ApiResult.Ok(Unit)
    }

    override suspend fun disconnectDiscord(channelId: String): ApiResult<Unit> {
        disconnectedDiscord = true
        return ApiResult.Ok(Unit)
    }
}

// Drives the authorize-URL provider with a fixed loopback redirect (as the desktop launcher would) and
// records the URL it was asked to open, so a test can assert the exact URL the browser is sent to.
private class FakeConnectLauncher : ConnectLauncher {
    var openedUrl: String? = null

    override suspend fun authorizeStreamer(
        baseUrl: String
    ): ApiResult<bot.nomnomz.dashboard.core.connection.SessionTokens> =
        ApiResult.Failure(
            bot.nomnomz.dashboard.core.network.ApiError(0, "UNUSED", "streamer login not used here")
        )

    override suspend fun awaitConnect(
        authorizeUrlFor: suspend (redirect: String) -> ApiResult<String>
    ): ApiResult<Unit> =
        when (val url: ApiResult<String> = authorizeUrlFor("http://127.0.0.1:5757/cb")) {
            is ApiResult.Failure -> ApiResult.Failure(url.error)
            is ApiResult.Ok -> {
                openedUrl = url.value
                ApiResult.Ok(Unit)
            }
        }
}

private class FakeVault : bot.nomnomz.dashboard.core.connection.SessionTokenStore {
    override suspend fun read(profileId: String): bot.nomnomz.dashboard.core.connection.SessionTokens? = null

    override suspend fun write(
        profileId: String,
        tokens: bot.nomnomz.dashboard.core.connection.SessionTokens,
    ) = Unit

    override suspend fun clear(profileId: String) = Unit
}
