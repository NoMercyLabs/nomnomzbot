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
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.test.performTextInput
import androidx.compose.ui.test.runComposeUiTest
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.LifecycleRegistry
import androidx.lifecycle.compose.LocalLifecycleOwner
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.feedback.RecordingFeedback
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.BotStatus
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ObsConnection
import bot.nomnomz.dashboard.feature.integrations.state.FakeBotAuthApi
import bot.nomnomz.dashboard.feature.integrations.state.FakeChannelsApi
import bot.nomnomz.dashboard.feature.integrations.state.FakeIntegrationsApi
import bot.nomnomz.dashboard.feature.integrations.state.FakeSystemApi
import bot.nomnomz.dashboard.feature.integrations.state.IntegrationsController
import bot.nomnomz.dashboard.feature.integrations.state.makeIntegrationsController
import bot.nomnomz.dashboard.feature.obs.state.FakeObsApi
import bot.nomnomz.dashboard.feature.obs.state.ObsController
import bot.nomnomz.dashboard.feature.settings.state.TwitchAppCredentialsController
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.vts.state.FakeVtsApi
import bot.nomnomz.dashboard.feature.vts.state.VtsController
import kotlin.test.Test
import kotlin.test.assertEquals

// The OBS connection form lives on Integrations (it moved off the OBS page). Saving it sends the typed
// settings to the OBS connection endpoint through the one ObsController.
@OptIn(ExperimentalTestApi::class)
class IntegrationsObsCardTest {
    @Test
    fun the_obs_card_saves_the_typed_host_and_port_and_the_fake_receives_them() = runComposeUiTest {
        val obs: FakeObsApi = FakeObsApi(upsert = { ApiResult.Ok(ObsConnection(host = "10.0.0.5", port = 4456)) })
        setContent { Screen(obs) }
        waitForIdle()
        // The OBS card sits above the VTube Studio card, which repeats the "Direct" chip and "Save connection".
        onAllNodesWithText("Direct")[0].performScrollTo().performClick()
        // Direct mode shows host, port and password in that order, then the one VTube Studio endpoint field.
        val fieldCount: Int = onAllNodes(hasSetTextAction()).fetchSemanticsNodes().size
        onAllNodes(hasSetTextAction())[fieldCount - 4].performScrollTo().performTextInput("10.0.0.5")
        onAllNodes(hasSetTextAction())[fieldCount - 3].performScrollTo().performTextInput("4456")
        onAllNodesWithText("Save connection")[0].performScrollTo().performClick()
        waitForIdle()

        assertEquals(listOf("10.0.0.5"), obs.sent.map { it.host })
        assertEquals(listOf(4456), obs.sent.map { it.port })
    }

    @Composable
    private fun Screen(obs: FakeObsApi) {
        val feedback: RecordingFeedback = RecordingFeedback()
        val system: FakeSystemApi = FakeSystemApi(twitchSecretConfigured = true)
        val controller: IntegrationsController =
            makeIntegrationsController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeBotAuthApi(BotStatus(connected = false)),
                FakeIntegrationsApi(status = emptyList()),
                feedback,
                system,
            )
        val twitchApp: TwitchAppCredentialsController =
            TwitchAppCredentialsController(system, { "http://localhost:5080" }, feedback)
        val obsController: ObsController = ObsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), obs)
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
                        obsController = obsController,
                        vtsController = VtsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), FakeVtsApi()),
                        backendOrigin = "http://localhost:5080",
                        role = ManagementRole.Broadcaster,
                    )
                }
            }
        }
    }
}
