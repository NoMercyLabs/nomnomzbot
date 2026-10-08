// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.games.ui

import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.assertIsDisplayed
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.LifecycleRegistry
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.GameSummary
import bot.nomnomz.dashboard.feature.commands.state.PrimaryChannelOnly
import bot.nomnomz.dashboard.feature.games.state.GamesController
import bot.nomnomz.dashboard.feature.games.state.RecordingGamesApi
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

// The game config dialog's reset, as rendered: it first says what goes back to default and that the on/off state
// stays, and only the confirm sends the reset.
@OptIn(ExperimentalTestApi::class)
class GamesScreenResetTest {
    @Test
    fun reset_shows_its_consequence_and_only_the_confirm_sends_it() = runComposeUiTest {
        val game =
            GameSummary(
                id = "g1",
                gameType = "coinflip",
                category = "gambling",
                isEnabled = true,
                winChancePercent = 70.0,
                permission = "Subscriber",
            )
        val api = RecordingGamesApi(ApiResult.Ok(listOf(game)))
        val controller = GamesController(PrimaryChannelOnly(), api)

        setContent {
            NomNomzTheme {
                AppEnvironment("en") {
                    withLifecycle { GamesScreen(controller = controller, role = ManagementRole.Broadcaster) }
                }
            }
        }
        waitForIdle()

        onNodeWithContentDescription("Edit coinflip").performClick()
        waitForIdle()
        // A real pointer tap: the footer must be on screen even though the settings are taller than the window.
        onNodeWithTag("game-reset").performScrollTo().performClick()
        waitForIdle()

        onNodeWithText(
            "coinflip goes back to its default odds, payout, bet limits, cooldown, who can play and custom " +
                "settings. The 18+ check turns off. Whether the game is on or off does not change.",
        ).assertExists()
        assertTrue(api.resets.isEmpty(), "nothing is reset before the confirm")

        onNodeWithText("Reset").performClick()
        waitForIdle()

        assertEquals(listOf("coinflip"), api.resets)
    }

    // The settings are taller than the window. The body scrolls and the footer stays pinned and visible, so
    // Save and Reset can be reached without scrolling the whole dialog off the screen.
    @Test
    fun the_footer_stays_on_screen_when_the_settings_are_taller_than_the_window() = runComposeUiTest {
        val game = GameSummary(id = "g1", gameType = "coinflip", category = "gambling", isEnabled = true)
        val controller = GamesController(PrimaryChannelOnly(), RecordingGamesApi(ApiResult.Ok(listOf(game))))

        setContent {
            NomNomzTheme {
                AppEnvironment("en") {
                    withLifecycle { GamesScreen(controller = controller, role = ManagementRole.Broadcaster) }
                }
            }
        }
        waitForIdle()
        onNodeWithContentDescription("Edit coinflip").performClick()
        waitForIdle()

        onNodeWithTag("game-reset").performScrollTo().assertIsDisplayed()
        onNodeWithText("Save").assertIsDisplayed()
    }
}

// collectAsStateWithLifecycle needs a resumed lifecycle owner; the test host provides none.
@Composable
private fun withLifecycle(content: @Composable () -> Unit) {
    val owner: LifecycleOwner =
        object : LifecycleOwner {
            override val lifecycle: Lifecycle = LifecycleRegistry.createUnsafe(this)
        }
    (owner.lifecycle as LifecycleRegistry).currentState = Lifecycle.State.RESUMED
    CompositionLocalProvider(LocalLifecycleOwner provides owner) { content() }
}
