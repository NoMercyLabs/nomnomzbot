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
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.CreateWidgetBody
import bot.nomnomz.dashboard.core.network.GalleryItemDetail
import bot.nomnomz.dashboard.core.network.GalleryItemSummary
import bot.nomnomz.dashboard.core.network.GalleryListRequest
import bot.nomnomz.dashboard.core.network.PinGalleryItemBody
import bot.nomnomz.dashboard.core.network.ProjectDto
import bot.nomnomz.dashboard.core.network.ReviewGalleryItemBody
import bot.nomnomz.dashboard.core.network.SubmitGalleryItemBody
import bot.nomnomz.dashboard.core.network.ProjectManifestDto
import bot.nomnomz.dashboard.core.network.WidgetGalleryApi
import bot.nomnomz.dashboard.core.network.WidgetSummary
import bot.nomnomz.dashboard.core.network.WidgetTemplate
import bot.nomnomz.dashboard.core.network.WidgetVersionDetail
import bot.nomnomz.dashboard.core.network.WidgetVersionSummary
import bot.nomnomz.dashboard.core.network.WidgetsApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The Overlays page's state-holder (frontend-ia.md §3 — the Stream group; a plain holder, not a ViewModel).
// Resolves the active channel, then lists its real OBS overlay widgets from the backend (no fabricated rows) —
// each row carries the browser-source URL the operator copies into OBS. It also drives the page's writes —
// enable/disable, rename, delete, clone, create, and the compile-on-save code editor + version rollback. Every
// mutation re-lists on success so the screen always reflects the backend's truth. Delete is destructive (the
// overlay's URL stops resolving once it is gone), so the screen confirms it before calling [deleteWidget]. The
// screen renders [state]; a retry / reconnect calls [load] again.
class WidgetsController(
    private val channelsApi: ChannelsApi,
    private val widgetsApi: WidgetsApi,
    private val widgetGalleryApi: WidgetGalleryApi,
    private val projectEditor: ProjectEditorIO,
) {
    private val _state: MutableStateFlow<WidgetsState> = MutableStateFlow(WidgetsState.Loading)

    /** The page render state: loading / ready (with the overlays) / empty / error. */
    val state: StateFlow<WidgetsState> = _state.asStateFlow()

    // The channel the writes target — resolved by [load] and reused by every mutation so a write never has to
    // re-resolve the channel. Null until the first successful resolve.
    private var channelId: String? = null

    /** Resolve the active channel, then list its overlay widgets. */
    suspend fun load() {
        // Only show the full-page loading state on first load; a refetch after a mutation keeps
        // the current content on screen (no flash) and swaps it when the new data arrives.
        if (_state.value !is WidgetsState.Ready) _state.value = WidgetsState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = WidgetsState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }
        channelId = channel.id

        when (val result: ApiResult<List<WidgetSummary>> = widgetsApi.list(channel.id)) {
            is ApiResult.Failure -> _state.value = WidgetsState.Error(result.error.message)
            is ApiResult.Ok ->
                _state.value =
                    if (result.value.isEmpty()) WidgetsState.Empty
                    else WidgetsState.Ready(result.value)
        }
    }

    /** Flip a widget's enabled flag (partial PUT carrying only the flag), then reload on success. */
    suspend fun toggleWidget(widgetId: String, enabled: Boolean) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(widgetsApi.setEnabled(channel, widgetId, enabled))
    }

    /**
     * Delete a widget, addressed by its [widgetId]. Reloads on success. Surfaces the error on failure.
     * Destructive — the screen routes this through the confirm dialog before calling it (deleting the
     * overlay invalidates its browser-source URL).
     */
    suspend fun deleteWidget(widgetId: String) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(widgetsApi.delete(channel, widgetId))
    }

    /**
     * Create a new widget ({ [name], [framework] }), then open the multi-file project editor seeded with a one-file
     * project ({ entry → [seedSource] }, a chosen template's source or blank) so the operator authors + compiles
     * the first version right away. Reloads when the editor closes; surfaces the error if the create call fails.
     */
    suspend fun createWidget(
        name: String,
        framework: String,
        seedSource: String,
        messages: WidgetEditorMessages,
    ) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        when (val result: ApiResult<WidgetSummary> = widgetsApi.create(channel, CreateWidgetBody(name, framework))) {
            is ApiResult.Ok -> {
                val seeded: ProjectDto = seedProject(framework, seedSource)
                openEditor(channel, result.value.id, name, framework, seeded, messages)
            }
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    /** Rename a widget ([widgetId]) to [newName] via a partial PUT. Reloads on success. */
    suspend fun renameWidget(widgetId: String, newName: String) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(widgetsApi.rename(channel, widgetId, newName))
    }

    /**
     * Open the multi-file project editor on a widget, loaded with its current `src/` project (its file set +
     * manifest), then reload when it closes. Each "Save & Compile" sends the whole project to
     * [WidgetsApi.putProject], which re-builds it server-side and reports the outcome inline. A widget with no
     * saved project yet (freshly created, never compiled) opens on a seeded one-file project.
     */
    suspend fun editWidgetCode(widget: WidgetSummary, messages: WidgetEditorMessages) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        // A brand-new widget has no version yet, so getProject fails — fall back to a seeded one-file project
        // rather than blocking the operator from authoring their first version.
        val project: ProjectDto =
            when (val loaded: ApiResult<ProjectDto> = widgetsApi.getProject(channel, widget.id)) {
                is ApiResult.Ok -> loaded.value
                is ApiResult.Failure -> seedProject(widget.framework, "")
            }
        openEditor(channel, widget.id, widget.name, widget.framework, project, messages)
    }

    /** Roll the overlay back to a past [versionId] (it becomes the served version again). Reloads on success. */
    suspend fun rollbackVersion(widgetId: String, versionId: String) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        when (val result: ApiResult<WidgetSummary> = widgetsApi.rollback(channel, widgetId, versionId)) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    /**
     * The widget's version history (newest first) for the rollback dialog. Returns the raw result so the dialog
     * can render its own loading / error / list — a read that does not disturb the page's [state].
     */
    suspend fun listVersions(widgetId: String): ApiResult<List<WidgetVersionSummary>> {
        val channel: String = channelId ?: return ApiResult.Failure(NoChannelApiError)
        return widgetsApi.listVersions(channel, widgetId)
    }

    /** The starter templates the create dialog offers. Returns the raw result for the dialog to render. */
    suspend fun listTemplates(): ApiResult<List<WidgetTemplate>> {
        val channel: String = channelId ?: return ApiResult.Failure(NoChannelApiError)
        return widgetsApi.listTemplates(channel)
    }

    /** Clone an installed widget into a fresh, independently-editable custom copy. Reloads on success. */
    suspend fun cloneWidget(widgetId: String) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        when (val result: ApiResult<WidgetSummary> = widgetsApi.clone(channel, widgetId)) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    /**
     * Browse the public widget gallery for the browse surface. Returns the raw result so the dialog renders its
     * own loading / error / list — a read that does not disturb the page's [state], and (like the catalogue
     * itself) needs no channel resolve.
     */
    suspend fun listGallery(request: GalleryListRequest): ApiResult<List<GalleryItemSummary>> =
        widgetGalleryApi.listGallery(
            framework = request.framework,
            trustTier = request.trustTier,
            page = request.page,
            pageSize = request.pageSize,
        )

    /**
     * Submit a community widget to the gallery for review (any signed-in user). Returns the raw result so the
     * submit dialog surfaces the backend validation errors (bad SHA / URL) inline without disturbing the page.
     */
    suspend fun submitToGallery(body: SubmitGalleryItemBody): ApiResult<GalleryItemDetail> =
        widgetGalleryApi.submitGalleryItem(body)

    /**
     * The reviewer queue read (`gallery:review`): the submissions awaiting a verdict. The status filter only works
     * for a reviewer server-side; a non-reviewer gets their own items. Returns raw so the review panel renders its
     * own loading/error/list without touching the overlays [state].
     */
    suspend fun listReviewQueue(reviewStatus: String): ApiResult<List<GalleryItemSummary>> =
        widgetGalleryApi.listGallery(reviewStatus = reviewStatus, pageSize = 100)

    /** Load one gallery item in full (its review metadata + source) for the review detail panel. */
    suspend fun galleryItemDetail(galleryItemId: String): ApiResult<GalleryItemDetail> =
        widgetGalleryApi.getGalleryItem(galleryItemId)

    /** Reviewer verdict on a submission. Returns the raw result so the panel refreshes the queue on success. */
    suspend fun reviewGalleryItem(
        galleryItemId: String,
        body: ReviewGalleryItemBody,
    ): ApiResult<GalleryItemDetail> = widgetGalleryApi.reviewGalleryItem(galleryItemId, body)

    /** Reviewer re-pin — moves the item to a new commit and back to `in_review` (off the public list). */
    suspend fun pinGalleryItem(
        galleryItemId: String,
        body: PinGalleryItemBody,
    ): ApiResult<GalleryItemDetail> = widgetGalleryApi.pinGalleryItem(galleryItemId, body)

    /**
     * Install a gallery item into the active channel (compiled + live), then reload so the new overlay appears in
     * the list. Surfaces the error on failure.
     */
    suspend fun installFromGallery(galleryItemId: String) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        when (val result: ApiResult<WidgetSummary> = widgetsApi.install(channel, galleryItemId)) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    /**
     * Clone a gallery item into a fresh, independently-editable custom widget, then open the compile-on-save
     * editor on the new copy (seeded with its source) so the operator can adapt it right away. The editor close
     * reloads the list (via [editWidgetCode]); surfaces the error if the clone call itself fails.
     */
    suspend fun cloneFromGallery(galleryItemId: String, messages: WidgetEditorMessages) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        when (val result: ApiResult<WidgetSummary> = widgetsApi.cloneFromGallery(channel, galleryItemId)) {
            is ApiResult.Ok -> editWidgetCode(result.value, messages)
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    // Open the multi-file project editor, wiring each save to a project PUT (server re-build → new active
    // version), then reload the list when the operator closes it (so the row reflects the new active version /
    // freshly created widget). The manifest loaded with the project is preserved on save so declared dependencies
    // and the pinned entry survive round-trips.
    private suspend fun openEditor(
        channel: String,
        widgetId: String,
        title: String,
        framework: String,
        project: ProjectDto,
        messages: WidgetEditorMessages,
    ) {
        projectEditor.editAndCompile(
            title = title,
            initialFiles = project.files,
            entryPath = project.manifest.entry,
            // Highlighting is best-effort; the framework doubles as the editor's language badge.
            language = framework.ifBlank { "html" },
            compile = { editedFiles -> saveProjectFeedback(channel, widgetId, editedFiles, project.manifest, messages) },
        )
        load()
    }

    // Save the edited project (files + the preserved manifest) via putProject and map the build outcome to inline
    // feedback. The server returns a failure Result on a broken build (nothing persisted), so a transport or build
    // failure surfaces as ok=false carrying the real backend reason; a clean build surfaces the success message.
    private suspend fun saveProjectFeedback(
        channel: String,
        widgetId: String,
        files: Map<String, String>,
        manifest: ProjectManifestDto,
        messages: WidgetEditorMessages,
    ): CompileFeedback =
        when (val result: ApiResult<WidgetVersionDetail> =
            widgetsApi.putProject(channel, widgetId, ProjectDto(files = files, manifest = manifest))) {
            is ApiResult.Ok -> CompileFeedback(ok = true, message = messages.compiled)
            is ApiResult.Failure -> CompileFeedback(ok = false, message = result.error.message)
        }

    // A one-file seed project for a widget with no saved project yet — mirrors the backend's single-file scaffold
    // (entry filename by framework, kind = "widget"). Vue projects declare the one allowed dependency so the seed
    // builds; other frameworks declare none.
    private fun seedProject(framework: String, source: String): ProjectDto {
        val normalized: String = framework.trim().lowercase()
        val entry: String = entryFileName(normalized)
        val dependencies: List<String>? = if (normalized == "vue") listOf("vue") else null
        return ProjectDto(
            files = mapOf(entry to source),
            manifest =
                ProjectManifestDto(
                    entry = entry,
                    kind = "widget",
                    framework = normalized,
                    dependencies = dependencies,
                ),
        )
    }

    // The conventional single-file entry filename for a widget framework — kept in lock-step with the backend's
    // ProjectScaffold.EntryFileName so a client seed and a server scaffold agree on the entry path.
    private fun entryFileName(framework: String): String =
        when (framework.trim().lowercase()) {
            "vue" -> "index.vue"
            "react" -> "index.tsx"
            "vanilla" -> "index.html"
            else -> "index.js"
        }

    // A write either reloads the list (success) or surfaces its error over the current Ready list without
    // losing it (failure) — so a failed toggle/delete leaves the page intact with a visible reason.
    private suspend fun afterWrite(result: ApiResult<Unit>) {
        when (result) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    private fun failWrite(detail: String) {
        val current: WidgetsState = _state.value
        _state.value =
            if (current is WidgetsState.Ready) current.copy(actionError = detail)
            else WidgetsState.Error(detail)
    }

    private companion object {
        const val NoChannelError: String = "No active channel — reconnect and try again."
        val NoChannelApiError: ApiError = ApiError(status = 0, code = "NO_CHANNEL", message = NoChannelError)
    }
}

/**
 * The localized editor-feedback string the save callback surfaces on a clean build. Resolved by the screen (a
 * Composable) and passed in, because the controller is a plain state holder with no access to Compose string
 * resources. A failed build surfaces the backend's real error message directly, so only the success string is
 * localized here.
 */
data class WidgetEditorMessages(val compiled: String)

/** The Overlays page render state. */
sealed interface WidgetsState {
    data object Loading : WidgetsState

    /**
     * The channel's overlay widgets are listed. [actionError] is non-null only when the last toggle/delete
     * failed — the screen surfaces it as a transient banner while keeping the list rendered.
     */
    data class Ready(val widgets: List<WidgetSummary>, val actionError: String? = null) :
        WidgetsState

    data object Empty : WidgetsState

    data class Error(val detail: String) : WidgetsState
}
