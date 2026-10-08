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
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onFirst
import androidx.compose.ui.test.onLast
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.test.performTextInput
import androidx.compose.ui.test.runComposeUiTest
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.LifecycleRegistry
import androidx.lifecycle.compose.LocalLifecycleOwner
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.feedback.FeedbackKind
import bot.nomnomz.dashboard.core.feedback.RecordingFeedback
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.BotStatus
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.feature.integrations.state.FakeBotAuthApi
import bot.nomnomz.dashboard.feature.integrations.state.FakeChannelsApi
import bot.nomnomz.dashboard.feature.integrations.state.FakeIntegrationsApi
import bot.nomnomz.dashboard.feature.integrations.state.FakeSystemApi
import bot.nomnomz.dashboard.feature.integrations.state.IntegrationsController
import bot.nomnomz.dashboard.feature.integrations.state.makeIntegrationsController
import bot.nomnomz.dashboard.feature.settings.state.TwitchAppCredentialsController
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

// Shared defect SH-1 on the Integrations credential writes: the Twitch app credentials save (first-time and
// the overwrite confirm) and the provider credential forms (branded connect modal, reactive onboarding
// dialog). A write waits for the server; a failure leaves the typed input in place and the reason beside the
// control, with no failure toast on top.
@OptIn(ExperimentalTestApi::class)
class IntegrationsSavesWaitForServerTest {
    private val twitchDown: ApiResult<Unit> = ApiResult.Failure(ApiError(503, "DOWN", "Twitch said no."))
    private val reason: String = "Couldn't save the credentials: Twitch said no."

    @Test
    fun a_failed_first_time_twitch_save_keeps_the_typed_id_and_shows_the_reason_without_a_toast() = runComposeUiTest {
        val system: FakeSystemApi = FakeSystemApi(twitchSecretConfigured = false, twitchSaveResult = twitchDown)
        val feedback: RecordingFeedback = RecordingFeedback()
        setContent { Screen(system = system, feedback = feedback) }
        waitForIdle()
        onAllNodes(hasSetTextAction()).onFirst().performTextInput("my-client-id")
        onNodeWithText("Save credentials").performScrollTo().performClick()
        waitForIdle()

        assertEquals(1, system.twitchSaveCalls)
        onNodeWithText(reason).assertExists()
        onNodeWithText("my-client-id").assertExists()
        assertTrue(feedback.messages.none { it.kind == FeedbackKind.Error }, "a failure must not also toast")
    }

    @Test
    fun a_failed_twitch_overwrite_keeps_the_confirm_open_with_the_reason_inline() = runComposeUiTest {
        val system: FakeSystemApi = FakeSystemApi(twitchSecretConfigured = true, twitchSaveResult = twitchDown)
        val feedback: RecordingFeedback = RecordingFeedback()
        setContent { Screen(system = system, feedback = feedback) }
        waitForIdle()
        onNodeWithText("Edit").performScrollTo().performClick()
        waitForIdle()
        onAllNodes(hasSetTextAction()).onFirst().performTextInput("my-client-id")
        onNodeWithText("Save credentials").performScrollTo().performClick()
        waitForIdle()
        onNodeWithText("Replace credentials?").assertExists()
        onNodeWithText("Replace").performClick()
        waitForIdle()

        assertEquals(1, system.twitchSaveCalls)
        onNodeWithText("Replace credentials?").assertExists()
        assertTrue(feedback.messages.none { it.kind == FeedbackKind.Error }, "a failure must not also toast")
    }

    @Test
    fun a_successful_twitch_overwrite_closes_the_confirm() = runComposeUiTest {
        val system: FakeSystemApi = FakeSystemApi(twitchSecretConfigured = true)
        setContent { Screen(system = system, feedback = RecordingFeedback()) }
        waitForIdle()
        onNodeWithText("Edit").performScrollTo().performClick()
        waitForIdle()
        onAllNodes(hasSetTextAction()).onFirst().performTextInput("my-client-id")
        onNodeWithText("Save credentials").performScrollTo().performClick()
        waitForIdle()
        onNodeWithText("Replace").performClick()
        waitForIdle()

        assertEquals("my-client-id", system.savedTwitchClientId)
        onNodeWithText("Replace credentials?").assertDoesNotExist()
    }

