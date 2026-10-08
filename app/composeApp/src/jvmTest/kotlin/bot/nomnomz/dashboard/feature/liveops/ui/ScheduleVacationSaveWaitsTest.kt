// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.liveops.ui

import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.hasSetTextAction
import androidx.compose.ui.test.isToggleable
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.test.performTextReplacement
import androidx.compose.ui.test.runComposeUiTest
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.LifecycleRegistry
import androidx.lifecycle.compose.LocalLifecycleOwner
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.LiveOpsScheduleVacation
import bot.nomnomz.dashboard.feature.liveops.state.ScheduleController
import bot.nomnomz.dashboard.feature.moderation.state.FakeChannelsApi
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlinx.coroutines.CompletableDeferred

// Shared defect SH-1 on the stream schedule vacation card: Save vacation fired the write in a detached coroutine, so
// it had no pending state and a failure showed only in the page banner, away from the control. These run the real
// screen and controller against a fake API: the button waits for the server, a failure keeps the chosen dates and
// shows the reason next to the button (once), and turning the window off is the same one click with the same wait.
@OptIn(ExperimentalTestApi::class)
class ScheduleVacationSaveWaitsTest {
    private val reason: String = "Twitch refused the vacation"
    private val start: String = "2026-08-01T00:00:00Z"

    @Composable
    private fun WithLifecycle(content: @Composable () -> Unit) {
        val owner: LifecycleOwner =
            object : LifecycleOwner {
                override val lifecycle: Lifecycle = LifecycleRegistry.createUnsafe(this)
            }
        (owner.lifecycle as LifecycleRegistry).apply {
            currentState = Lifecycle.State.CREATED
            currentState = Lifecycle.State.STARTED
            currentState = Lifecycle.State.RESUMED
        }
        CompositionLocalProvider(LocalLifecycleOwner provides owner) { content() }
    }

    private fun androidx.compose.ui.test.ComposeUiTest.openPage(api: FakeScheduleApi) {
        setContent {
            WithLifecycle {
                AppEnvironment(tag = "en") {
                    NomNomzTheme {
                        ScheduleScreen(
                            controller =
                                ScheduleController(
                                    FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                                    api,
                                    NoStreamApi(),
                                    NoFileIO(),
                                ),
                            role = ManagementRole.Broadcaster,
                        )
                    }
                }
            }
        }
        waitForIdle()
    }

    private fun androidx.compose.ui.test.ComposeUiTest.enableAndType() {
        onNode(isToggleable()).performScrollTo().performClick()
        waitForIdle()
        onAllNodes(hasSetTextAction())[0].performTextReplacement(start)
        waitForIdle()
    }

    private fun androidx.compose.ui.test.ComposeUiTest.clickSave() {
        onNodeWithText("Save vacation").performScrollTo().performClick()
        waitForIdle()
    }

    @Test
    fun a_failed_vacation_save_keeps_the_dates_and_shows_the_reason_once_next_to_the_button() = runComposeUiTest {
        val api = FakeScheduleApi().apply { settingsAnswer = { ApiResult.Failure(ApiError(403, "FORBIDDEN", reason)) } }
        openPage(api)
        enableAndType()
        clickSave()

        assertEquals(listOf(true), api.settings.map { it.isVacationEnabled })
        onNodeWithText(start).assertExists()
        onAllNodesWithText(reason).assertCountEquals(1)
        onNodeWithText("Save vacation").assertExists()
    }

    @Test
    fun a_successful_vacation_save_sends_the_window_and_shows_no_error() = runComposeUiTest {
        val api = FakeScheduleApi()
        openPage(api)
        enableAndType()
        clickSave()

        assertEquals(listOf(start), api.settings.map { it.vacationStartTime })
        assertEquals(listOf("Europe/Amsterdam"), api.settings.map { it.timezone })
        onAllNodesWithText(reason).assertCountEquals(0)
    }

    @Test
    fun the_button_is_pending_while_the_save_runs_and_sends_no_second_write() = runComposeUiTest {
        val gate: CompletableDeferred<ApiResult<Unit>> = CompletableDeferred()
        val api = FakeScheduleApi().apply { settingsAnswer = { gate.await() } }
        openPage(api)
        enableAndType()
        clickSave()

        // Pending: the label is replaced by the spinner, so there is nothing left to click twice.
        onAllNodesWithText("Save vacation").assertCountEquals(0)
        assertEquals(1, api.settings.size)
        gate.complete(ApiResult.Ok(Unit))
        waitForIdle()
        onNodeWithText("Save vacation").assertExists()
        assertEquals(1, api.settings.size)
    }

    @Test
    fun clearing_the_vacation_is_one_click_that_sends_enabled_false_and_shows_a_failure_inline() = runComposeUiTest {
        val api =
            FakeScheduleApi().apply {
                seedVacation(LiveOpsScheduleVacation(startTime = start, endTime = "2026-08-15T00:00:00Z"))
                settingsAnswer = { ApiResult.Failure(ApiError(500, "BOOM", reason)) }
            }
        openPage(api)
        onNode(isToggleable()).performScrollTo().performClick()
        waitForIdle()
        clickSave()

        assertEquals(listOf(false), api.settings.map { it.isVacationEnabled })
        onAllNodesWithText(reason).assertCountEquals(1)
    }
}
