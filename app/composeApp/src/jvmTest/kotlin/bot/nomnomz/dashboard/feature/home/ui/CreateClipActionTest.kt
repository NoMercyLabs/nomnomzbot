// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.home.ui

import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.LiveOpsClipStub
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlinx.coroutines.CompletableDeferred

// SH1-HOM-2 on the Home "Create clip" action: a tap shows progress, a second tap while the create is pending
// makes no second clip, and a refusal shows its reason next to the button and lets the operator try again.
@OptIn(ExperimentalTestApi::class)
class CreateClipActionTest {
    private val stub: LiveOpsClipStub = LiveOpsClipStub(id = "clip1", editUrl = "https://clips.example/edit/clip1")
    private val reason: String = "Twitch refused: the channel is offline."

    @Test
    fun a_double_tap_while_pending_creates_exactly_one_clip_and_shows_progress() = runComposeUiTest {
        val gate: CompletableDeferred<ApiResult<LiveOpsClipStub>> = CompletableDeferred()
        var calls: Int = 0
        val created: MutableList<LiveOpsClipStub> = mutableListOf()
        setContent {
            NomNomzTheme {
                AppEnvironment("en") {
                    CreateClipAction(
                        createClip = {
                            calls++
                            gate.await()
                        },
                        onCreated = { created += it },
                        enabled = true,
                    )
                }
            }
        }
        waitForIdle()

        onNodeWithContentDescription("Create Clip").performClick()
        waitForIdle()
        onNodeWithContentDescription("Creating clip…").assertExists()
        onNodeWithContentDescription("Creating clip…").performClick()
        waitForIdle()

        assertEquals(1, calls)
        gate.complete(ApiResult.Ok(stub))
        waitForIdle()
        assertEquals(listOf(stub), created)
        onNodeWithContentDescription("Create Clip").assertExists()
    }

    @Test
    fun a_refused_create_shows_the_reason_inline_and_the_button_works_again() = runComposeUiTest {
        var calls: Int = 0
        val created: MutableList<LiveOpsClipStub> = mutableListOf()
        setContent {
            NomNomzTheme {
                AppEnvironment("en") {
                    CreateClipAction(
                        createClip = {
                            calls++
                            ApiResult.Failure(ApiError(400, "NOT_LIVE", reason))
                        },
                        onCreated = { created += it },
                        enabled = true,
                    )
                }
            }
        }
        waitForIdle()

        onNodeWithContentDescription("Create Clip").performClick()
        waitForIdle()

        assertEquals(1, calls)
        assertEquals(emptyList<LiveOpsClipStub>(), created)
        onNodeWithText(reason).assertExists()
        onNodeWithContentDescription("Create Clip").performClick()
        waitForIdle()
        assertEquals(2, calls)
    }
}
