// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.setup.state

import bot.nomnomz.dashboard.core.connection.ConnectLauncher
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.BotOAuthUrl
import bot.nomnomz.dashboard.core.network.BotStatus
import bot.nomnomz.dashboard.core.network.SetupAction
import bot.nomnomz.dashboard.core.network.SetupField
import bot.nomnomz.dashboard.core.network.SetupStep
import bot.nomnomz.dashboard.core.network.SetupWizard
import bot.nomnomz.dashboard.core.network.SystemApi
import bot.nomnomz.dashboard.core.network.SystemCheck
import bot.nomnomz.dashboard.core.network.SystemChecks
import bot.nomnomz.dashboard.core.network.SystemStatus
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// Proves the first-run setup wizard's state machine — the behavior the screen renders and the credential
// saves the user drives. These assert the resulting STATE (the steps rendered from the backend, the ready
// gate) and the SIDE EFFECTS (which credentials were PUT, that the wizard is re-read after a save, that
// the sign-in + complete fire only when ready), not surface calls. A step is "complete" only when the
// re-read wizard says so — never an optimistic local flip; that mirrors the backend being the source.
class SetupControllerTest {

    private fun controller(
        api: FakeSystemApi,
        launcher: ConnectLauncher = FakeConnectLauncher(),
        onReadyToSignIn: suspend () -> Boolean = { true },
    ): SetupController = SetupController(api, launcher, onReadyToSignIn)

    @Test
    fun load_renders_the_backend_steps_and_is_not_ready_when_the_twitch_app_is_missing() = runTest {
        val api = FakeSystemApi(wizard = wizard(twitch = false, bot = false), ready = false)
        val controller = controller(api)

        controller.load()

        val steps: SetupState.Steps = controller.state.value as SetupState.Steps
        // The steps are rendered verbatim from the backend wizard (the self-describing contract).
        assertEquals(listOf("twitch_app", "platform_bot", "spotify"), steps.steps.map { it.key })
        assertFalse(steps.steps.first { it.key == "twitch_app" }.complete)
        // status.ready==false ⇒ the streamer sign-in must NOT be enabled yet (the gap being closed).
        assertFalse(steps.ready)
    }

    @Test
    fun saving_twitch_credentials_puts_them_then_reflects_the_step_complete_and_ready_from_the_reread() = runTest {
        val api = FakeSystemApi(wizard = wizard(twitch = false, bot = true), ready = false)
        val controller = controller(api)
        controller.load()
        assertFalse((controller.state.value as SetupState.Steps).ready)

        val twitch: SetupStep = (controller.state.value as SetupState.Steps).steps.first { it.key == "twitch_app" }
        controller.onFieldChange("twitch_app", "clientId", "abc123")
        controller.onFieldChange("twitch_app", "clientSecret", "s3cr3t")
        controller.onFieldChange("twitch_app", "botUsername", "NomNomzBot")
        // The backend becomes ready once the Twitch app is saved (bot was already authorized).
        api.wizardAfter = wizard(twitch = true, bot = true)
        api.readyAfter = true
        controller.saveCredentials(twitch)

        // The exact credentials were PUT to the backend (the consequence of the action).
        assertEquals("abc123", api.savedTwitchClientId)
        assertEquals("s3cr3t", api.savedTwitchClientSecret)
        assertEquals("NomNomzBot", api.savedTwitchBotUsername)
        // The step now reflects the backend's re-read: complete, and the flow is ready to sign in.
        val steps: SetupState.Steps = controller.state.value as SetupState.Steps
        assertTrue(steps.steps.first { it.key == "twitch_app" }.complete)
        assertTrue(steps.ready)
        assertNull(steps.busy)
        assertNull(steps.error)
    }

    @Test
    fun saving_with_a_blank_required_field_surfaces_an_error_and_does_not_call_the_backend() = runTest {
        val api = FakeSystemApi(wizard = wizard(twitch = false, bot = true), ready = false)
        val controller = controller(api)
        controller.load()
        val twitch: SetupStep = (controller.state.value as SetupState.Steps).steps.first { it.key == "twitch_app" }

        // Only the client id is filled — the secret is blank.
        controller.onFieldChange("twitch_app", "clientId", "abc123")
        controller.saveCredentials(twitch)

        val steps: SetupState.Steps = controller.state.value as SetupState.Steps
        val error: SetupError? = steps.error
        assertTrue(error is SetupError.MissingFields && error.stepKey == "twitch_app")
        // The backend was never called with an incomplete credential pair.
        assertNull(api.savedTwitchClientId)
    }

