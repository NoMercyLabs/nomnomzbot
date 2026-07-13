// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.widgets.state

import bot.nomnomz.dashboard.core.editor.CustomCodeEditorIO
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.CreateWidgetBody
import bot.nomnomz.dashboard.core.network.WidgetSummary
import bot.nomnomz.dashboard.core.network.WidgetsApi
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// Proves the Overlays page state machine the screen renders: resolve the active channel, then surface the
// channel's real overlay widgets — empty when there are none, error if either step fails — and prove the writes
// follow through (a toggle persists the flipped flag, a delete really removes the row). The screen is a pure
// projection of this, so testing it proves the page shows real data (no fabricated rows), exposes each
// overlay's browser-source URL, and degrades cleanly.
class WidgetsControllerTest {

    @Test
    fun load_surfaces_the_channel_widgets_with_their_overlay_urls() = runTest {
        val controller =
            widgetsController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                RecordingWidgetsApi(
                    ApiResult.Ok(
                        listOf(
                            WidgetSummary(
                                id = "w-1",
                                name = "Alerts",
                                type = "alerts",
                                isEnabled = true,
                                overlayUrl = "http://localhost:8080/overlay?widgetId=w-1&token=tok",
                            )
                        )
                    )
                ),
            )

        controller.load()

        val state: WidgetsState = controller.state.value
        assertTrue(state is WidgetsState.Ready)
        val widgets: List<WidgetSummary> = (state as WidgetsState.Ready).widgets
        assertEquals(1, widgets.size)
        val widget: WidgetSummary = widgets.first()
        assertEquals("Alerts", widget.name)
        assertEquals("alerts", widget.type)
        assertEquals(true, widget.isEnabled)
        // The browser-source URL — the page's core value (paste into OBS) — survives intact to the row.
        assertEquals("http://localhost:8080/overlay?widgetId=w-1&token=tok", widget.overlayUrl)
        assertNull(state.actionError)
    }

    @Test
    fun load_is_empty_when_the_channel_has_no_widgets() = runTest {
        val controller =
            widgetsController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                RecordingWidgetsApi(ApiResult.Ok(emptyList())),
            )

        controller.load()

        assertTrue(controller.state.value is WidgetsState.Empty)
    }

    @Test
    fun load_errors_when_no_channel_resolves() = runTest {
        val controller =
            widgetsController(
                FakeChannelsApi(ApiResult.Failure(ApiError(404, "NO_CHANNEL", "none onboarded"))),
                RecordingWidgetsApi(ApiResult.Ok(emptyList())),
            )

        controller.load()

        assertTrue(controller.state.value is WidgetsState.Error)
    }

    @Test
    fun load_errors_when_the_list_call_fails() = runTest {
        val controller =
            widgetsController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                RecordingWidgetsApi(ApiResult.Failure(ApiError(500, "ERR", "boom"))),
            )

        controller.load()

        assertTrue(controller.state.value is WidgetsState.Error)
    }

    @Test
    fun toggle_puts_only_the_enabled_flag_then_reloads_with_the_flipped_state() = runTest {
        val widgetsApi =
            RecordingWidgetsApi(
                ApiResult.Ok(listOf(WidgetSummary(id = "w-1", name = "Alerts", isEnabled = true)))
            )
        val controller =
            widgetsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), widgetsApi)
        controller.load()

        controller.toggleWidget(widgetId = "w-1", enabled = false)

        // A toggle records exactly the widget + the new flag.
        assertEquals(1, widgetsApi.toggled.size)
        val toggle: Pair<String, Boolean> = widgetsApi.toggled.first()
        assertEquals("w-1", toggle.first)
        assertEquals(false, toggle.second)
        assertEquals("ch1", widgetsApi.toggledChannelId)

        // The reload reflects the persisted flip — the consequence of the action, not merely the call.
        val state: WidgetsState = controller.state.value
        assertTrue(state is WidgetsState.Ready)
        assertEquals(false, (state as WidgetsState.Ready).widgets.first().isEnabled)
    }

    @Test
    fun delete_removes_the_widget_then_reloads_to_empty() = runTest {
        val widgetsApi =
            RecordingWidgetsApi(
                ApiResult.Ok(listOf(WidgetSummary(id = "w-1", name = "Alerts", isEnabled = true)))
            )
        val controller =
            widgetsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), widgetsApi)
        controller.load()
        assertTrue(controller.state.value is WidgetsState.Ready)

        controller.deleteWidget(widgetId = "w-1")

        assertEquals(listOf("w-1"), widgetsApi.deleted)
        // The store is now empty, so the post-delete reload lands on Empty — the row is really gone.
        assertTrue(controller.state.value is WidgetsState.Empty)
    }

    @Test
    fun a_failed_write_surfaces_the_error_over_the_kept_list() = runTest {
        val widgetsApi =
            RecordingWidgetsApi(
                ApiResult.Ok(listOf(WidgetSummary(id = "w-1", name = "Alerts", isEnabled = true))),
                writeResult = ApiResult.Failure(ApiError(403, "FORBIDDEN", "no permission")),
            )
        val controller =
            widgetsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), widgetsApi)
        controller.load()

        controller.deleteWidget(widgetId = "w-1")

        // The list is kept (not blown away) and the failure is surfaced on it.
        val state: WidgetsState = controller.state.value
        assertTrue(state is WidgetsState.Ready)
        assertEquals(1, (state as WidgetsState.Ready).widgets.size)
        assertEquals("no permission", state.actionError)
    }

    @Test
    fun edit_widget_code_opens_seeded_saves_the_edited_source_then_reloads_with_it() = runTest {
        val widgetsApi =
            RecordingWidgetsApi(
                ApiResult.Ok(
                    listOf(
                        WidgetSummary(
                            id = "w-1",
                            name = "Timer",
                            type = "custom",
                            isEnabled = true,
                            customCode = "<old/>",
                        )
                    )
                )
            )
        val editor = FakeCodeEditor(result = "<new>hi</new>")
        val controller =
            widgetsController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                widgetsApi,
                editor,
            )
        controller.load()

        controller.editWidgetCode(
            WidgetSummary(id = "w-1", name = "Timer", type = "custom", customCode = "<old/>")
        )

        // The editor was opened seeded with the widget's current source (title + code) — proving the round-trip
        // reads the real stored code, not a blank buffer.
        assertEquals("Timer" to "<old/>", editor.openedWith)
        // The edited source was persisted for exactly that widget.
        assertEquals(listOf("w-1" to "<new>hi</new>"), widgetsApi.savedCode)
        // The reload reflects the saved code — the consequence of the action, not merely the call.
        val state: WidgetsState = controller.state.value
        assertTrue(state is WidgetsState.Ready)
        assertEquals("<new>hi</new>", (state as WidgetsState.Ready).widgets.first().customCode)
    }

    @Test
    fun edit_widget_code_cancelled_persists_nothing() = runTest {
        val widgetsApi =
            RecordingWidgetsApi(
                ApiResult.Ok(
                    listOf(
                        WidgetSummary(id = "w-1", name = "Timer", type = "custom", customCode = "<x/>")
                    )
                )
            )
        val controller =
            widgetsController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                widgetsApi,
                FakeCodeEditor(result = null), // the operator cancelled the editor
            )
        controller.load()

        controller.editWidgetCode(
            WidgetSummary(id = "w-1", name = "Timer", type = "custom", customCode = "<x/>")
        )

        // A cancelled edit writes nothing — the widget's stored code is left untouched.
        assertTrue(widgetsApi.savedCode.isEmpty())
    }
}

