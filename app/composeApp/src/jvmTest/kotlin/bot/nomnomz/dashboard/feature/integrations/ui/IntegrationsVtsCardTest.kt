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
import androidx.compose.ui.test.performTextClearance
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
import bot.nomnomz.dashboard.core.network.VtsConnection
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

// The VTube Studio connection form lives on Integrations (it moved off the VTube Studio page). Saving it sends
// the typed endpoint to the VTS connection endpoint through the one VtsController.
@OptIn(ExperimentalTestApi::class)
class IntegrationsVtsCardTest {
    @Test
    fun the_vts_card_saves_the_typed_endpoint_and_the_fake_receives_it() = runComposeUiTest {
        val vts: FakeVtsApi = FakeVtsApi(upsert = { ApiResult.Ok(VtsConnection(endpoint = "ws://10.0.0.5:8001")) })
        setContent { Screen(vts) }
        waitForIdle()
        // The VTS card is the last on the page: its endpoint is the last text field.
        val fieldCount: Int = onAllNodes(hasSetTextAction()).fetchSemanticsNodes().size
        onAllNodes(hasSetTextAction())[fieldCount - 1].performScrollTo().performTextClearance()
        onAllNodes(hasSetTextAction())[fieldCount - 1].performScrollTo().performTextInput("ws://10.0.0.5:8001")
        // The VTube Studio card sits below the OBS card, which also has a "Save connection" button.
        onAllNodesWithText("Save connection")[1].performScrollTo().performClick()
        waitForIdle()

        assertEquals(listOf<String?>("ws://10.0.0.5:8001"), vts.sent.map { it.endpoint })
    }

    @Composable
    private fun Screen(vts: FakeVtsApi) {
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
                        obsController = ObsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), FakeObsApi()),
                        vtsController = VtsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), vts),
                        backendOrigin = "http://localhost:5080",
                        role = ManagementRole.Broadcaster,
                    )
                }
            }
        }
    }
}