    @Test
    fun connecting_the_bot_opens_the_backend_oauth_url_then_reflects_the_reread_status() = runTest {
        val api =
            FakeSystemApi(wizard = wizard(twitch = true, bot = false), ready = false, botOAuthUrl = "https://id.twitch.tv/authorize?bot")
        val launcher = FakeConnectLauncher()
        val controller = controller(api, launcher)
        controller.load()
        assertFalse((controller.state.value as SetupState.Steps).steps.first { it.key == "platform_bot" }.complete)

        // The backend marks the bot connected + the system ready once the dance completes.
        api.wizardAfter = wizard(twitch = true, bot = true)
        api.readyAfter = true
        controller.connectBot()

        // The launcher was driven to open the exact bot authorize URL the backend issued.
        assertEquals("https://id.twitch.tv/authorize?bot", launcher.openedUrl)
        // The bot step now reflects the backend re-read (connected), and the flow is ready.
        val steps: SetupState.Steps = controller.state.value as SetupState.Steps
        assertTrue(steps.steps.first { it.key == "platform_bot" }.complete)
        assertTrue(steps.ready)
    }

    @Test
    fun finish_when_ready_runs_the_streamer_signin_then_marks_setup_complete() = runTest {
        val api = FakeSystemApi(wizard = wizard(twitch = true, bot = true), ready = true)
        var signedIn = false
        val controller = controller(api, onReadyToSignIn = {
            signedIn = true
            true
        })
        controller.load()
        assertTrue((controller.state.value as SetupState.Steps).ready)

        controller.finish()

        // The streamer OAuth was handed off (the gate advances to the shell inside the lambda)...
        assertTrue(signedIn)
        // ...and setup was finalized so the credential endpoints lock to admins.
        assertTrue(api.setupCompleted)
    }

    @Test
    fun finish_does_nothing_when_not_ready() = runTest {
        val api = FakeSystemApi(wizard = wizard(twitch = false, bot = false), ready = false)
        var signedIn = false
        val controller = controller(api, onReadyToSignIn = {
            signedIn = true
            true
        })
        controller.load()

        controller.finish()

        // Not ready ⇒ no sign-in, no completion (the gap: OAuth can't start before the app is configured).
        assertFalse(signedIn)
        assertFalse(api.setupCompleted)
    }

    @Test
    fun finish_surfaces_an_error_and_does_not_complete_when_signin_fails() = runTest {
        val api = FakeSystemApi(wizard = wizard(twitch = true, bot = true), ready = true)
        val controller = controller(api, onReadyToSignIn = { false })
        controller.load()

        controller.finish()

        val steps: SetupState.Steps = controller.state.value as SetupState.Steps
        assertEquals(SetupError.SignIn, steps.error)
        assertFalse(api.setupCompleted)
    }
}

// ── Fakes ─────────────────────────────────────────────────────────────────────

