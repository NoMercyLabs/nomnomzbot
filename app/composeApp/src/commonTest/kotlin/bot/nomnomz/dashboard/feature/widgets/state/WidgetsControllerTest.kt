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
import bot.nomnomz.dashboard.core.editor.ProjectEditorIO
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.SdkTypesApi
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.CreateWidgetBody
import bot.nomnomz.dashboard.core.network.GalleryItemDetail
import bot.nomnomz.dashboard.core.network.GalleryItemSummary
import bot.nomnomz.dashboard.core.network.GalleryListRequest
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.ProjectDto
import bot.nomnomz.dashboard.core.network.ProjectManifestDto
import bot.nomnomz.dashboard.core.network.PinGalleryItemBody
import bot.nomnomz.dashboard.core.network.ReviewGalleryItemBody
import bot.nomnomz.dashboard.core.network.SubmitGalleryItemBody
import bot.nomnomz.dashboard.core.network.WidgetGalleryApi
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

    private val messages = WidgetEditorMessages(compiled = "compiled-ok")

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
    fun edit_widget_code_loads_the_project_saves_it_and_reports_success() = runTest {
        val widgetsApi =
            RecordingWidgetsApi(
                ApiResult.Ok(
                    listOf(
                        WidgetSummary(id = "w-1", name = "Timer", framework = "vanilla", activeVersionId = "v-1")
                    )
                ),
                projectResult =
                    ApiResult.Ok(
                        ProjectDto(
                            files = mapOf("index.html" to "<old/>"),
                            manifest =
                                ProjectManifestDto(entry = "index.html", kind = "widget", framework = "vanilla"),
                        )
                    ),
                putProjectResult = ApiResult.Ok(WidgetVersionDetail(versionNumber = 2, buildStatus = "success")),
            )
        val editor = FakeProjectEditor(toSave = listOf("<new>hi</new>"))
        val controller =
            widgetsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), widgetsApi, editor)
        controller.load()

        controller.editWidgetCode(
            WidgetSummary(id = "w-1", name = "Timer", framework = "vanilla", activeVersionId = "v-1"),
            messages,
        )

        // The editor opened on the widget's real project (loaded via getProject), seeded with the entry source.
        assertEquals("Timer", editor.openedTitle)
        assertEquals("index.html", editor.openedEntry)
        assertEquals("<old/>", editor.openedEntryContent)
        assertEquals(listOf("w-1"), widgetsApi.loadedProjectIds)
        // "Save & Compile" PUT exactly the edited project for that widget — a real server build, not a no-op.
        assertEquals(listOf("w-1" to mapOf("index.html" to "<new>hi</new>")), widgetsApi.savedProjects)
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
                projectResult =
                    ApiResult.Ok(
                        ProjectDto(
                            files = mapOf("index.vue" to "<old/>"),
                            manifest = ProjectManifestDto(entry = "index.vue", kind = "widget", framework = "vue"),
                        )
                    ),
                // The server rejects a broken build with a failure Result (nothing persisted) — not a 200.
                putProjectResult = ApiResult.Failure(ApiError(400, "BUILD", "Unexpected token '<' at 3:1")),
            )
        val editor = FakeProjectEditor(toSave = listOf("<broken"))
        val controller =
            widgetsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), widgetsApi, editor)
        controller.load()

        controller.editWidgetCode(
            WidgetSummary(id = "w-1", name = "Timer", framework = "vue", activeVersionId = "v-1"),
            messages,
        )

        // The project was PUT and the REAL backend build error is surfaced inline (ok = false), proving the save
        // path is a live server build whose failure is shown — not a silent no-op.
        assertEquals(listOf("w-1" to mapOf("index.vue" to "<broken")), widgetsApi.savedProjects)
        assertEquals(
            listOf(CompileFeedback(ok = false, message = "Unexpected token '<' at 3:1")),
            editor.feedbacks,
        )
    }

    @Test
    fun edit_widget_code_closed_without_saving_saves_nothing() = runTest {
        val widgetsApi =
            RecordingWidgetsApi(
                ApiResult.Ok(
                    listOf(
                        WidgetSummary(id = "w-1", name = "Timer", framework = "vanilla", activeVersionId = "v-1")
                    )
                ),
                projectResult =
                    ApiResult.Ok(
                        ProjectDto(
                            files = mapOf("index.html" to "<x/>"),
                            manifest =
                                ProjectManifestDto(entry = "index.html", kind = "widget", framework = "vanilla"),
                        )
                    ),
            )
        val controller =
            widgetsController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                widgetsApi,
                FakeProjectEditor(toSave = emptyList()), // the operator closed without saving
            )
        controller.load()

        controller.editWidgetCode(
            WidgetSummary(id = "w-1", name = "Timer", framework = "vanilla", activeVersionId = "v-1"),
            messages,
        )

        // Closing the editor without a save persists nothing — no project is PUT, no version is built.
        assertTrue(widgetsApi.savedProjects.isEmpty())
    }

    @Test
    fun create_posts_the_chosen_framework_and_seeds_the_editor_with_the_template_source() = runTest {
        val widgetsApi = RecordingWidgetsApi(ApiResult.Ok(emptyList()))
        val editor = FakeProjectEditor(toSave = emptyList())
        val controller =
            widgetsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), widgetsApi, editor)
        controller.load()

        controller.createWidget(name = "My Timer", framework = "vue", seedSource = "<template/>", messages = messages)

        // Create posts the framework (renamed from `type`), not a legacy type.
        assertEquals(listOf(CreateWidgetBody(name = "My Timer", framework = "vue")), widgetsApi.created)
        // …then opens the editor on a seeded one-file project: the vue entry (index.vue) carries the template source.
        assertEquals("My Timer", editor.openedTitle)
        assertEquals("index.vue", editor.openedEntry)
        assertEquals("<template/>", editor.openedEntryContent)
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
    fun browse_gallery_lists_the_public_catalogue_and_threads_the_framework_filter() = runTest {
        val galleryApi =
            FakeWidgetGalleryApi(
                ApiResult.Ok(
                    listOf(
                        GalleryItemSummary(
                            id = "g-1",
                            name = "Follower Alert",
                            framework = "vue",
                            trustTier = "first_party",
                            installCount = 42,
                        )
                    )
                )
            )
        val controller =
            widgetsController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                RecordingWidgetsApi(ApiResult.Ok(emptyList())),
                galleryApi = galleryApi,
            )

        val result: ApiResult<List<GalleryItemSummary>> =
            controller.listGallery(GalleryListRequest(framework = "vue"))

        // The catalogue items surface with their browse fields intact (name, trust tier, install count) …
        assertTrue(result is ApiResult.Ok)
        val items: List<GalleryItemSummary> = (result as ApiResult.Ok).value
        assertEquals(1, items.size)
        assertEquals("Follower Alert", items.first().name)
        assertEquals("first_party", items.first().trustTier)
        assertEquals(42, items.first().installCount)
        // … and the chosen framework filter is threaded straight through to the gallery read.
        assertEquals("vue", galleryApi.listedRequests.single().framework)
    }

    @Test
    fun install_from_gallery_calls_install_then_reloads_with_the_new_overlay() = runTest {
        val widgetsApi = RecordingWidgetsApi(ApiResult.Ok(emptyList()))
        val controller =
            widgetsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), widgetsApi)
        controller.load()
        assertTrue(controller.state.value is WidgetsState.Empty)

        controller.installFromGallery(galleryItemId = "g-1")

        // The install endpoint was hit with the gallery item id, scoped to the resolved channel …
        assertEquals(listOf("g-1"), widgetsApi.installed)
        assertEquals("ch1", widgetsApi.installedChannelId)
        // … and the post-install reload surfaces the freshly-installed overlay — the consequence, not just the call.
        val state: WidgetsState = controller.state.value
        assertTrue(state is WidgetsState.Ready)
        assertEquals(1, (state as WidgetsState.Ready).widgets.size)
        assertEquals("installed-g-1", state.widgets.first().id)
    }

    @Test
    fun clone_from_gallery_clones_with_the_gallery_item_id_then_opens_the_editor_on_the_copy() = runTest {
        val widgetsApi =
            RecordingWidgetsApi(
                ApiResult.Ok(emptyList()),
                projectResult =
                    ApiResult.Ok(
                        ProjectDto(
                            files = mapOf("index.html" to "<gallery-src/>"),
                            manifest =
                                ProjectManifestDto(entry = "index.html", kind = "widget", framework = "vanilla"),
                        )
                    ),
            )
        val editor = FakeProjectEditor(toSave = emptyList())
        val controller =
            widgetsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), widgetsApi, editor)
        controller.load()

        controller.cloneFromGallery(galleryItemId = "g-1", messages = messages)

        // Clone hit the real clone endpoint with the GALLERY item id (not an installed-widget id).
        assertEquals(listOf("g-1"), widgetsApi.clonedFromGalleryIds)
        assertTrue(widgetsApi.clonedIds.isEmpty())
        // …then the editor opened on the new copy, seeded with its cloned project source (ready to adapt).
        assertEquals("<gallery-src/>", editor.openedEntryContent)
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

