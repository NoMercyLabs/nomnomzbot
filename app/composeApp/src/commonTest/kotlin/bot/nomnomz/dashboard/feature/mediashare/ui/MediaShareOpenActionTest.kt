// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.mediashare.ui

import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.hasContentDescription
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.runComposeUiTest
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.LifecycleRegistry
import androidx.lifecycle.compose.LocalLifecycleOwner
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.MediaShareApi
import bot.nomnomz.dashboard.core.network.MediaShareConfig
import bot.nomnomz.dashboard.core.network.MediaShareRequest
import bot.nomnomz.dashboard.core.network.UpdateMediaShareConfigBody
import bot.nomnomz.dashboard.feature.mediashare.state.MediaShareController
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlinx.coroutines.runBlocking

// S-OBS-07 — a `!media <url>` result was captured into the queue but never actually watchable: no click-to-open
// popup, no on-page player. This proves the DASHBOARD half of the fix: clicking a queue row's Play action opens
// THAT row's own real sourceUrl (embedding a player isn't practical here — no first-party WebView/iframe
// primitive exists cross-platform in this design system, so the reachable fix is opening the real clip
// externally). The "not always the first row" case is the actual regression this guards: a naive
// implementation closing over the wrong loop variable, or defaulting to queue[0], would still pass a
// single-row test but fail here.
@OptIn(ExperimentalTestApi::class)
class MediaShareOpenActionTest {

    @Test
    fun clicking_a_rows_play_action_opens_that_rows_own_source_url_not_the_first_row() = runComposeUiTest {
        val first = MediaShareRequest(id = "1", status = "pending", sourceUrl = "https://clips.twitch.tv/first-clip", title = "First clip")
        val second = MediaShareRequest(id = "2", status = "pending", sourceUrl = "https://www.youtube.com/watch?v=second", title = "Second clip")
        val controller = MediaShareController(FakeMediaShareApi(listOf(first, second)))
        runBlocking { controller.load() }

        val opened: MutableList<String> = mutableListOf()

        setContent {
            withLifecycle {
                NomNomzTheme {
                    AppEnvironment("en") {
                        MediaShareScreen(
                            controller = controller,
                            role = ManagementRole.Broadcaster,
                            onOpenMedia = { url -> opened.add(url) },
                        )
                    }
                }
            }
        }
        waitForIdle()

        // Two rows each carry a "Play" affordance; click the SECOND one specifically.
        onAllNodes(hasContentDescription("Play"))[1].performClick()
        waitForIdle()

        assertEquals(listOf(second.sourceUrl), opened)
    }

    @Test
    fun clicking_the_first_rows_play_action_opens_the_first_rows_own_source_url() = runComposeUiTest {
        val first = MediaShareRequest(id = "1", status = "pending", sourceUrl = "https://clips.twitch.tv/first-clip", title = "First clip")
        val second = MediaShareRequest(id = "2", status = "pending", sourceUrl = "https://www.youtube.com/watch?v=second", title = "Second clip")
        val controller = MediaShareController(FakeMediaShareApi(listOf(first, second)))
        runBlocking { controller.load() }

        val opened: MutableList<String> = mutableListOf()

        setContent {
            withLifecycle {
                NomNomzTheme {
                    AppEnvironment("en") {
                        MediaShareScreen(
                            controller = controller,
                            role = ManagementRole.Broadcaster,
                            onOpenMedia = { url -> opened.add(url) },
                        )
                    }
                }
            }
        }
        waitForIdle()

        onAllNodes(hasContentDescription("Play"))[0].performClick()
        waitForIdle()

        assertEquals(listOf(first.sourceUrl), opened)
    }
}

@Composable
private fun withLifecycle(content: @Composable () -> Unit) {
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

private class FakeMediaShareApi(private val snapshot: List<MediaShareRequest>) : MediaShareApi {
    override suspend fun queue(status: String?): ApiResult<List<MediaShareRequest>> = ApiResult.Ok(snapshot)

    override suspend fun next(): ApiResult<MediaShareRequest> = error("stub")

    override suspend fun approve(id: String): ApiResult<MediaShareRequest> = error("stub")

    override suspend fun reject(id: String): ApiResult<MediaShareRequest> = error("stub")

    override suspend fun skip(id: String): ApiResult<MediaShareRequest> = error("stub")

    override suspend fun played(id: String): ApiResult<MediaShareRequest> = error("stub")

    override suspend fun reorder(id: String, position: Int): ApiResult<MediaShareRequest> = error("stub")

    override suspend fun config(): ApiResult<MediaShareConfig> = ApiResult.Ok(MediaShareConfig())

    override suspend fun updateConfig(body: UpdateMediaShareConfigBody): ApiResult<MediaShareConfig> = error("stub")
}
