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

import bot.nomnomz.dashboard.core.editor.CompileFeedback
import bot.nomnomz.dashboard.core.editor.CustomCodeEditorIO
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.CreateWidgetBody
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.WidgetSummary
import bot.nomnomz.dashboard.core.network.WidgetTemplate
import bot.nomnomz.dashboard.core.network.WidgetVersionDetail
import bot.nomnomz.dashboard.core.network.WidgetVersionSummary
import bot.nomnomz.dashboard.core.network.WidgetsApi
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// Proves the Overlays page state machine the screen renders: resolve the active channel, then surface the
// channel's real overlay widgets — empty when there are none, error if either step fails — and prove the writes
// follow through. The widget backend is compile-on-save: editing loads the active version's source, "Save &
// Compile" builds the next version and surfaces the build outcome inline (a real success message or the real
// build error — never a silent no-op), create posts the chosen framework and seeds the editor, and clone hits
// the real clone endpoint. The screen is a pure projection of this controller.
class WidgetsControllerTest {

    private val messages = WidgetEditorMessages(compiled = "compiled-ok", buildFailed = "build-failed")

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
                                framework = "vanilla",
                                source = "first_party",
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
        assertEquals("vanilla", widget.framework)
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
    fun edit_widget_code_loads_the_active_version_source_compiles_and_reports_success() = runTest {
        val widgetsApi =
            RecordingWidgetsApi(
                ApiResult.Ok(
                    listOf(
                        WidgetSummary(id = "w-1", name = "Timer", framework = "vanilla", activeVersionId = "v-1")
                    )
                ),
                versionSource = "<old/>",
                compileResult = ApiResult.Ok(WidgetVersionDetail(versionNumber = 2, buildStatus = "success")),
            )
        val editor = FakeCodeEditor(toSave = listOf("<new>hi</new>"))
        val controller =
            widgetsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), widgetsApi, editor)
        controller.load()

        controller.editWidgetCode(
            WidgetSummary(id = "w-1", name = "Timer", framework = "vanilla", activeVersionId = "v-1"),
            messages,
        )

        // The editor opened seeded with the ACTIVE VERSION's source (loaded via getVersion) — not a blank buffer.
        assertEquals("Timer" to "<old/>", editor.openedWith)
        assertEquals(listOf("v-1"), widgetsApi.loadedVersionIds)
        // "Save & Compile" compiled exactly the edited source for that widget — a real version build, not a no-op.
        assertEquals(listOf("w-1" to "<new>hi</new>"), widgetsApi.compiled)
        // The build outcome was reported inline as success.
        assertEquals(listOf(CompileFeedback(ok = true, message = "compiled-ok")), editor.feedbacks)
    }

    @Test
    fun edit_widget_code_surfaces_the_real_build_error_not_a_silent_no_op() = runTest {
        val widgetsApi =
            RecordingWidgetsApi(
                ApiResult.Ok(
                    listOf(
                        WidgetSummary(id = "w-1", name = "Timer", framework = "vue", activeVersionId = "v-1")
                    )
                ),
                versionSource = "<old/>",
                compileResult =
                    ApiResult.Ok(
                        WidgetVersionDetail(buildStatus = "error", buildError = "Unexpected token '<' at 3:1")
                    ),
            )
        val editor = FakeCodeEditor(toSave = listOf("<broken"))
        val controller =
            widgetsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), widgetsApi, editor)
        controller.load()

        controller.editWidgetCode(
            WidgetSummary(id = "w-1", name = "Timer", framework = "vue", activeVersionId = "v-1"),
            messages,
        )

        // The compile ran and the REAL backend build error is surfaced inline (ok = false), proving the save
        // path is a live compile whose failure is shown — not the old silent PUT no-op.
        assertEquals(listOf("w-1" to "<broken"), widgetsApi.compiled)
        assertEquals(
            listOf(CompileFeedback(ok = false, message = "Unexpected token '<' at 3:1")),
            editor.feedbacks,
        )
    }

    @Test
    fun edit_widget_code_closed_without_saving_compiles_nothing() = runTest {
        val widgetsApi =
            RecordingWidgetsApi(
                ApiResult.Ok(
                    listOf(
                        WidgetSummary(id = "w-1", name = "Timer", framework = "vanilla", activeVersionId = "v-1")
                    )
                ),
                versionSource = "<x/>",
            )
        val controller =
            widgetsController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                widgetsApi,
                FakeCodeEditor(toSave = emptyList()), // the operator closed without saving
            )
        controller.load()

        controller.editWidgetCode(
            WidgetSummary(id = "w-1", name = "Timer", framework = "vanilla", activeVersionId = "v-1"),
            messages,
        )

        // Closing the editor without a save compiles nothing — no version is built.
        assertTrue(widgetsApi.compiled.isEmpty())
    }

    @Test
    fun create_posts_the_chosen_framework_and_seeds_the_editor_with_the_template_source() = runTest {
        val widgetsApi = RecordingWidgetsApi(ApiResult.Ok(emptyList()))
        val editor = FakeCodeEditor(toSave = emptyList())
        val controller =
            widgetsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), widgetsApi, editor)
        controller.load()

        controller.createWidget(name = "My Timer", framework = "vue", seedSource = "<template/>", messages = messages)

        // Create posts the framework (renamed from `type`), not a legacy type.
        assertEquals(listOf(CreateWidgetBody(name = "My Timer", framework = "vue")), widgetsApi.created)
        // …then opens the editor seeded with the chosen template's source so Save compiles the first version.
        assertEquals("My Timer" to "<template/>", editor.openedWith)
    }

    @Test
    fun clone_calls_the_real_clone_endpoint_with_the_installed_widget_id() = runTest {
        val widgetsApi =
            RecordingWidgetsApi(
                ApiResult.Ok(listOf(WidgetSummary(id = "w-1", name = "Alerts", source = "first_party")))
            )
        val controller =
            widgetsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), widgetsApi)
        controller.load()

        controller.cloneWidget(widgetId = "w-1")

        // Real backend clone (not a client-side "Copy of…" re-POST): the source installed-widget id is sent.
        assertEquals(listOf("w-1"), widgetsApi.clonedIds)
    }

    @Test
    fun rollback_calls_the_endpoint_then_reloads() = runTest {
        val widgetsApi =
            RecordingWidgetsApi(
                ApiResult.Ok(listOf(WidgetSummary(id = "w-1", name = "Alerts", activeVersionId = "v-2")))
            )
        val controller =
            widgetsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), widgetsApi)
        controller.load()

        controller.rollbackVersion(widgetId = "w-1", versionId = "v-1")

        assertEquals(listOf("w-1" to "v-1"), widgetsApi.rolledBack)
        assertTrue(controller.state.value is WidgetsState.Ready)
    }
}

