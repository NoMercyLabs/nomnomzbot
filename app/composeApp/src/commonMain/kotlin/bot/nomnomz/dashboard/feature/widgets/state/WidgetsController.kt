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
import bot.nomnomz.dashboard.core.network.WidgetSummary
import bot.nomnomz.dashboard.core.network.WidgetTemplate
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
    private val codeEditor: CustomCodeEditorIO,
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
     * Create a new widget ({ [name], [framework] }), then open the compile-on-save editor seeded with
     * [seedSource] (a chosen template's source, or blank) so the operator authors + compiles the first version
     * right away. Reloads when the editor closes; surfaces the error if the create call fails.
     */
    suspend fun createWidget(
        name: String,
        framework: String,
        seedSource: String,
        messages: WidgetEditorMessages,
    ) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        when (val result: ApiResult<WidgetSummary> = widgetsApi.create(channel, CreateWidgetBody(name, framework))) {
            is ApiResult.Ok -> openEditor(channel, result.value.id, name, framework, seedSource, messages)
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    /** Rename a widget ([widgetId]) to [newName] via a partial PUT. Reloads on success. */
    suspend fun renameWidget(widgetId: String, newName: String) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(widgetsApi.rename(channel, widgetId, newName))
    }

    /**
     * Open the compile-on-save code editor on a widget, seeded with its active version's source, then reload
     * when it closes. Each "Save & Compile" builds a new version via [WidgetsApi.compile] and reports the build
     * result inline. If the source cannot be loaded, surfaces the error and does not open the editor.
     */
    suspend fun editWidgetCode(widget: WidgetSummary, messages: WidgetEditorMessages) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        val source: String =
            when (val loaded: ApiResult<String> = loadActiveSource(channel, widget)) {
                is ApiResult.Ok -> loaded.value
                is ApiResult.Failure -> return failWrite(loaded.error.message)
            }
        openEditor(channel, widget.id, widget.name, widget.framework, source, messages)
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

    // Open the compile-on-save editor, wiring each save to a compile of the widget's next version, then reload
    // the list when the operator closes it (so the row reflects the new active version / freshly created widget).
    private suspend fun openEditor(
        channel: String,
        widgetId: String,
        title: String,
        framework: String,
        initialSource: String,
        messages: WidgetEditorMessages,
    ) {
        codeEditor.editAndCompile(
            title = title,
            initialCode = initialSource,
            // Highlighting is best-effort; the framework doubles as the editor's language badge.
            language = framework.ifBlank { "html" },
            compile = { edited -> compileToFeedback(channel, widgetId, edited, messages) },
        )
        load()
    }

    // Compile the authored source into the widget's next version and map the build outcome to inline feedback.
    // A transport failure is surfaced as a failed build with the backend's error message.
    private suspend fun compileToFeedback(
        channel: String,
        widgetId: String,
        source: String,
        messages: WidgetEditorMessages,
    ): CompileFeedback =
        when (val result = widgetsApi.compile(channel, widgetId, source)) {
            is ApiResult.Ok -> {
                val ok: Boolean = result.value.buildStatus.equals("success", ignoreCase = true)
                CompileFeedback(
                    ok = ok,
                    message =
                        if (ok) messages.compiled
                        else result.value.buildError ?: result.value.buildLog ?: messages.buildFailed,
                )
            }
            is ApiResult.Failure -> CompileFeedback(ok = false, message = result.error.message)
        }

    // Load the source the editor seeds with: the active version if the widget has one, else the newest version,
    // else blank (a freshly created widget with no compiled version yet). A hard load failure propagates.
    private suspend fun loadActiveSource(channel: String, widget: WidgetSummary): ApiResult<String> {
        val versionId: String? =
            widget.activeVersionId
                ?: when (val versions: ApiResult<List<WidgetVersionSummary>> = widgetsApi.listVersions(channel, widget.id)) {
                    is ApiResult.Ok -> versions.value.firstOrNull()?.id
                    is ApiResult.Failure -> return ApiResult.Failure(versions.error)
                }
        if (versionId == null) return ApiResult.Ok("")
        return when (val detail = widgetsApi.getVersion(channel, widget.id, versionId)) {
            is ApiResult.Ok -> ApiResult.Ok(detail.value.sourceCode ?: "")
            is ApiResult.Failure -> ApiResult.Failure(detail.error)
        }
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
 * The localized editor-feedback strings the compile callback surfaces. Resolved by the screen (a Composable) and
 * passed in, because the controller is a plain state holder with no access to Compose string resources.
 * [buildError] / [buildLog] from the backend take precedence over [buildFailed] when a build reports an error.
 */
data class WidgetEditorMessages(val compiled: String, val buildFailed: String)

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