// Builds a controller with a default (immediately-closing) project editor and an empty gallery so the tests that
// don't exercise those stay unchanged; the editor / gallery tests pass an explicit [FakeProjectEditor] /
// [FakeWidgetGalleryApi].
private fun widgetsController(
    channelsApi: ChannelsApi,
    widgetsApi: WidgetsApi,
    editor: ProjectEditorIO = FakeProjectEditor(),
    galleryApi: WidgetGalleryApi = FakeWidgetGalleryApi(),
): WidgetsController = WidgetsController(channelsApi, widgetsApi, galleryApi, editor, FakeSdkTypesApi())

// A fake SDK-types facade. The editor tests don't assert on the declarations (the fake project editor never
// opens a real language service), so it just returns an empty d.ts — the same graceful path a fetch failure
// takes in production.
private class FakeSdkTypesApi : SdkTypesApi {
    override suspend fun types(context: String): ApiResult<String> = ApiResult.Ok("")
}

// A fake multi-file project editor. Records the project it opened with (title + files + entry), "presses Save &
// Compile" for each entry-file edit in [toSave] (invoking the caller's compile callback with the full file map
// carrying that edit, and capturing the returned feedback), then closes. An empty [toSave] models the operator
// closing the editor without saving. [openedEntryContent] is the loaded content of the entry file — what the
// editor seeded with.
private class FakeProjectEditor(private val toSave: List<String> = emptyList()) : ProjectEditorIO {
    var openedTitle: String? = null
    var openedFiles: Map<String, String>? = null
    var openedEntry: String? = null
    val feedbacks: MutableList<CompileFeedback> = mutableListOf()

