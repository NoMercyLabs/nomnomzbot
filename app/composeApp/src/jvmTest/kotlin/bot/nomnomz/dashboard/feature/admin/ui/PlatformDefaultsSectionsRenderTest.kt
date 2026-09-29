// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.admin.ui

import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.ActionDefault
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.EventResponseDefault
import bot.nomnomz.dashboard.core.network.BuiltinReplyDefault
import bot.nomnomz.dashboard.feature.admin.state.FakePlatformDefaultsApi
import bot.nomnomz.dashboard.feature.admin.state.PlatformDefaultsController
import kotlinx.coroutines.test.runTest
import kotlin.test.Test

/**
 * Plan item A4 follow-up: a failed load of a platform-defaults family surfaces the server's real error message
 * on screen (never a silent blank list) and clears the loading spinner once the call settles — previously these
 * four families had no loading/error tracking at all, so a slow or failed load rendered as an empty section
 * indistinguishable from "there is genuinely nothing here".
 */
@OptIn(ExperimentalTestApi::class)
class PlatformDefaultsSectionsRenderTest {

    @Composable
    private fun EnglishContent(content: @Composable () -> Unit) {
        AppEnvironment(tag = "en") {
            NomNomzTheme { content() }
        }
    }

    @Composable
    private fun ObservingActionDefaultsSection(controller: PlatformDefaultsController) {
        val state by controller.state.collectAsState()
        ActionDefaultsSection(state = state, controller = controller)
    }

    @Composable
    private fun ObservingEventResponseDefaultsSection(controller: PlatformDefaultsController) {
        val state by controller.state.collectAsState()
        EventResponseDefaultsSection(state = state, controller = controller)
    }

    @Composable
    private fun ObservingBuiltinReplyDefaultsSection(controller: PlatformDefaultsController) {
        val state by controller.state.collectAsState()
        BuiltinReplyDefaultsSection(state = state, controller = controller)
    }

    @Composable
    private fun ObservingTtsVoiceDefaultSection(controller: PlatformDefaultsController) {
        val state by controller.state.collectAsState()
        TtsVoiceDefaultSection(state = state, controller = controller)
    }

    @Test
    fun a_failed_action_defaults_load_renders_the_servers_error_message() = runComposeUiTest {
        val api = FakePlatformDefaultsApi(actions = emptyList())
        api.failActionDefaults = ApiError(503, "UNAVAILABLE", "the action-defaults service is unreachable")
        val controller = PlatformDefaultsController(api)
        runTest { controller.loadActionDefaults() }

        setContent { EnglishContent { ObservingActionDefaultsSection(controller) } }

        onNodeWithText("the action-defaults service is unreachable").assertExists()
    }

    @Test
    fun a_failed_event_response_defaults_load_renders_the_servers_error_message() = runComposeUiTest {
        val api = FakePlatformDefaultsApi(
            actions = emptyList(),
            events = listOf(
                EventResponseDefault(
                    eventType = "channel.follow",
                    isEnabled = true,
                    message = "Welcome {user}!",
                    variables = listOf("user"),
                    channelsFollowing = 4,
                    channelsWithOwnResponse = 2,
                ),
            ),
        )
        api.failEventResponseDefaults = ApiError(500, "INTERNAL", "event defaults failed to load")
        val controller = PlatformDefaultsController(api)
        runTest { controller.loadEventResponseDefaults() }

        setContent { EnglishContent { ObservingEventResponseDefaultsSection(controller) } }

        onNodeWithText("event defaults failed to load").assertExists()
    }

    @Test
    fun a_failed_builtin_reply_defaults_load_renders_the_servers_error_message() = runComposeUiTest {
        val api = FakePlatformDefaultsApi(
            actions = emptyList(),
            replies = listOf(
                BuiltinReplyDefault(
                    builtinKey = "uptime",
                    slot = "live",
                    shippedTemplate = "{channel} has been live for {uptime}.",
                    platformTemplate = null,
                    channelsWithOwnReply = 2,
                ),
            ),
        )
        api.failBuiltinReplyDefaults = ApiError(500, "INTERNAL", "reply defaults failed to load")
        val controller = PlatformDefaultsController(api)
        runTest { controller.loadBuiltinReplyDefaults() }

        setContent { EnglishContent { ObservingBuiltinReplyDefaultsSection(controller) } }

        onNodeWithText("reply defaults failed to load").assertExists()
    }

    @Test
    fun a_failed_tts_voice_candidates_load_renders_the_servers_error_message() = runComposeUiTest {
        val api = FakePlatformDefaultsApi(actions = emptyList())
        api.failTtsVoiceCandidates = ApiError(500, "INTERNAL", "voice candidates failed to load")
        val controller = PlatformDefaultsController(api)
        runTest { controller.loadTtsVoiceDefault() }

        setContent { EnglishContent { ObservingTtsVoiceDefaultSection(controller) } }

        onNodeWithText("voice candidates failed to load").assertExists()
    }
}
