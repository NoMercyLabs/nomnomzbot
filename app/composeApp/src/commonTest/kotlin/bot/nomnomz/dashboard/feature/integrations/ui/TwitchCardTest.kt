// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.integrations.ui

import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.hasSetTextAction
import androidx.compose.ui.test.onAllNodesWithContentDescription
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performTextInput
import androidx.compose.ui.test.runComposeUiTest
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.LifecycleRegistry
import androidx.lifecycle.compose.LocalLifecycleOwner
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.BotOAuthUrl
import bot.nomnomz.dashboard.core.network.BotStatus
import bot.nomnomz.dashboard.core.network.PronounOption
import bot.nomnomz.dashboard.core.network.SetupWizard
import bot.nomnomz.dashboard.core.network.SystemApi
import bot.nomnomz.dashboard.core.network.SystemCheck
import bot.nomnomz.dashboard.core.network.SystemChecks
import bot.nomnomz.dashboard.core.network.SystemStatus
import bot.nomnomz.dashboard.feature.settings.state.TwitchAppCredentialsController
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

// Proves the merged Twitch card: the Twitch app the bot signs in through and the bot account it signs in
// are ONE card, and each action still reaches its owner — Connect reaches the bot callback, Save reaches the
// credentials controller (asserted on the PUT the fake backend received, not on a node existing). English is
// pinned via [AppEnvironment] so the assertions don't depend on locale.
@OptIn(ExperimentalTestApi::class)
class TwitchCardTest {

    // The credentials section collects its controller state with a lifecycle, so the test provides one.
    @Composable
    private fun EnglishContent(content: @Composable () -> Unit) {
        val owner: LifecycleOwner =
            object : LifecycleOwner {
                override val lifecycle: Lifecycle = LifecycleRegistry.createUnsafe(this)
            }
        (owner.lifecycle as LifecycleRegistry).apply {
            currentState = Lifecycle.State.CREATED
            currentState = Lifecycle.State.STARTED
            currentState = Lifecycle.State.RESUMED
        }
        CompositionLocalProvider(LocalLifecycleOwner provides owner) {
            AppEnvironment(tag = "en") {
                NomNomzTheme { content() }
            }
        }
    }

    @Test
    fun one_card_shows_the_app_state_and_the_bot_account_and_connect_reaches_the_bot_callback() = runComposeUiTest {
        val api = FakeSystemApi(twitchConfigured = false)
        val controller = TwitchAppCredentialsController(api, { "https://bot.example.test" })
        var connectCalls = 0

        setContent {
            EnglishContent {
                TwitchCard(
                    appController = controller,
                    botConnected = false,
                    botAccountName = null,
                    botBusy = false,
                    manage = ManageDecision.Allowed,
                    onConnectBot = { connectCalls += 1 },
                    onDisconnectBot = {},
                )
            }
        }
        waitForIdle()

        assertTrue(onAllNodesWithText("Twitch").fetchSemanticsNodes().isNotEmpty(), "expected the one Twitch title")
        assertTrue(onAllNodesWithText("Bot account").fetchSemanticsNodes().isNotEmpty(), "expected the bot part")
        assertTrue(onAllNodesWithText("Not connected").fetchSemanticsNodes().isNotEmpty(), "bot must read not connected")
        assertTrue(
            // The configuration state is one screen-reader node (content description), not a bare text node.
            onAllNodesWithContentDescription("Using the shared app").fetchSemanticsNodes().isNotEmpty(),
            "expected the app part's configuration state in the same card",
        )

        onNodeWithText("Connect").performClick()
        waitForIdle()
        assertEquals(1, connectCalls, "Connect must reach the bot connect callback exactly once")
    }

    @Test
    fun connected_bot_shows_its_account_and_disconnect_reaches_the_bot_callback() = runComposeUiTest {
        val api = FakeSystemApi(twitchConfigured = true)
        val controller = TwitchAppCredentialsController(api, { "https://bot.example.test" })
        var disconnectCalls = 0

        setContent {
            EnglishContent {
                TwitchCard(
                    appController = controller,
                    botConnected = true,
                    botAccountName = "MyStreamBot",
                    botBusy = false,
                    manage = ManageDecision.Allowed,
                    onConnectBot = {},
                    onDisconnectBot = { disconnectCalls += 1 },
                )
            }
        }
        waitForIdle()

        assertTrue(
            onAllNodesWithText("Connected as MyStreamBot").fetchSemanticsNodes().isNotEmpty(),
            "a connected bot must show its own account name",
        )
        assertTrue(
            onAllNodesWithContentDescription("Using your own app").fetchSemanticsNodes().isNotEmpty(),
            "a configured app must read as the user's own app",
        )

        onNodeWithText("Disconnect").performClick()
        waitForIdle()
        assertEquals(1, disconnectCalls, "Disconnect must reach the bot disconnect callback exactly once")
    }

    @Test
    fun saving_credentials_from_the_merged_card_puts_them_through_the_controller() = runComposeUiTest {
        val api = FakeSystemApi(twitchConfigured = false)
        val controller = TwitchAppCredentialsController(api, { "https://bot.example.test" })

        setContent {
            EnglishContent {
                TwitchCard(
                    appController = controller,
                    botConnected = false,
                    botAccountName = null,
                    botBusy = false,
                    manage = ManageDecision.Allowed,
                    onConnectBot = {},
                    onDisconnectBot = {},
                )
            }
        }
        waitForIdle()

        // Not configured: the form is open. The first text field is the client id.
        onAllNodes(matcher = hasSetTextAction())[0].performTextInput("myclientid")
        onNodeWithText("Save credentials").performClick()
        waitUntil(timeoutMillis = 5_000) { api.savedClientId != null }

        assertEquals("myclientid", api.savedClientId, "Save must PUT the typed client id through the controller")
        assertEquals("", api.savedClientSecret, "the secret is optional and rides blank")
    }
}

private class FakeSystemApi(private val twitchConfigured: Boolean) : SystemApi {
    var savedClientId: String? = null
    var savedClientSecret: String? = null

    override suspend fun status(): ApiResult<SystemStatus> {
        val twitch: Boolean = savedClientId != null || twitchConfigured
        return ApiResult.Ok(
            SystemStatus(
                onboardingComplete = twitch,
                checks =
                    SystemChecks(
                        twitchApp =
                            SystemCheck(
                                ok = twitch,
                                ready = true,
                                status = if (twitch) "ready_redirect" else "ready_device",
                            ),
                        platformBot = SystemCheck(ok = true, ready = true, status = "connected"),
                    ),
            ),
        )
    }

    override suspend fun saveTwitchCredentials(
        clientId: String,
        clientSecret: String,
        botUsername: String?,
    ): ApiResult<Unit> {
        savedClientId = clientId
        savedClientSecret = clientSecret
        return ApiResult.Ok(Unit)
    }

    override suspend fun wizard(): ApiResult<SetupWizard> = ApiResult.Ok(SetupWizard(complete = false))

    override suspend fun saveCredentials(provider: String, clientId: String, clientSecret: String): ApiResult<Unit> =
        ApiResult.Ok(Unit)

    override suspend fun useSharedTwitchApp(): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun botOAuthUrl(): ApiResult<BotOAuthUrl> = ApiResult.Ok(BotOAuthUrl("https://unused"))

    override suspend fun botStatus(): ApiResult<BotStatus> = ApiResult.Ok(BotStatus(connected = true))

    override suspend fun completeSetup(): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun pronouns(): ApiResult<List<PronounOption>> = ApiResult.Ok(emptyList())
}
