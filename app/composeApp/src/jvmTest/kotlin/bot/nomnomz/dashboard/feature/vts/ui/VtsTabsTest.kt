// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.vts.ui

import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.test.ExperimentalTestApi
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
import bot.nomnomz.dashboard.core.network.VtsConnection
import bot.nomnomz.dashboard.core.network.VtsHotkeyRef
import bot.nomnomz.dashboard.core.network.VtsModelInventory
import bot.nomnomz.dashboard.core.network.VtsModelRef
import bot.nomnomz.dashboard.feature.integrations.state.FakeChannelsApi
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.vts.state.FakeVtsApi
import bot.nomnomz.dashboard.feature.vts.state.VtsController
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlinx.coroutines.runBlocking

// The VTube Studio page has two tabs (Hotkeys, Expressions). Until VTS is authorized and answers the page shows
// one connect step instead of the tab's controls; once connected, Hotkeys holds the model and hotkey pickers and
// Expressions holds the expression picker. The connection form lives on Integrations, so the connect step only
// sends the operator there.
@OptIn(ExperimentalTestApi::class)
class VtsTabsTest {
    private val inventory: VtsModelInventory =
        VtsModelInventory(
            models = listOf(VtsModelRef(id = "m1", name = "Akari")),
            hotkeys = listOf(VtsHotkeyRef(id = "h1", name = "Wave")),
            expressions = listOf("smile.exp3.json"),
        )
    private val connectLine: String = "VTube Studio is not connected. Connect it to control your model."

    @Test
    fun a_disconnected_page_shows_the_connect_step_and_none_of_the_controls_on_either_tab() = runComposeUiTest {
        val api: FakeVtsApi = FakeVtsApi(connection = VtsConnection(hasPluginToken = false), inventory = null)
        var tab: VtsSection by mutableStateOf(VtsSection.Hotkeys)
        setContent { Screen(api, tab, onOpenIntegrations = {}) }
        waitForIdle()

        onNodeWithText(connectLine).assertExists()
        onNodeWithText("Open VTube Studio connection").assertExists()
        onNodeWithText("Trigger").assertDoesNotExist()
        onNodeWithText("Save connection").assertDoesNotExist()

        tab = VtsSection.Expressions
        waitForIdle()
        onNodeWithText(connectLine).assertExists()
        onNodeWithText("Activate").assertDoesNotExist()
    }

    @Test
    fun the_connect_button_opens_the_vts_connection_on_integrations() = runComposeUiTest {
        val api: FakeVtsApi = FakeVtsApi(connection = VtsConnection(hasPluginToken = false))
        var opened: Int = 0
        setContent { Screen(api, VtsSection.Hotkeys, onOpenIntegrations = { opened += 1 }) }
        waitForIdle()
        onNodeWithText("Open VTube Studio connection").performClick()
        waitForIdle()

        assertEquals(1, opened)
    }

    @Test
    fun a_connected_hotkeys_tab_shows_the_model_and_hotkey_controls_and_not_the_expression_or_connection_ones() =
        runComposeUiTest {
            val api: FakeVtsApi = FakeVtsApi(connection = VtsConnection(hasPluginToken = true), inventory = inventory)
            setContent { Screen(api, VtsSection.Hotkeys, onOpenIntegrations = {}) }
            waitForIdle()

            onNodeWithText("Load model").assertExists()
            onNodeWithText("Trigger").assertExists()
            onNodeWithText("Activate").assertDoesNotExist()
            onNodeWithText(connectLine).assertDoesNotExist()
            onNodeWithText("Save connection").assertDoesNotExist()
        }

    @Test
    fun a_connected_expressions_tab_shows_the_expression_control_and_not_the_hotkey_ones() = runComposeUiTest {
        val api: FakeVtsApi = FakeVtsApi(connection = VtsConnection(hasPluginToken = true), inventory = inventory)
        setContent { Screen(api, VtsSection.Expressions, onOpenIntegrations = {}) }
        waitForIdle()

        onNodeWithText("Activate").assertExists()
        onNodeWithText("Trigger").assertDoesNotExist()
        onNodeWithText("Load model").assertDoesNotExist()
    }

    @Composable
    private fun Screen(api: FakeVtsApi, section: VtsSection, onOpenIntegrations: () -> Unit) {
        val controller: VtsController = VtsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "c1"))), api)
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
                    VtsScreen(
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
