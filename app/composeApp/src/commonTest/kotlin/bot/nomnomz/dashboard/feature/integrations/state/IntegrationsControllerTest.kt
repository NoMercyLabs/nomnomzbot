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
import bot.nomnomz.dashboard.core.network.AuthApi
import bot.nomnomz.dashboard.core.network.AuthPayload
import bot.nomnomz.dashboard.core.network.BotAuthApi
import bot.nomnomz.dashboard.core.network.BotStatus
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.CurrentUser
import bot.nomnomz.dashboard.core.network.DeviceBotPoll
import bot.nomnomz.dashboard.core.network.DeviceCodeStart
import bot.nomnomz.dashboard.core.network.DeviceLoginPoll
import bot.nomnomz.dashboard.core.network.IntegrationStatus
import bot.nomnomz.dashboard.core.network.IntegrationsApi
import bot.nomnomz.dashboard.core.network.MissingScope
import bot.nomnomz.dashboard.core.network.MissingScopes
import bot.nomnomz.dashboard.core.network.OAuthStart
import bot.nomnomz.dashboard.core.network.ScopeRegrantStart
import bot.nomnomz.dashboard.core.network.SetupWizard
import bot.nomnomz.dashboard.core.network.SystemApi
import bot.nomnomz.dashboard.core.network.SystemCheck
import bot.nomnomz.dashboard.core.network.SystemChecks
import bot.nomnomz.dashboard.core.network.SystemStatus
import bot.nomnomz.dashboard.core.network.TwitchDiagnosticsApi
import bot.nomnomz.dashboard.core.network.BotOAuthUrl
import bot.nomnomz.dashboard.core.network.EventSubSubscription
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
        diagnostics: TwitchDiagnosticsApi = FakeTwitchDiagnosticsApi(),
        auth: AuthApi = FakeAuthApi(),
        // Whether the backend reports a Twitch client SECRET configured (twitchApp.ok) — drives the bot
        // connect's redirect-vs-device choice. Defaults to secret configured so the redirect-path tests
        // (the prior behavior) keep passing without each restating it.
        system: SystemApi = FakeSystemApi(twitchSecretConfigured = true),
    ): IntegrationsController {
        val session = SessionStore(FakeVault())
        return IntegrationsController(
            session,
            channels,
            bot,
            integrations,
            launcher,
            diagnostics,
            auth,
            system,
        )
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
    fun disconnect_bot_calls_the_backend_then_reflects_the_disconnect() = runTest {
        val bot = FakeBotAuthApi(status = BotStatus(connected = true, displayName = "NomNomzBot"))
        val controller =
            controller(
                channels = FakeChannelsApi(ApiResult.Ok(channel)),
                bot = bot,
                integrations = FakeIntegrationsApi(emptyList()),
                launcher = FakeConnectLauncher(),
            )
        controller.load()
        assertTrue((controller.state.value as IntegrationsState.Ready).bot.connected)

        controller.disconnectBot()

        // The backend disconnect ran, and the post-disconnect re-read shows it (no optimistic flip).
        assertTrue(bot.disconnectCalled)
        assertEquals(false, (controller.state.value as IntegrationsState.Ready).bot.connected)
    }

    @Test
    fun connect_bot_uses_the_redirect_flow_when_a_secret_is_configured() = runTest {
        val bot = FakeBotAuthApi(status = BotStatus(connected = false), authorizeUrl = "https://id.twitch.tv/authorize?bot")
        val launcher = FakeConnectLauncher()
        val controller =
            controller(
                channels = FakeChannelsApi(ApiResult.Ok(channel)),
                bot = bot,
                integrations = FakeIntegrationsApi(emptyList()),
                launcher = launcher,
                // A secret is configured ⇒ the one-tap redirect bot flow.
                system = FakeSystemApi(twitchSecretConfigured = true),
            )
        controller.load()
        assertFalse((controller.state.value as IntegrationsState.Ready).bot.connected)

        // The backend flips the bot to connected once the dance completes (the loopback signal carries no
        // token — the connection is proven by the status re-read).
        bot.status = BotStatus(connected = true, displayName = "NomNomzBot")
        controller.connectBot()

        // The redirect path ran: the launcher opened the exact URL the backend issued; no device login started.
        assertEquals("https://id.twitch.tv/authorize?bot", launcher.openedUrl)
        assertEquals("http://127.0.0.1:5757/cb", bot.startedWithRedirect)
        assertFalse(bot.deviceStartCalled)
        // The row now reflects the backend's connected status (re-read, not optimistic).
        val ready: IntegrationsState.Ready = controller.state.value as IntegrationsState.Ready
        assertTrue(ready.bot.connected)
        assertEquals("NomNomzBot", ready.bot.accountName)
        assertNull(ready.busy)
        assertNull(ready.botDevice)
    }

    @Test
    fun connect_bot_uses_the_device_flow_when_no_secret_is_configured() = runTest {
        // No secret ⇒ the redirect bot URL would 400, so the bot connects via the secret-free device flow:
        // mint a code, surface it, poll until the operator approves, then re-read the connected status.
        val bot =
            FakeBotAuthApi(
                status = BotStatus(connected = false),
                devicePollStatuses = listOf("pending", "authorized"),
            )
        val launcher = FakeConnectLauncher()
        val controller =
            controller(
                channels = FakeChannelsApi(ApiResult.Ok(channel)),
                bot = bot,
                integrations = FakeIntegrationsApi(emptyList()),
                launcher = launcher,
                system = FakeSystemApi(twitchSecretConfigured = false),
            )
        controller.load()
        assertFalse((controller.state.value as IntegrationsState.Ready).bot.connected)

        // The backend connects the shared bot server-side once the device login is approved (status re-read).
        bot.status = BotStatus(connected = true, displayName = "NomNomzBot")
        controller.connectBot()

        // The DEVICE path ran (a code was minted + polled), and the redirect launcher was never opened.
        assertTrue(bot.deviceStartCalled)
        assertEquals("BOT-DEV-1", bot.polledDeviceCode)
        assertNull(launcher.openedUrl)
        assertNull(bot.startedWithRedirect)
        // On approval the panel is dismissed and the row reflects the backend's connected status (re-read).
        val ready: IntegrationsState.Ready = controller.state.value as IntegrationsState.Ready
        assertNull(ready.botDevice)
        assertTrue(ready.bot.connected)
        assertEquals("NomNomzBot", ready.bot.accountName)
    }

    @Test
    fun connect_bot_via_device_surfaces_the_user_code_for_the_operator_to_approve() = runTest {
        // While the operator approves, the screen shows the user code + verify URL — the panel data the UI
        // renders, proving the device login is surfaced, not silent. The fake records the LIVE panel state at
        // the moment of the first poll (mid-flow), then authorizes so the loop ends deterministically.
        var panelAtFirstPoll: BotDeviceState? = null
        val bot =
            FakeBotAuthApi(
                status = BotStatus(connected = true, displayName = "NomNomzBot"),
                deviceStart =
                    ApiResult.Ok(
                        DeviceCodeStart(
                            deviceCode = "BOT-DEV-9",
                            userCode = "ABCD-1234",
                            verificationUri = "https://www.twitch.tv/activate",
                            interval = 1,
                            expiresIn = 600,
                        )
                    ),
                devicePollStatuses = listOf("authorized"),
            )
        val controller =
            controller(
                channels = FakeChannelsApi(ApiResult.Ok(channel)),
                bot = bot,
                integrations = FakeIntegrationsApi(emptyList()),
                launcher = FakeConnectLauncher(),
                system = FakeSystemApi(twitchSecretConfigured = false),
            )
        controller.load()
        // Capture the panel the controller published, observed from inside the very first poll (mid-flow).
        bot.onPoll = { panelAtFirstPoll = (controller.state.value as? IntegrationsState.Ready)?.botDevice }

        controller.connectBotViaDevice()

        // The panel WAS live while awaiting approval, carrying the exact code + verify URL the screen renders.
        assertEquals("ABCD-1234", panelAtFirstPoll?.userCode)
        assertEquals("https://www.twitch.tv/activate", panelAtFirstPoll?.verificationUri)
        // After approval the panel is dismissed and the bot reflects the re-read connected status.
        val ready: IntegrationsState.Ready = controller.state.value as IntegrationsState.Ready
        assertNull(ready.botDevice)
        assertTrue(ready.bot.connected)
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

    @Test
    fun client_registered_reflects_the_backend_per_provider_check() = runTest {
        // Spotify's client IS registered server-side; Discord's is NOT; YouTube has no check field at all.
        val controller =
            controller(
                channels = FakeChannelsApi(ApiResult.Ok(channel)),
                bot = FakeBotAuthApi(BotStatus(connected = false)),
                integrations = FakeIntegrationsApi(emptyList()),
                launcher = FakeConnectLauncher(),
                system =
                    FakeSystemApi(
                        twitchSecretConfigured = true,
                        spotifyClientConfigured = true,
                        discordClientConfigured = false,
                    ),
            )
        controller.load()

        // Spotify → registered (proceed straight to OAuth); Discord → not registered (collect credentials);
        // YouTube → unknown (no backend check), so the flow treats it like "not registered" too.
        assertEquals(true, controller.clientRegistered("spotify"))
        assertEquals(false, controller.clientRegistered("discord"))
        assertNull(controller.clientRegistered("youtube"))
    }

    @Test
    fun save_provider_credentials_registers_the_client_then_flips_registered() = runTest {
        val system =
            FakeSystemApi(
                twitchSecretConfigured = true,
                discordClientConfigured = false, // Discord client not registered yet.
            )
        val controller =
            controller(
                channels = FakeChannelsApi(ApiResult.Ok(channel)),
                bot = FakeBotAuthApi(BotStatus(connected = false)),
                integrations = FakeIntegrationsApi(emptyList()),
                launcher = FakeConnectLauncher(),
                system = system,
            )
        controller.load()
        // Precondition: the client isn't registered, so a connect would route through the credential step.
        assertEquals(false, controller.clientRegistered("discord"))

        val saved: Boolean =
            controller.saveProviderCredentials("discord", clientId = "disc-id", clientSecret = "disc-secret")

        // The save succeeded and hit the RIGHT endpoint with the typed credentials (the side effect that
        // registers the operator's own client) — not the Spotify/YouTube ones.
        assertTrue(saved)
        assertEquals("disc-id" to "disc-secret", system.savedDiscord)
        assertNull(system.savedSpotify)
        assertNull(system.savedYouTube)
        // The post-save status re-read now reports the client registered (proven by the re-read, not a flip).
        assertEquals(true, controller.clientRegistered("discord"))
    }

    @Test
    fun save_provider_credentials_returns_false_and_keeps_unregistered_on_backend_failure() = runTest {
        val system =
            FakeSystemApi(twitchSecretConfigured = true, spotifyClientConfigured = false, saveSucceeds = false)
        val controller =
            controller(
                channels = FakeChannelsApi(ApiResult.Ok(channel)),
                bot = FakeBotAuthApi(BotStatus(connected = false)),
                integrations = FakeIntegrationsApi(emptyList()),
                launcher = FakeConnectLauncher(),
                system = system,
            )
        controller.load()

        val saved: Boolean =
            controller.saveProviderCredentials("spotify", clientId = "id", clientSecret = "secret")

        // A backend rejection returns false (the host stays on the credential step), records no save, and the
        // client stays unregistered — the flow never proceeds to an OAuth it can't complete.
        assertFalse(saved)
        assertNull(system.savedSpotify)
        assertEquals(false, controller.clientRegistered("spotify"))
    }

    @Test
    fun save_provider_credentials_rejects_a_blank_client_id() = runTest {
        val system = FakeSystemApi(twitchSecretConfigured = true)
        val controller =
            controller(
                channels = FakeChannelsApi(ApiResult.Ok(channel)),
                bot = FakeBotAuthApi(BotStatus(connected = false)),
                integrations = FakeIntegrationsApi(emptyList()),
                launcher = FakeConnectLauncher(),
                system = system,
            )
        controller.load()

        val saved: Boolean = controller.saveProviderCredentials("spotify", clientId = "   ", clientSecret = "s")

        // A blank id never reaches the backend (client-side guard) and reports failure.
        assertFalse(saved)
        assertNull(system.savedSpotify)
    }

    @Test
    fun load_surfaces_the_missing_twitch_scopes_into_the_ready_state() = runTest {
        val diagnostics =
            FakeTwitchDiagnosticsApi(
                missing =
                    MissingScopes(
                        connectionStatus = "connected",
                        scopes =
                            listOf(
                                MissingScope("moderator:read:followers", listOf("followers")),
                                MissingScope("channel:read:subscriptions", listOf("subscriptions")),
                            ),
                    )
            )
        val controller =
            controller(
                channels = FakeChannelsApi(ApiResult.Ok(channel)),
                bot = FakeBotAuthApi(BotStatus(connected = true)),
                integrations = FakeIntegrationsApi(emptyList()),
                launcher = FakeConnectLauncher(),
                diagnostics = diagnostics,
            )

        controller.load()

        val ready: IntegrationsState.Ready = controller.state.value as IntegrationsState.Ready
        // The screen renders the gaps the backend reported — the banner's data, not a guess.
        assertEquals(2, ready.missingScopes.size)
        assertEquals("moderator:read:followers", ready.missingScopes[0].scope)
        assertEquals("subscriptions", ready.missingScopes[1].features.first())
        assertNull(ready.regrant)
    }

    @Test
    fun regrant_shows_the_device_code_then_clears_the_gaps_once_authorized() = runTest {
        val diagnostics =
            FakeTwitchDiagnosticsApi(
                missing =
                    MissingScopes(
                        scopes = listOf(MissingScope("channel:read:subscriptions", listOf("subscriptions")))
                    ),
                regrant =
                    ScopeRegrantStart(
                        deviceCode = "DEV-123",
                        userCode = "WXYZ-9876",
                        verificationUri = "https://twitch.tv/activate",
                        interval = 0, // poll immediately in the test
                        expiresIn = 60,
                        requestedScopes =
                            listOf("moderator:read:followers", "channel:read:subscriptions"),
                    ),
            )
        // The streamer approves on the first poll → authorized.
        val auth = FakeAuthApi(pollStatuses = listOf("authorized"))
        val controller =
            controller(
                channels = FakeChannelsApi(ApiResult.Ok(channel)),
                bot = FakeBotAuthApi(BotStatus(connected = true)),
                integrations = FakeIntegrationsApi(emptyList()),
                launcher = FakeConnectLauncher(),
                diagnostics = diagnostics,
                auth = auth,
            )
        controller.load()
        assertEquals(1, (controller.state.value as IntegrationsState.Ready).missingScopes.size)

        // The backend clears the gap once the widened grant is approved + reconciled (status re-read).
        diagnostics.missingAfter = MissingScopes(scopes = emptyList())
        controller.regrantScopes()

        val ready: IntegrationsState.Ready = controller.state.value as IntegrationsState.Ready
        // The re-grant requested the UNION (existing followers scope kept + the missing subscriptions scope).
        assertEquals(
            listOf("moderator:read:followers", "channel:read:subscriptions"),
            diagnostics.lastRegrantRequestedScopes,
        )
        // After approval the panel is dismissed and the banner data is gone (re-read, not optimistic).
        assertNull(ready.regrant)
        assertTrue(ready.missingScopes.isEmpty())
        // The streamer device poll was actually driven with the issued device code.
        assertEquals("DEV-123", auth.polledDeviceCode)
    }

    @Test
    fun regrant_is_a_no_op_when_the_backend_reports_nothing_to_grant() = runTest {
        val diagnostics = FakeTwitchDiagnosticsApi(regrantFails = true)
        val controller =
            controller(
                channels = FakeChannelsApi(ApiResult.Ok(channel)),
                bot = FakeBotAuthApi(BotStatus(connected = true)),
                integrations = FakeIntegrationsApi(emptyList()),
                launcher = FakeConnectLauncher(),
                diagnostics = diagnostics,
            )
        controller.load()

        controller.regrantScopes()

        // No device-code panel is shown when there is nothing to grant.
        assertNull((controller.state.value as IntegrationsState.Ready).regrant)
    }
}

private fun List<ProviderConnection>.row(provider: String): ProviderConnection =
    first { it.provider.equals(provider, ignoreCase = true) }

// ── Fakes ─────────────────────────────────────────────────────────────────────

private class FakeChannelsApi(private val result: ApiResult<ChannelSummary>) : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = result

    override suspend fun list(): ApiResult<List<ChannelSummary>> = ApiResult.Ok(emptyList())

    override suspend fun join(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun leave(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun reset(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun deleteChannel(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun channelScopes(channelId: String) = error("stub")
    override suspend fun startChannelBotConnect(channelId: String) = error("stub")
    override suspend fun channelBotStatus(channelId: String) = error("stub")
    override suspend fun disconnectChannelBot(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun moderatedChannels(): ApiResult<List<ModeratedChannel>> = ApiResult.Ok(emptyList())
}

private class FakeBotAuthApi(
    var status: BotStatus,
    private val authorizeUrl: String = "https://id.twitch.tv/authorize?bot",
    // The device login the secret-free path drives: a started code, then the poll statuses to walk through.
    private val deviceStart: ApiResult<DeviceCodeStart> =
        ApiResult.Ok(
            DeviceCodeStart(
                deviceCode = "BOT-DEV-1",
                userCode = "WXYZ-7890",
                verificationUri = "https://www.twitch.tv/activate",
                interval = 1,
                expiresIn = 60,
            )
        ),
    private val devicePollStatuses: List<String> = listOf("authorized"),
) : BotAuthApi {
    var startedWithRedirect: String? = null
    var deviceStartCalled: Boolean = false
    var polledDeviceCode: String? = null
    // Invoked at the start of each poll, so a test can observe the controller's live panel state mid-flow.
    var onPoll: (() -> Unit)? = null
    private var pollIndex: Int = 0

    override suspend fun start(loopbackRedirect: String): ApiResult<OAuthStart> {
        startedWithRedirect = loopbackRedirect
        return ApiResult.Ok(OAuthStart(authorizeUrl = authorizeUrl, state = "state-nonce"))
    }

    override suspend fun startDeviceLogin(): ApiResult<DeviceCodeStart> {
        deviceStartCalled = true
        return deviceStart
    }

    override suspend fun pollDeviceLogin(deviceCode: String): ApiResult<DeviceBotPoll> {
        onPoll?.invoke()
        polledDeviceCode = deviceCode
        val pollStatus: String = devicePollStatuses.getOrElse(pollIndex) { "pending" }
        pollIndex++
        val bot: BotStatus? =
            if (pollStatus == "authorized") BotStatus(connected = true, displayName = "NomNomzBot")
            else null
        return ApiResult.Ok(DeviceBotPoll(status = pollStatus, bot = bot))
    }

    override suspend fun status(): ApiResult<BotStatus> = ApiResult.Ok(status)

    var disconnectCalled: Boolean = false

    override suspend fun disconnect(): ApiResult<Unit> {
        disconnectCalled = true
        status = BotStatus(connected = false) // the refresh() re-read then reflects the disconnect.
        return ApiResult.Ok(Unit)
    }
}

// A configurable [SystemApi] for the integrations tests. [status] drives the bot-connect method choice
// (twitchApp.ok) AND the per-provider app-client registration signal the register-then-login flow reads
// (spotify/discord checks). It records the credential saves so the flow's side effect — the operator's own
// client being registered through the right endpoint — can be asserted. A save flips the matching provider
// check to ok (modeling the backend now reporting the client configured), so the post-save re-read proves the
// registration via the status re-read, not an optimistic flip.
private class FakeSystemApi(
    private val twitchSecretConfigured: Boolean,
    spotifyClientConfigured: Boolean = false,
    discordClientConfigured: Boolean = false,
    private val saveSucceeds: Boolean = true,
) : SystemApi {
    private var spotifyOk: Boolean = spotifyClientConfigured
    private var discordOk: Boolean = discordClientConfigured

    var savedSpotify: Pair<String, String>? = null
    var savedYouTube: Pair<String, String>? = null
    var savedDiscord: Pair<String, String>? = null

    override suspend fun status(): ApiResult<SystemStatus> =
        ApiResult.Ok(
            SystemStatus(
                ready = true,
                checks =
                    SystemChecks(
                        twitchApp =
                            SystemCheck(
                                ok = twitchSecretConfigured,
                                ready = true,
                                status = if (twitchSecretConfigured) "ready_redirect" else "ready_device",
                            ),
                        platformBot = SystemCheck(ok = true, ready = true, status = "connected"),
                        spotify = SystemCheck(ok = spotifyOk, ready = spotifyOk, status = if (spotifyOk) "configured" else "missing"),
                        discord = SystemCheck(ok = discordOk, ready = discordOk, status = if (discordOk) "configured" else "missing"),
                    ),
            )
        )

    override suspend fun wizard(): ApiResult<SetupWizard> = ApiResult.Ok(SetupWizard(complete = true))

    override suspend fun saveTwitchCredentials(
        clientId: String,
        clientSecret: String,
        botUsername: String?,
    ): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun saveSpotifyCredentials(clientId: String, clientSecret: String): ApiResult<Unit> {
        if (!saveSucceeds) return ApiResult.Failure(ApiError(403, "FORBIDDEN", "Not allowed."))
        savedSpotify = clientId to clientSecret
        spotifyOk = true
        return ApiResult.Ok(Unit)
    }

    override suspend fun saveYouTubeCredentials(clientId: String, clientSecret: String): ApiResult<Unit> {
        if (!saveSucceeds) return ApiResult.Failure(ApiError(403, "FORBIDDEN", "Not allowed."))
        savedYouTube = clientId to clientSecret
        return ApiResult.Ok(Unit)
    }

    override suspend fun saveDiscordCredentials(clientId: String, clientSecret: String): ApiResult<Unit> {
        if (!saveSucceeds) return ApiResult.Failure(ApiError(403, "FORBIDDEN", "Not allowed."))
        savedDiscord = clientId to clientSecret
        discordOk = true
        return ApiResult.Ok(Unit)
    }

    override suspend fun botOAuthUrl(): ApiResult<BotOAuthUrl> =
        ApiResult.Ok(BotOAuthUrl("https://id.twitch.tv/authorize?bot"))

    override suspend fun botStatus(): ApiResult<BotStatus> = ApiResult.Ok(BotStatus(connected = true))

    override suspend fun completeSetup(): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun pronouns(): ApiResult<List<bot.nomnomz.dashboard.core.network.PronounOption>> = ApiResult.Ok(emptyList())
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

    override suspend fun authorizeProvider(
        baseUrl: String,
        providerKey: String,
    ): ApiResult<bot.nomnomz.dashboard.core.connection.SessionTokens> =
        ApiResult.Failure(
            bot.nomnomz.dashboard.core.network.ApiError(0, "UNUSED", "provider login not used here")
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

private class FakeTwitchDiagnosticsApi(
    private val missing: MissingScopes = MissingScopes(scopes = emptyList()),
    private val regrant: ScopeRegrantStart? = null,
    private val regrantFails: Boolean = false,
) : TwitchDiagnosticsApi {
    // `missingAfter` lets a test model the backend clearing the gaps between the start and the post-approval
    // re-read, so the banner clearing is proven by the re-read (not an optimistic local flip).
    var missingAfter: MissingScopes? = null
    var lastRegrantRequestedScopes: List<String>? = null

    override suspend fun missingScopes(): ApiResult<MissingScopes> = ApiResult.Ok(missingAfter ?: missing)

    override suspend fun startRegrant(): ApiResult<ScopeRegrantStart> {
        if (regrantFails || regrant == null)
            return ApiResult.Failure(ApiError(409, "NO_MISSING_SCOPES", "Nothing to grant."))
        lastRegrantRequestedScopes = regrant.requestedScopes
        return ApiResult.Ok(regrant)
    }

    override suspend fun subscriptions(channelId: String): ApiResult<List<EventSubSubscription>> = ApiResult.Ok(emptyList())
    override suspend fun reconcile(channelId: String) = error("stub")
}

private class FakeAuthApi(private val pollStatuses: List<String> = emptyList()) : AuthApi {
    var polledDeviceCode: String? = null
    private var pollIndex: Int = 0

    override suspend fun providers():
        ApiResult<List<bot.nomnomz.dashboard.core.network.LoginProvider>> = ApiResult.Ok(emptyList())

    override suspend fun me(): ApiResult<CurrentUser> =
        ApiResult.Failure(ApiError(0, "UNUSED", "not used here"))

    override suspend fun startDeviceLogin(
        provider: String
    ): ApiResult<bot.nomnomz.dashboard.core.network.DeviceCodeStart> =
        ApiResult.Failure(ApiError(0, "UNUSED", "not used here"))

    override suspend fun pollDeviceLogin(
        provider: String,
        deviceCode: String,
    ): ApiResult<DeviceLoginPoll> {
        polledDeviceCode = deviceCode
        val status: String = pollStatuses.getOrElse(pollIndex) { "pending" }
        pollIndex++
        val auth: AuthPayload? =
            if (status == "authorized") AuthPayload(accessToken = "a", refreshToken = "r", expiresIn = 3600L)
            else null
        return ApiResult.Ok(DeviceLoginPoll(status = status, auth = auth))
    }

    override suspend fun refresh(refreshToken: String?): ApiResult<AuthPayload> =
        ApiResult.Failure(ApiError(0, "UNUSED", "not used here"))
}