// Builds a controller with a default (immediately-closing) code editor so the tests that don't exercise the
// editor stay unchanged; the editor tests pass an explicit [FakeCodeEditor].
private fun widgetsController(
    channelsApi: ChannelsApi,
    widgetsApi: WidgetsApi,
    editor: CustomCodeEditorIO = FakeCodeEditor(),
): WidgetsController = WidgetsController(channelsApi, widgetsApi, editor)

// A fake compile-on-save editor. Records what it opened with, "presses Save & Compile" for each source in
// [toSave] (invoking the caller's compile callback and capturing the returned feedback), then closes. An empty
// [toSave] models the operator closing the editor without saving.
private class FakeCodeEditor(private val toSave: List<String> = emptyList()) : CustomCodeEditorIO {
    var openedWith: Pair<String, String>? = null
    val feedbacks: MutableList<CompileFeedback> = mutableListOf()

    override suspend fun editAndCompile(
        title: String,
        initialCode: String,
        language: String,
        compile: suspend (String) -> CompileFeedback,
    ) {
        openedWith = title to initialCode
        for (source in toSave) {
            feedbacks += compile(source)
        }
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
// write mutates the store so the controller's post-write reload observes the real consequence (a flipped flag, a
// removed row) — not merely that a call happened. [writeResult] forces every simple write to fail (the store is
// left untouched) to exercise the error path. [versionSource] is what getVersion returns as the editable source;
// [compileResult] is the build outcome each compile reports; [versions]/[templates] back the version + template
// lists. A list-level failure is modelled by passing a Failure as the initial result.
private class RecordingWidgetsApi(
    initial: ApiResult<List<WidgetSummary>>,
    private val writeResult: ApiResult<Unit> = ApiResult.Ok(Unit),
    private val versionSource: String = "",
    private val compileResult: ApiResult<WidgetVersionDetail> =
        ApiResult.Ok(WidgetVersionDetail(buildStatus = "success")),
    private val versions: List<WidgetVersionSummary> = emptyList(),
    private val templates: ApiResult<List<WidgetTemplate>> = ApiResult.Ok(emptyList()),
) : WidgetsApi {
    private val listFailure: ApiError? = (initial as? ApiResult.Failure)?.error
    private val store: MutableList<WidgetSummary> =
        (initial as? ApiResult.Ok)?.value?.toMutableList() ?: mutableListOf()

    val toggled: MutableList<Pair<String, Boolean>> = mutableListOf()
    var toggledChannelId: String? = null
    val deleted: MutableList<String> = mutableListOf()
    val created: MutableList<CreateWidgetBody> = mutableListOf()
    val compiled: MutableList<Pair<String, String>> = mutableListOf()
    val loadedVersionIds: MutableList<String> = mutableListOf()
    val clonedIds: MutableList<String> = mutableListOf()
    val rolledBack: MutableList<Pair<String, String>> = mutableListOf()

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

    override suspend fun create(channelId: String, body: CreateWidgetBody): ApiResult<WidgetSummary> {
        created += body
        return ApiResult.Ok(WidgetSummary(id = "new-widget", framework = body.framework, name = body.name))
    }

    override suspend fun rename(channelId: String, widgetId: String, name: String): ApiResult<Unit> =
        ApiResult.Ok(Unit)

    // Records the compiled source so the controller's compile-on-save round-trip is observable (a real version
    // build), and returns the configured build outcome so a success or a failure surfaces inline.
    override suspend fun compile(
        channelId: String,
        widgetId: String,
        sourceCode: String,
    ): ApiResult<WidgetVersionDetail> {
        compiled += widgetId to sourceCode
        return compileResult
    }

    override suspend fun listVersions(
        channelId: String,
        widgetId: String,
    ): ApiResult<List<WidgetVersionSummary>> = ApiResult.Ok(versions)

    override suspend fun getVersion(
        channelId: String,
        widgetId: String,
        versionId: String,
    ): ApiResult<WidgetVersionDetail> {
        loadedVersionIds += versionId
        return ApiResult.Ok(WidgetVersionDetail(id = versionId, widgetId = widgetId, sourceCode = versionSource))
    }

    override suspend fun rollback(
        channelId: String,
        widgetId: String,
        versionId: String,
    ): ApiResult<WidgetSummary> {
        rolledBack += widgetId to versionId
        return ApiResult.Ok(store.firstOrNull { it.id == widgetId } ?: WidgetSummary(id = widgetId))
    }

    override suspend fun listTemplates(channelId: String): ApiResult<List<WidgetTemplate>> = templates

    override suspend fun clone(channelId: String, installedWidgetId: String): ApiResult<WidgetSummary> {
        clonedIds += installedWidgetId
        return ApiResult.Ok(WidgetSummary(id = "cloned-widget", name = "clone", source = "custom"))
    }
}
