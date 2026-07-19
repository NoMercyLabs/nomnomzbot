// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.codescripts.state

import bot.nomnomz.dashboard.core.editor.CompileFeedback
import bot.nomnomz.dashboard.core.editor.ProjectEditorIO
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.CodeScriptDetail
import bot.nomnomz.dashboard.core.network.CodeScriptSummary
import bot.nomnomz.dashboard.core.network.CodeScriptVersion
import bot.nomnomz.dashboard.core.network.CodeScriptsApi
import bot.nomnomz.dashboard.core.network.CreateScriptBody
import bot.nomnomz.dashboard.core.network.ProjectDto
import bot.nomnomz.dashboard.core.network.ScriptTestRunBody
import bot.nomnomz.dashboard.core.network.SdkTypesApi
import bot.nomnomz.dashboard.core.network.TestRunResult
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The Code Scripts page's state-holder. Lists all scripts, opens a project view for one (its `src/` file set +
// manifest), and drives create / enable-toggle / delete. Editing a script's code opens the shared multi-file
// project editor: each "Save & Compile" round-trips the whole project to putProject, which validates + compiles
// the entry and, on success, appends AND publishes a new version — so a save is a publish. Reloads the list on
// every successful write; opening a script re-fetches its detail + project.
class CodeScriptsController(
    private val api: CodeScriptsApi,
    private val projectEditor: ProjectEditorIO,
    private val sdkTypesApi: SdkTypesApi,
) {
    private val _state: MutableStateFlow<CodeScriptsState> = MutableStateFlow(CodeScriptsState.Loading)

    /** The page render state. */
    val state: StateFlow<CodeScriptsState> = _state.asStateFlow()

    /** Load (or reload) the full script list. */
    suspend fun load() {
        // Only show the full-page loading state on first load; a refetch after a mutation keeps
        // the current content on screen (no flash) and swaps it when the new data arrives.
        if (_state.value !is CodeScriptsState.Ready) _state.value = CodeScriptsState.Loading
        when (val result: ApiResult<List<CodeScriptSummary>> = api.list()) {
            is ApiResult.Failure -> _state.value = CodeScriptsState.Error(result.error.message)
            is ApiResult.Ok ->
                _state.value =
                    if (result.value.isEmpty()) CodeScriptsState.Empty
                    else CodeScriptsState.Ready(scripts = result.value)
        }
    }

    /**
     * Open a script for editing: fetch its detail AND its multi-file project (its `src/` file set + manifest),
     * then transition to [CodeScriptsState.Editing] with the entry file pre-selected. A failure surfaces over the
     * kept list without opening the editor.
     */
    suspend fun open(id: String) {
        val current: CodeScriptsState = _state.value
        val scripts: List<CodeScriptSummary> =
            if (current is CodeScriptsState.Ready) current.scripts else emptyList()

        val detail: CodeScriptDetail =
            when (val result: ApiResult<CodeScriptDetail> = api.get(id)) {
                is ApiResult.Ok -> result.value
                is ApiResult.Failure -> return failWrite(result.error.message)
            }
        val project: ProjectDto =
            when (val result: ApiResult<ProjectDto> = api.getProject(id)) {
                is ApiResult.Ok -> result.value
                is ApiResult.Failure -> return failWrite(result.error.message)
            }
        // The append-only version history (newest first) — best-effort: a fetch failure leaves the editor usable
        // with an empty history rather than blocking the open (the rollback list simply shows nothing to roll to).
        val versions: List<CodeScriptVersion> =
            when (val result: ApiResult<List<CodeScriptVersion>> = api.listVersions(id)) {
                is ApiResult.Ok -> result.value
                is ApiResult.Failure -> emptyList()
            }

        _state.value =
            CodeScriptsState.Editing(
                scripts = scripts,
                detail = detail,
                project = project,
                selectedPath = project.manifest.entry,
                versions = versions,
            )
    }

    /**
     * Roll the script back to a past [versionId] by re-publishing it as the active version (the backend keeps the
     * full append-only history — nothing is destroyed, the older version simply becomes current again). Re-opens
     * the script on success so the detail, project source, and version history all reflect the newly-active
     * version; surfaces the backend's reason over the kept editor on failure. Only applies while [id] is open.
     */
    suspend fun rollback(id: String, versionId: String) {
        val current: CodeScriptsState = _state.value
        if (current !is CodeScriptsState.Editing || current.detail.id != id) return
        when (val result: ApiResult<CodeScriptSummary> = api.publishVersion(id, versionId)) {
            is ApiResult.Ok -> {
                open(id)
                loadListSilent()
            }
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    /** Select a file in the open project's tree (drives the in-page preview pane). */
    fun selectFile(path: String) {
        val current: CodeScriptsState = _state.value
        if (current is CodeScriptsState.Editing) {
            _state.value = current.copy(selectedPath = path)
        }
    }

    /** Close the editor, returning to the list. */
    fun close() {
        val current: CodeScriptsState = _state.value
        if (current is CodeScriptsState.Editing) {
            _state.value =
                if (current.scripts.isEmpty()) CodeScriptsState.Empty
                else CodeScriptsState.Ready(scripts = current.scripts)
        }
    }

    /**
     * Open the shared multi-file project editor on the currently-open script, seeded with its project. Each
     * "Save & Compile" sends the whole project to [CodeScriptsApi.putProject] (validate + compile + publish),
     * surfacing the outcome inline: [compiledMessage] on success, the backend's real reason on failure. Reloads
     * the detail + list when the editor closes.
     */
    suspend fun editCode(id: String, compiledMessage: String) {
        val current: CodeScriptsState = _state.value
        if (current !is CodeScriptsState.Editing || current.detail.id != id) return
        val project: ProjectDto = current.project

        projectEditor.editAndCompile(
            title = current.detail.name,
            initialFiles = project.files,
            entryPath = project.manifest.entry,
            language = project.manifest.framework.ifBlank { "script" },
            // The script-context nnz.d.ts drives `nnz.` autocomplete + diagnostics in the web editor; a fetch
            // failure degrades to a plain editor rather than blocking editing.
            sdkTypes = fetchSdkTypes("script"),
            compile = { editedFiles -> saveProjectFeedback(id, editedFiles, project, compiledMessage) },
        )
        // Reload so the version number / validation status reflects the newly-published version, and refresh the
        // list underneath the editor.
        open(id)
        loadListSilent()
    }

    /**
     * Dry-run the open script's current version with sample [variables] + [args]. Effects are captured, never
     * performed (backend enforces this). Surfaces the captured result — chat output + effects — inline over the
     * editor, or the failure reason. Only applies while [id]'s editor is open.
     */
    suspend fun testRun(id: String, variables: Map<String, String>, args: List<String>) {
        val current: CodeScriptsState = _state.value
        if (current !is CodeScriptsState.Editing || current.detail.id != id) return
        _state.value = current.copy(testRunning = true, testError = null)

        when (
            val result: ApiResult<TestRunResult> = api.testRun(id, ScriptTestRunBody(variables, args))
        ) {
            is ApiResult.Ok -> updateEditing(id) { it.copy(testRunning = false, testResult = result.value, testError = null) }
            is ApiResult.Failure -> updateEditing(id) { it.copy(testRunning = false, testError = result.error.message) }
        }
    }

    // Apply [transform] to the open editor state only if it is still the same script (guards against the user
    // closing / switching scripts mid-run).
    private fun updateEditing(id: String, transform: (CodeScriptsState.Editing) -> CodeScriptsState.Editing) {
        val current: CodeScriptsState = _state.value
        if (current is CodeScriptsState.Editing && current.detail.id == id) {
            _state.value = transform(current)
        }
    }

    /** Toggle enabled/disabled. */
    suspend fun setEnabled(id: String, enabled: Boolean) {
        when (val result: ApiResult<CodeScriptSummary> = api.setEnabled(id, enabled)) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    /** Create a new script (a single-source project the backend scaffolds). Reloads the list on success. */
    suspend fun create(name: String, description: String?, sourceCode: String) {
        when (
            val result: ApiResult<CodeScriptSummary> =
                api.create(CreateScriptBody(name, description?.takeIf { it.isNotBlank() }, sourceCode))
        ) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    /** Delete a script. Reloads the list on success. */
    suspend fun delete(id: String) {
        when (val result: ApiResult<Unit> = api.delete(id)) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    // Fetch the generated nnz.d.ts for [context] to hand the editor's TypeScript language service; degrade to an
    // empty string (no autocomplete) on any failure rather than block the editor from opening.
    private suspend fun fetchSdkTypes(context: String): String =
        when (val result: ApiResult<String> = sdkTypesApi.types(context)) {
            is ApiResult.Ok -> result.value
            is ApiResult.Failure -> ""
        }

    // Save the edited project (files + the preserved manifest) and map the outcome to inline editor feedback. The
    // server returns a failure Result on a broken validation/compile (nothing persisted), so a failure surfaces
    // the real reason; a clean save surfaces the success message.
    private suspend fun saveProjectFeedback(
        id: String,
        files: Map<String, String>,
        project: ProjectDto,
        compiledMessage: String,
    ): CompileFeedback =
        when (
            val result: ApiResult<CodeScriptVersion> =
                api.putProject(id, ProjectDto(files = files, manifest = project.manifest))
        ) {
            is ApiResult.Ok -> CompileFeedback(ok = true, message = compiledMessage)
            is ApiResult.Failure -> CompileFeedback(ok = false, message = result.error.message)
        }

    private suspend fun loadListSilent() {
        when (val result: ApiResult<List<CodeScriptSummary>> = api.list()) {
            is ApiResult.Ok -> {
                val current: CodeScriptsState = _state.value
                if (current is CodeScriptsState.Editing) {
                    _state.value = current.copy(scripts = result.value)
                }
            }
            is ApiResult.Failure -> Unit // ignore silent reload failure; editor stays open
        }
    }

    private fun failWrite(detail: String) {
        val current: CodeScriptsState = _state.value
        _state.value =
            when (current) {
                is CodeScriptsState.Ready -> current.copy(actionError = detail)
                is CodeScriptsState.Editing -> current.copy(actionError = detail)
                else -> CodeScriptsState.Error(detail)
            }
    }
}

/** The Code Scripts page render state. */
sealed interface CodeScriptsState {
    data object Loading : CodeScriptsState

    data object Empty : CodeScriptsState

    data class Error(val detail: String) : CodeScriptsState

    data class Ready(
        val scripts: List<CodeScriptSummary>,
        val actionError: String? = null,
    ) : CodeScriptsState

    /**
     * The project view is open for [detail]. [project] is the script's `src/` file set + manifest; [selectedPath]
     * is the file shown in the in-page preview pane (the tree drives it). The actual editing happens in the shared
     * multi-file project editor launched from this view.
     */
    data class Editing(
        val scripts: List<CodeScriptSummary>,
        val detail: CodeScriptDetail,
        val project: ProjectDto,
        val selectedPath: String,
        /** The script's append-only version history (newest first) — backs the rollback list. */
        val versions: List<CodeScriptVersion> = emptyList(),
        val actionError: String? = null,
        val testRunning: Boolean = false,
        val testResult: TestRunResult? = null,
        val testError: String? = null,
    ) : CodeScriptsState
}