private fun wizard(twitch: Boolean, bot: Boolean, spotify: Boolean = false): SetupWizard {
    val saveCreds = SetupAction("save_credentials", "PUT", "/api/v1/system/setup/credentials/twitch", null)
    val botAction = SetupAction("oauth_redirect", "GET", "/api/v1/system/setup/bot/oauth-url", "/api/v1/system/setup/bot/status")
    return SetupWizard(
        complete = twitch && bot,
        steps =
            listOf(
                SetupStep(
                    key = "twitch_app",
                    title = "Connect your Twitch application",
                    description = "Paste your Twitch app credentials.",
                    required = true,
                    complete = twitch,
                    status = if (twitch) "configured" else "missing",
                    instructions = listOf("Register an app", "Set the redirect URL"),
                    action = saveCreds,
                    fields =
                        listOf(
                            SetupField("clientId", "Client ID", "text", true, null),
                            SetupField("clientSecret", "Client Secret", "password", true, null),
                        ),
                ),
                SetupStep(
                    key = "platform_bot",
                    title = "Authorize the bot account",
                    description = "Sign in as the bot account.",
                    required = true,
                    complete = bot,
                    status = if (bot) "connected" else "disconnected",
                    instructions = listOf("Log in as the bot"),
                    action = botAction,
                    fields = emptyList(),
                ),
                SetupStep(
                    key = "spotify",
                    title = "Spotify (optional)",
                    description = "Enable song requests.",
                    required = false,
                    complete = spotify,
                    status = if (spotify) "configured" else "not_configured",
                    instructions = listOf("Create a Spotify app"),
                    action = SetupAction("save_credentials", "PUT", "/api/v1/system/setup/credentials/spotify", null),
                    fields =
                        listOf(
                            SetupField("clientId", "Client ID", "text", true, null),
                            SetupField("clientSecret", "Client Secret", "password", true, null),
                        ),
                ),
            ),
    )
}

private fun checks(twitch: Boolean, bot: Boolean): SystemChecks =
    SystemChecks(
        twitchApp = SystemCheck(twitch, if (twitch) "configured" else "missing"),
        platformBot = SystemCheck(bot, if (bot) "connected" else "disconnected"),
    )

private class FakeSystemApi(
    private val wizard: SetupWizard,
    private val ready: Boolean,
    private val botOAuthUrl: String = "https://id.twitch.tv/authorize?bot",
) : SystemApi {
    // Let a test model the backend changing between the initial load and the post-action reload, so the
    // re-read (not an optimistic flip) is what advances the step + ready gate.
    var wizardAfter: SetupWizard? = null
    var readyAfter: Boolean? = null

    var savedTwitchClientId: String? = null
    var savedTwitchClientSecret: String? = null
    var savedTwitchBotUsername: String? = null
    var savedSpotify: Pair<String, String>? = null
    var setupCompleted: Boolean = false

    override suspend fun status(): ApiResult<SystemStatus> {
        val isReady: Boolean = readyAfter ?: ready
        val w: SetupWizard = wizardAfter ?: wizard
        val twitch: Boolean = w.steps.first { it.key == "twitch_app" }.complete
        val bot: Boolean = w.steps.first { it.key == "platform_bot" }.complete
        return ApiResult.Ok(SystemStatus(ready = isReady, checks = checks(twitch, bot)))
    }

    override suspend fun wizard(): ApiResult<SetupWizard> = ApiResult.Ok(wizardAfter ?: wizard)

    override suspend fun saveTwitchCredentials(
        clientId: String,
        clientSecret: String,
        botUsername: String?,
    ): ApiResult<Unit> {
        savedTwitchClientId = clientId
        savedTwitchClientSecret = clientSecret
        savedTwitchBotUsername = botUsername
        return ApiResult.Ok(Unit)
    }

    override suspend fun saveSpotifyCredentials(clientId: String, clientSecret: String): ApiResult<Unit> {
        savedSpotify = clientId to clientSecret
        return ApiResult.Ok(Unit)
    }

    override suspend fun saveYouTubeCredentials(clientId: String, clientSecret: String): ApiResult<Unit> =
        ApiResult.Ok(Unit)

    override suspend fun saveDiscordCredentials(clientId: String, clientSecret: String): ApiResult<Unit> =
        ApiResult.Ok(Unit)

    override suspend fun botOAuthUrl(): ApiResult<BotOAuthUrl> = ApiResult.Ok(BotOAuthUrl(botOAuthUrl))

    override suspend fun botStatus(): ApiResult<BotStatus> {
        val bot: Boolean = (wizardAfter ?: wizard).steps.first { it.key == "platform_bot" }.complete
        return ApiResult.Ok(BotStatus(connected = bot))
    }

    override suspend fun completeSetup(): ApiResult<Unit> {
        setupCompleted = true
        return ApiResult.Ok(Unit)
    }
}

// Drives the authorize-URL provider with a fixed loopback redirect (as the desktop launcher would) and
// records the URL it was asked to open, so a test can assert the exact URL the browser is sent to.
private class FakeConnectLauncher : ConnectLauncher {
    var openedUrl: String? = null

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
