// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.widgets.ui

import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.onNodeWithContentDescription
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
import bot.nomnomz.dashboard.core.network.GalleryItemSummary
import bot.nomnomz.dashboard.core.network.GalleryPage
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.widgets.state.FakeChannelsApi
import bot.nomnomz.dashboard.feature.widgets.state.FakeWidgetGalleryApi
import bot.nomnomz.dashboard.feature.widgets.state.RecordingWidgetsApi
import bot.nomnomz.dashboard.feature.widgets.state.WidgetsController
import bot.nomnomz.dashboard.feature.widgets.state.widgetsController
import kotlin.test.Test
import kotlin.test.assertEquals

// The Overlays page splits into Gallery (the catalogue a streamer browses) and My overlays (what they run). These
// prove the hand-off between the two: the catalogue lists what the API returns, Install calls the API for that
// item, and the installed overlay then shows in My overlays with its OBS browser-source URL.
@OptIn(ExperimentalTestApi::class)
class OverlaysGalleryToMineTest {
    private val item: GalleryItemSummary = GalleryItemSummary(id = "g-1", name = "Goal bar", framework = "vue")

    @Test
    fun gallery_lists_the_catalogue_items_the_api_returns() = runComposeUiTest {
        val api: RecordingWidgetsApi = RecordingWidgetsApi(ApiResult.Ok(emptyList()))
        setContent { Host(api, OverlaysSection.Gallery) }
        mainClock.advanceTimeBy(1_000)
        waitForIdle()

        onNodeWithText("Goal bar", substring = true).assertExists()
        assertEquals(emptyList(), api.installed)
    }

    @Test
    fun installing_from_gallery_calls_install_and_the_overlay_then_shows_in_my_overlays_with_its_obs_url() =
        runComposeUiTest {
            val api: RecordingWidgetsApi = RecordingWidgetsApi(ApiResult.Ok(emptyList()))
            setContent { Host(api, OverlaysSection.Gallery) }
            mainClock.advanceTimeBy(1_000)
            waitForIdle()

            onNodeWithContentDescription("Install Goal bar").performClick()
            waitForIdle()

            assertEquals(listOf("g-1"), api.installed)
            onNodeWithText("https://overlays.example/o/installed-g-1", substring = true).assertExists()
            onNodeWithText("Gallery Widget", substring = true, useUnmergedTree = true).assertExists()
        }

    @Test
    fun an_empty_my_overlays_offers_one_button_to_the_gallery() = runComposeUiTest {
        val api: RecordingWidgetsApi = RecordingWidgetsApi(ApiResult.Ok(emptyList()))
        setContent { Host(api, OverlaysSection.Mine) }
        waitForIdle()

        onNodeWithText("No overlays yet").assertExists()
        onNodeWithText("Browse gallery", useUnmergedTree = true).performClick()
        waitForIdle()
        mainClock.advanceTimeBy(1_000)

        onNodeWithText("Goal bar", substring = true).assertExists()
    }

    @Composable
    private fun Host(api: RecordingWidgetsApi, start: OverlaysSection) {
        val controller: WidgetsController =
            widgetsController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                api,
                galleryApi = FakeWidgetGalleryApi(ApiResult.Ok(GalleryPage(items = listOf(item)))),
            )
        val owner: LifecycleOwner =
            object : LifecycleOwner {
                override val lifecycle: Lifecycle = LifecycleRegistry.createUnsafe(this)
            }
        (owner.lifecycle as LifecycleRegistry).apply {
            currentState = Lifecycle.State.CREATED
            currentState = Lifecycle.State.STARTED
            currentState = Lifecycle.State.RESUMED
        }
        var section: OverlaysSection by remember { mutableStateOf(start) }
        CompositionLocalProvider(LocalLifecycleOwner provides owner) {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    WidgetsScreen(
                        controller = controller,
                        role = ManagementRole.Broadcaster,
                        section = section,
                        onOpenSection = { section = it },
                    )
                }
            }
        }
    }
}