    @Test
    fun a_failed_provider_credential_save_in_the_connect_modal_keeps_the_input_and_shows_the_reason() = runComposeUiTest {
        val system: FakeSystemApi = FakeSystemApi(twitchSecretConfigured = true, saveSucceeds = false)
        val feedback: RecordingFeedback = RecordingFeedback()
        setContent { Screen(system = system, feedback = feedback) }
        waitForIdle()
        openDiscordCredentialStep()
        onAllNodes(hasSetTextAction()).onFirst().performTextInput("discord-client-id")
        onNodeWithText("Save and continue").performClick()
        waitForIdle()

        onNodeWithText("Couldn't save the credentials: Not allowed.").assertExists()
        onNodeWithText("discord-client-id").assertExists()
        onNodeWithText("Save and continue").assertExists()
        assertTrue(feedback.messages.none { it.kind == FeedbackKind.Error }, "a failure must not also toast")
    }

    @Test
    fun a_successful_provider_credential_save_registers_the_client_and_leaves_the_credential_step() = runComposeUiTest {
        val system: FakeSystemApi = FakeSystemApi(twitchSecretConfigured = true)
        setContent { Screen(system = system, feedback = RecordingFeedback()) }
        waitForIdle()
        openDiscordCredentialStep()
        onAllNodes(hasSetTextAction()).onFirst().performTextInput("discord-client-id")
        onNodeWithText("Save and continue").performClick()
        waitForIdle()

        assertEquals("discord-client-id", system.savedDiscord?.first)
        onNodeWithText("Save and continue").assertDoesNotExist()
    }

    @Test
    fun a_failed_onboarding_credential_save_keeps_the_dialog_open_with_the_reason() = runComposeUiTest {
        val system: FakeSystemApi = FakeSystemApi(twitchSecretConfigured = true, saveSucceeds = false)
        val feedback: RecordingFeedback = RecordingFeedback()
        val integrations: FakeIntegrationsApi =
            FakeIntegrationsApi(
                status = emptyList(),
                startResult = ApiResult.Failure(ApiError(400, "PROVIDER_NOT_CONFIGURED", "not configured")),
            )
        setContent { Screen(system = system, feedback = feedback, integrations = integrations) }
        waitForIdle()
        onAllNodesWithText("Connect").get(4).performScrollTo().performClick()
        waitForIdle()
        onNodeWithText("Set up Kick first").assertExists()
        // The dialog's two fields come last (client id, then secret): the id is second from the end.
        val fieldCount: Int = onAllNodes(hasSetTextAction()).fetchSemanticsNodes().size
        onAllNodes(hasSetTextAction())[fieldCount - 2].performTextInput("kick-client-id")
        onNodeWithText("Save and continue").performClick()
        waitForIdle()

        onNodeWithText("Set up Kick first").assertExists()
        onNodeWithText("Couldn't save the credentials: Not allowed.").assertExists()
        onNodeWithText("kick-client-id").assertExists()
        assertTrue(feedback.messages.none { it.kind == FeedbackKind.Error }, "a failure must not also toast")
    }

    private fun androidx.compose.ui.test.ComposeUiTest.openDiscordCredentialStep() {
        onAllNodesWithText("Connect").get(3).performScrollTo().performClick()
        waitForIdle()
        onNodeWithText("Continue with Discord").performClick()
        waitForIdle()
    }

    @Composable
    private fun Screen(
        system: FakeSystemApi,
        feedback: RecordingFeedback,
        integrations: FakeIntegrationsApi = FakeIntegrationsApi(status = emptyList()),
    ) {
        val controller: IntegrationsController =
            makeIntegrationsController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeBotAuthApi(BotStatus(connected = false)),
                integrations,
                feedback,
                system,
            )
        val twitchApp: TwitchAppCredentialsController =
            TwitchAppCredentialsController(system, { "http://localhost:5080" }, feedback)
        // The screen collects its flows with collectAsStateWithLifecycle(), which needs a LocalLifecycleOwner.
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
                NomNomzTheme {
                    IntegrationsScreen(
                        controller = controller,
                        twitchAppController = twitchApp,
                        role = ManagementRole.Broadcaster,
                    )
                }
            }
        }
    }
}