// Builds a controller with a default (cancelling) code editor so the tests that don't exercise the editor stay
// unchanged; the editor tests pass an explicit [FakeCodeEditor].
private fun widgetsController(
    channelsApi: ChannelsApi,
    widgetsApi: WidgetsApi,
    editor: CustomCodeEditorIO = FakeCodeEditor(),
): WidgetsController = WidgetsController(channelsApi, widgetsApi, editor)

// A fake editor that returns [result] from edit() (null = cancelled) and records what it was opened with, so a
// test can assert the editor is seeded with the widget's real current source.
private class FakeCodeEditor(private val result: String? = null) : CustomCodeEditorIO {
    var openedWith: Pair<String, String>? = null

    override suspend fun edit(title: String, initialCode: String, language: String): String? {
        openedWith = title to initialCode
        return result
    }
}

private class FakeChannelsApi(private val result: ApiResult<ChannelSummary>) : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = result

    override suspend fun list(): ApiResult<List<ChannelSummary>> = ApiResult.Ok(emptyList())

    override suspend fun join(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun leave(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun reset(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun deleteChannel(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun channelScopes(channelId: String) = error("stub")
    override suspend fun startChannelBotConnect(channelId: String) = error("stub")
    override suspend fun channelBotStatus(channelId: String) = error("stub")
    override suspend fun disconnectChannelBot(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun moderatedChannels(): ApiResult<List<ModeratedChannel>> = ApiResult.Ok(emptyList())
}

// A recording fake that behaves like the backend store: list() returns the live store, and each successful
// write mutates the store so the controller's post-write reload observes the real consequence (a flipped flag,
// a removed row) — not merely that a call happened. [writeResult] forces every write to fail (the store is left
// untouched) to exercise the error path. A list-level failure is modelled by passing a Failure as the initial
// result.
private class RecordingWidgetsApi(
    initial: ApiResult<List<WidgetSummary>>,
    private val writeResult: ApiResult<Unit> = ApiResult.Ok(Unit),
) : WidgetsApi {
    private val listFailure: ApiError? = (initial as? ApiResult.Failure)?.error
    private val store: MutableList<WidgetSummary> =
        (initial as? ApiResult.Ok)?.value?.toMutableList() ?: mutableListOf()

    val toggled: MutableList<Pair<String, Boolean>> = mutableListOf()
    var toggledChannelId: String? = null
    val deleted: MutableList<String> = mutableListOf()
    val savedCode: MutableList<Pair<String, String>> = mutableListOf()

    override suspend fun list(channelId: String): ApiResult<List<WidgetSummary>> =
        listFailure?.let { ApiResult.Failure(it) } ?: ApiResult.Ok(store.toList())

    override suspend fun setEnabled(
        channelId: String,
        widgetId: String,
        enabled: Boolean,
    ): ApiResult<Unit> {
        toggled += widgetId to enabled
        toggledChannelId = channelId
        if (writeResult is ApiResult.Ok) {
            val index: Int = store.indexOfFirst { it.id == widgetId }
            if (index >= 0) store[index] = store[index].copy(isEnabled = enabled)
        }
        return writeResult
    }

    override suspend fun delete(channelId: String, widgetId: String): ApiResult<Unit> {
        deleted += widgetId
        if (writeResult is ApiResult.Ok) store.removeAll { it.id == widgetId }
        return writeResult
    }

    override suspend fun create(channelId: String, body: CreateWidgetBody): ApiResult<WidgetSummary> =
        ApiResult.Ok(WidgetSummary(id = "new-widget", type = body.type, name = body.name))

    override suspend fun rename(channelId: String, widgetId: String, name: String): ApiResult<Unit> =
        ApiResult.Ok(Unit)

    // Records the saved code and, on success, writes it back to the store so the controller's post-write reload
    // observes the persisted source (a real consequence), not merely that the call happened.
    override suspend fun saveCode(channelId: String, widgetId: String, code: String): ApiResult<Unit> {
        savedCode += widgetId to code
        if (writeResult is ApiResult.Ok) {
            val index: Int = store.indexOfFirst { it.id == widgetId }
            if (index >= 0) store[index] = store[index].copy(customCode = code)
        }
        return writeResult
    }

    override suspend fun clone(channelId: String, sourceType: String, sourceName: String): ApiResult<WidgetSummary> =
        ApiResult.Ok(WidgetSummary(id = "cloned-widget", type = sourceType, name = "$sourceName (copy)"))
}
