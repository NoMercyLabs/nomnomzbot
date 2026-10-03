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

import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.hasSetTextAction
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performTextInput
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.GalleryItemSummary
import bot.nomnomz.dashboard.core.network.GalleryListRequest
import bot.nomnomz.dashboard.core.network.GalleryPage
import kotlinx.coroutines.CompletableDeferred
import kotlin.test.Test
import kotlin.test.assertEquals

// The gallery browse dialog must say what is happening: loading while a fetch runs, "empty" only after a
// fetch that finished with nothing, and an inline error (with a retry) when "Load more" fails.
@OptIn(ExperimentalTestApi::class)
class GalleryBrowseDialogTest {
    private val firstItem = GalleryItemSummary(id = "g-1", name = "Alert box", framework = "vue")
    private val emptyText: String = "No gallery widgets available"
    private val loadingText: String = "Loading the gallery…"

    private fun androidx.compose.ui.test.ComposeUiTest.show(
        loadGallery: suspend (GalleryListRequest) -> ApiResult<GalleryPage>,
    ) =
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    GalleryBrowseDialog(
                        manage = ManageDecision.Allowed,
                        loadGallery = loadGallery,
                        onInstall = {},
                        onClone = {},
                        onDismiss = {},
                    )
                }
            }
        }

    @Test
    fun a_reload_in_flight_shows_loading_not_the_empty_message() = runComposeUiTest {
        val second: CompletableDeferred<ApiResult<GalleryPage>> = CompletableDeferred()
        var calls = 0
        show { _ ->
            calls++
            if (calls == 1) ApiResult.Ok(GalleryPage(items = listOf(firstItem))) else second.await()
        }
        waitForIdle()
        mainClock.advanceTimeBy(1_000)
        onNodeWithText("Alert box", substring = true).assertExists()

        onNode(hasSetTextAction()).performTextInput("zzz")
        mainClock.advanceTimeBy(1_000)
        waitForIdle()

        assertEquals(2, calls)
        onNodeWithText(loadingText).assertExists()
        onAllNodesWithText(emptyText).assertCountEquals(0)

        second.complete(ApiResult.Ok(GalleryPage(items = emptyList())))
        mainClock.advanceTimeBy(1_000)
        waitForIdle()
        onNodeWithText(emptyText).assertExists()
    }

    @Test
    fun a_failed_load_more_shows_an_inline_error_keeps_the_items_and_can_retry() = runComposeUiTest {
        var calls = 0
        show { _ ->
            calls++
            when {
                calls == 1 -> ApiResult.Ok(GalleryPage(items = listOf(firstItem), hasMore = true, nextPage = 2))
                calls == 2 -> ApiResult.Failure(ApiError(503, "UNAVAILABLE", "gallery offline"))
                else ->
                    ApiResult.Ok(
                        GalleryPage(items = listOf(GalleryItemSummary(id = "g-2", name = "Chat box", framework = "vue")))
                    )
            }
        }
        waitForIdle()
        mainClock.advanceTimeBy(1_000)
        onNodeWithText("Load more").performClick()
        waitForIdle()

        onNodeWithText("gallery offline", substring = true).assertExists()
        onNodeWithText("Alert box", substring = true).assertExists()

        onNodeWithText("Try again").performClick()
        waitForIdle()
        onNodeWithText("Chat box", substring = true).assertExists()
        onAllNodesWithText("gallery offline", substring = true).assertCountEquals(0)
    }
}
