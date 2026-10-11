// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.obs.ui

import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.runComposeUiTest
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.LifecycleRegistry
import androidx.lifecycle.compose.LocalLifecycleOwner
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ObsProbe
import bot.nomnomz.dashboard.core.network.ObsScene
import bot.nomnomz.dashboard.feature.integrations.state.FakeChannelsApi
import bot.nomnomz.dashboard.feature.obs.state.FakeObsApi
import bot.nomnomz.dashboard.feature.obs.state.ObsController
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlinx.coroutines.runBlocking

// The OBS page has two tabs (Live control, Advanced). Until OBS is connected the page shows one connect step
// instead of the tab's controls; once connected, Live control lists the scenes OBS returns. The connection form
// lives on Integrations, so the connect step only sends the operator there.
@OptIn(ExperimentalTestApi::class)
class ObsTabsTest {
    private val scenes: List<ObsScene> =
        listOf(ObsScene(name = "Gameplay", isCurrent = true), ObsScene(name = "Intermission"))
    private val connectLine: String = "OBS is not connected. Connect it to control your scenes."

    @Test
    fun a_disconnected_page_shows_the_connect_step_and_none_of_the_controls_on_either_tab() = runComposeUiTest {
        val api: FakeObsApi = FakeObsApi(probe = ObsProbe(connected = false), scenes = scenes)
        var tab: ObsSection by mutableStateOf(ObsSection.Live)
        setContent { Screen(api, tab, onOpenIntegrations = {}) }
        waitForIdle()

        onNodeWithText(connectLine).assertExists()
        onNodeWithText("Open OBS connection").assertExists()
        onNodeWithText("Gameplay").assertDoesNotExist()
        onNodeWithText("Save connection").assertDoesNotExist()

        tab = ObsSection.Advanced
        waitForIdle()
        onNodeWithText(connectLine).assertExists()
        onNodeWithText("Gameplay").assertDoesNotExist()
    }

    @Test
    fun the_connect_button_opens_the_obs_connection_on_integrations() = runComposeUiTest {
        val api: FakeObsApi = FakeObsApi(probe = ObsProbe(connected = false))
        var opened: Int = 0
        setContent { Screen(api, ObsSection.Live, onOpenIntegrations = { opened += 1 }) }
        waitForIdle()
        onNodeWithText("Open OBS connection").performClick()
        waitForIdle()

        assertEquals(1, opened)
    }

    @Test
    fun a_connected_live_control_tab_shows_the_scenes_obs_returns_and_no_connect_step_or_form() = runComposeUiTest {
        val api: FakeObsApi = FakeObsApi(probe = ObsProbe(connected = true), scenes = scenes)
        setContent { Screen(api, ObsSection.Live, onOpenIntegrations = {}) }
        waitForIdle()

        // Each scene is listed twice on Live control: in the scene switcher and in the scene-items picker.
        onAllNodesWithText("Gameplay").assertCountEquals(2)
        onAllNodesWithText("Intermission").assertCountEquals(2)
        onNodeWithText(connectLine).assertDoesNotExist()
        onNodeWithText("Save connection").assertDoesNotExist()
    }

    @Test
    fun a_connected_advanced_tab_shows_the_raw_request_cards_and_not_the_scene_buttons() = runComposeUiTest {
        val api: FakeObsApi = FakeObsApi(probe = ObsProbe(connected = true), scenes = scenes)
        setContent { Screen(api, ObsSection.Advanced, onOpenIntegrations = {}) }
        waitForIdle()

        onNodeWithText("Raw request batch").assertExists()
        onNodeWithText("Gameplay").assertDoesNotExist()
    }

    @Composable
    private fun Screen(api: FakeObsApi, section: ObsSection, onOpenIntegrations: () -> Unit) {
        val controller: ObsController = ObsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "c1"))), api)
        runBlocking { controller.load() }
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
            NomNomzTheme {
                AppEnvironment("en") {
                    ObsScreen(
                        controller = controller,
                        role = ManagementRole.Broadcaster,
                        section = section,
                        onOpenIntegrations = onOpenIntegrations,
                    )
                }
            }
        }
    }
}