    val openedEntryContent: String?
        get() = openedFiles?.get(openedEntry)

    override suspend fun editAndCompile(
        title: String,
        initialFiles: Map<String, String>,
        entryPath: String,
        language: String,
        sdkTypes: String,
        compile: suspend (Map<String, String>) -> CompileFeedback,
    ) {
        openedTitle = title
        openedFiles = initialFiles
        openedEntry = entryPath
        for (edit in toSave) {
            // Model editing the entry file's content, then Save & Compile with the full updated file map.
            feedbacks += compile(initialFiles + (entryPath to edit))
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
// left untouched) to exercise the error path. [projectResult] is the project getProject returns for the editor to
// load; [putProjectResult] is the build outcome each project save reports; [versions]/[templates] back the version
// + template lists. A list-level failure is modelled by passing a Failure as the initial result.
private class RecordingWidgetsApi(
    initial: ApiResult<List<WidgetSummary>>,
    private val writeResult: ApiResult<Unit> = ApiResult.Ok(Unit),
    // The project getProject returns as the editable source (the `src/` file set + manifest the editor loads).
    private val projectResult: ApiResult<ProjectDto> = ApiResult.Ok(ProjectDto()),
    // The outcome each project save reports: Ok (server built + published a version) or Failure (broken build).
    private val putProjectResult: ApiResult<WidgetVersionDetail> =
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
    // Each project save: the widget id + the full file map that was PUT (so the round-trip is observable).
    val savedProjects: MutableList<Pair<String, Map<String, String>>> = mutableListOf()
    val loadedProjectIds: MutableList<String> = mutableListOf()
    val clonedIds: MutableList<String> = mutableListOf()
    val clonedFromGalleryIds: MutableList<String> = mutableListOf()
    val installed: MutableList<String> = mutableListOf()
    var installedChannelId: String? = null
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

    // The legacy single-source compile endpoint is no longer exercised by the controller (editing goes through
    // the project PUT), but the interface still declares it; records the source for completeness.
    override suspend fun compile(
        channelId: String,
        widgetId: String,
        sourceCode: String,
    ): ApiResult<WidgetVersionDetail> = putProjectResult

    // Returns the configured project the editor loads to edit, recording which widget's project was requested.
    override suspend fun getProject(channelId: String, widgetId: String): ApiResult<ProjectDto> {
        loadedProjectIds += widgetId
        return projectResult
    }

    // Records the full file map that was saved (the observable round-trip) and returns the configured build
    // outcome so a clean build (Ok) or a broken build (Failure) surfaces inline.
    override suspend fun putProject(
        channelId: String,
        widgetId: String,
        project: ProjectDto,
    ): ApiResult<WidgetVersionDetail> {
        savedProjects += widgetId to project.files
        return putProjectResult
    }

    override suspend fun listVersions(
        channelId: String,
        widgetId: String,
    ): ApiResult<List<WidgetVersionSummary>> = ApiResult.Ok(versions)

    override suspend fun getVersion(
        channelId: String,
        widgetId: String,
        versionId: String,
    ): ApiResult<WidgetVersionDetail> =
        ApiResult.Ok(WidgetVersionDetail(id = versionId, widgetId = widgetId))

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

    // Install adds the compiled gallery widget to the live store so the controller's post-install reload observes
    // the real new overlay (the consequence), and records the id + channel so the endpoint call is provable.
    override suspend fun install(channelId: String, galleryItemId: String): ApiResult<WidgetSummary> {
        installed += galleryItemId
        installedChannelId = channelId
        val widget =
            WidgetSummary(id = "installed-$galleryItemId", name = "Gallery Widget", source = "verified_gallery")
        store += widget
        return ApiResult.Ok(widget)
    }

    // The gallery clone returns a fresh custom widget carrying an active version, so the controller can open the
    // editor seeded with its source; the gallery item id is recorded to prove the fork source.
    override suspend fun cloneFromGallery(channelId: String, galleryItemId: String): ApiResult<WidgetSummary> {
        clonedFromGalleryIds += galleryItemId
        return ApiResult.Ok(
            WidgetSummary(id = "gallery-clone", name = "clone", source = "custom", activeVersionId = "v-1")
        )
    }
}

// A recording fake gallery catalogue: returns the preset [listResult] / [detail] and records every browse
// request so the controller's filter threading is provable without HTTP.
private class FakeWidgetGalleryApi(
    private val listResult: ApiResult<List<GalleryItemSummary>> = ApiResult.Ok(emptyList()),
    private val detail: ApiResult<GalleryItemDetail> = ApiResult.Ok(GalleryItemDetail()),
) : WidgetGalleryApi {
    val listedRequests: MutableList<GalleryListRequest> = mutableListOf()

    override suspend fun listGallery(
        framework: String?,
        trustTier: String?,
        reviewStatus: String?,
        page: Int,
        pageSize: Int,
    ): ApiResult<List<GalleryItemSummary>> {
        listedRequests += GalleryListRequest(framework, trustTier, page, pageSize)
        return listResult
    }

    override suspend fun getGalleryItem(galleryItemId: String): ApiResult<GalleryItemDetail> = detail

    override suspend fun submitGalleryItem(body: SubmitGalleryItemBody): ApiResult<GalleryItemDetail> = detail

    override suspend fun reviewGalleryItem(
        galleryItemId: String,
        body: ReviewGalleryItemBody,
    ): ApiResult<GalleryItemDetail> = detail

    override suspend fun pinGalleryItem(
        galleryItemId: String,
        body: PinGalleryItemBody,
    ): ApiResult<GalleryItemDetail> = detail
}
