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
import bot.nomnomz.dashboard.core.editor.EditorHistory
import bot.nomnomz.dashboard.core.editor.EditorOutcome
import bot.nomnomz.dashboard.core.editor.EditorTestRun
import bot.nomnomz.dashboard.core.editor.EditorTestRunEffect
import bot.nomnomz.dashboard.core.editor.EditorTestRunResult
import bot.nomnomz.dashboard.core.editor.EditorVersionSummary
import bot.nomnomz.dashboard.core.editor.EditorVersionsPage
import bot.nomnomz.dashboard.core.feedback.Feedback
import bot.nomnomz.dashboard.core.feedback.NoOpFeedback
import bot.nomnomz.dashboard.core.editor.ProjectEditorIO
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.CodeScriptDetail
import bot.nomnomz.dashboard.core.network.CodeScriptSummary
import bot.nomnomz.dashboard.core.network.CodeScriptVersion
import bot.nomnomz.dashboard.core.network.CodeScriptsApi
import bot.nomnomz.dashboard.core.network.CreateScriptBody
import bot.nomnomz.dashboard.core.network.PaginatedEnvelope
import bot.nomnomz.dashboard.core.network.ProjectDto
import bot.nomnomz.dashboard.core.network.ScriptTestRunBody
import bot.nomnomz.dashboard.core.network.SdkTypesApi
import bot.nomnomz.dashboard.core.network.TestRunResult
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import bot.nomnomz.dashboard.core.network.BlastRadiusSummary
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.scripts_action_error

// The Code Scripts page's state-holder. Lists all scripts, opens a project view for one (its `src/` file set +
// manifest), and drives create / enable-toggle / delete. Editing a script's code opens the shared multi-file
// project editor: each "Save & Compile" round-trips the whole project to putProject, which validates + compiles
// the entry and, on success, appends AND publishes a new version — so a save is a publish. Reloads the list on
// every successful write; opening a script re-fetches its detail + project.
class CodeScriptsController(
    private val api: CodeScriptsApi,
    private val projectEditor: ProjectEditorIO,
    private val sdkTypesApi: SdkTypesApi,
    private val feedback: Feedback = NoOpFeedback,
) {
    private val _state: MutableStateFlow<CodeScriptsState> = MutableStateFlow(CodeScriptsState.Loading)

    /** The page render state. */
    val state: StateFlow<CodeScriptsState> = _state.asStateFlow()

    // Set by [requestOpen] when another page (e.g. the pipeline builder's code-tier step field) deep-links here
    // with a specific script id to open — consumed (and cleared) by the very next [load], so it fires exactly
    // once, the next time this page is shown, and never re-opens on a later reload.
    private var pendingOpenId: String? = null

    /**
     * Ask this controller to open [scriptId]'s editor the next time it is [load]ed — the deep-link seam used when
     * the pipeline builder's code-tier step field navigates here for a specific script (mirrors the pattern of a
     * shell-level "pending selection" carried across a screen navigation). Safe to call before this controller's
     * screen is even composed.
     */
    fun requestOpen(scriptId: String) {
        pendingOpenId = scriptId
    }

    /** Load (or reload) the full script list — or, if [requestOpen] left a pending script id, open it instead. */
    suspend fun load() {
        val pending: String? = pendingOpenId
        if (pending != null) {
            pendingOpenId = null
            open(pending)
            return
        }
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
        // The append-only version history (newest first), first page only — best-effort: a fetch failure leaves
        // the editor usable with an empty history rather than blocking the open (the rollback list simply shows
        // nothing to roll to). Further pages load on demand via [loadMoreVersions] ("load more"), never dumped
        // all at once (S-OWN06).
        val versionsPage: PaginatedEnvelope<CodeScriptVersion>? =
            when (val result: ApiResult<PaginatedEnvelope<CodeScriptVersion>> = api.listVersions(id, page = 1)) {
                is ApiResult.Ok -> result.value
                is ApiResult.Failure -> null
            }

        _state.value =
            CodeScriptsState.Editing(
                scripts = scripts,
                detail = detail,
                project = project,
                versions = versionsPage?.data ?: emptyList(),
                versionsPage = 1,
                versionsHasMore = versionsPage?.hasMore ?: false,
            )
    }

    /**
     * Load the next page of [id]'s version history and append it to what's already shown ("load more" — the
     * history is never dumped in one unbounded response, S-OWN06). No-op if the editor for [id] isn't open, a
     * page is already loading, or there is no further page.
     */
    suspend fun loadMoreVersions(id: String) {
        val current: CodeScriptsState = _state.value
        if (current !is CodeScriptsState.Editing || current.detail.id != id) return
        if (current.versionsLoadingMore || !current.versionsHasMore) return

        val nextPage: Int = current.versionsPage + 1
        _state.value = current.copy(versionsLoadingMore = true)
        when (val result: ApiResult<PaginatedEnvelope<CodeScriptVersion>> = api.listVersions(id, page = nextPage)) {
            is ApiResult.Ok ->
                updateEditing(id) {
                    it.copy(
                        versions = it.versions + result.value.data,
                        versionsPage = nextPage,
                        versionsHasMore = result.value.hasMore,
                        versionsLoadingMore = false,
                    )
                }
            is ApiResult.Failure -> {
                updateEditing(id) { it.copy(versionsLoadingMore = false) }
                failWrite(result.error.message)
            }
        }
    }

    /**
     * Delete one saved version from [id]'s history (S-OWN06). The row is removed from the shown list on success;
     * the backend refuses (and this surfaces the reason) if it's the currently published version. Only applies
     * while [id]'s editor is open.
     */
    suspend fun deleteVersion(id: String, versionId: String) {
        val current: CodeScriptsState = _state.value
        if (current !is CodeScriptsState.Editing || current.detail.id != id) return
        when (val result: ApiResult<Unit> = api.deleteVersion(id, versionId)) {
            is ApiResult.Ok ->
                updateEditing(id) { it.copy(versions = it.versions.filterNot { v -> v.id == versionId }) }
            is ApiResult.Failure -> failWrite(result.error.message)
        }
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
     * Open the shared multi-file project editor DIRECTLY on script [id] — the collapsed replacement for the old
     * list -> read-only detail page -> "Edit code" hop (S-CODE-COLLAPSE): the operator lands in the real Monaco
     * editor the moment they open a script. Fetches the detail + project + first version-history page (same as
     * the old [open]), then launches the editor seeded with it, wiring its in-editor History and Test run side
     * views to this controller's own version/rollback/delete/test-run logic so both stay reachable WITHOUT a
     * separate page. Each "Save & Compile" sends the whole project to [CodeScriptsApi.putProject]
     * (validate + compile + publish), surfacing the outcome inline: [compiledMessage] on success, the backend's
     * real reason on failure. Reloads the list when the editor closes, returning to it (never back into a
     * stale "Editing" page). A failure surfaces over the kept list without opening the editor.
     *
     * [displayName] is the caller's already-resolved, never-blank editor title (see
     * [bot.nomnomz.dashboard.core.designsystem.resolveRowLabel]) — this controller has no Composable context to
     * resolve a localized fallback itself.
     */
    suspend fun openAndEdit(id: String, compiledMessage: String, displayName: String) {
        open(id)
        val current: CodeScriptsState = _state.value
        if (current !is CodeScriptsState.Editing || current.detail.id != id) return
        val project: ProjectDto = current.project

        projectEditor.editAndCompile(
            title = displayName,
            initialFiles = project.files,
            entryPath = project.manifest.entry,
            // Always "script": a code script runs in the bot sandbox and has no DOM to render, so the editor
            // must pick its no-preview layout. The manifest's framework holds a LANGUAGE here (typescript),
            // not a UI framework, and passing it through made the editor reserve a live-preview pane that can
            // never show anything. Per-file language is resolved from the file extension by the editor itself.
            language = "script",
            // The script-context nnz.d.ts drives `nnz.` autocomplete + diagnostics in the web editor; a fetch
            // failure degrades to a plain editor rather than blocking editing.
            sdkTypes = fetchSdkTypes("script"),
            history = buildEditorHistory(id, current),
            testRun = buildEditorTestRun(id),
            compile = { editedFiles -> saveProjectFeedback(id, editedFiles, project, compiledMessage) },
        )
        // The editor closed — back to the list, refreshed so the row reflects the newly-published version.
        load()
    }

    // Seeds the editor's History side view from the page already loaded by [open], and wires its load-more /
    // rollback / delete actions back onto this controller's own methods (single-sourced — no separate API
    // calling logic for the in-editor panel). A write failure already announces on the shell-level feedback
    // toast (see [failWrite]), matching the rest of this controller, so a "refresh from current state" is
    // always the right outcome here, success or not.
    private fun buildEditorHistory(id: String, editing: CodeScriptsState.Editing): EditorHistory =
        EditorHistory(
            initialVersions = editing.versions.toEditorSummaries(editing.detail.currentVersionId),
            initialHasMore = editing.versionsHasMore,
            loadMore = {
                loadMoreVersions(id)
                editorVersionsOutcome(id)
            },
            rollback = { versionId ->
                rollback(id, versionId)
                editorVersionsOutcome(id)
            },
            delete = { versionId ->
                deleteVersion(id, versionId)
                editorVersionsOutcome(id)
            },
        )

    private fun editorVersionsOutcome(id: String): EditorOutcome<EditorVersionsPage> {
        val current: CodeScriptsState = _state.value
        if (current !is CodeScriptsState.Editing || current.detail.id != id) {
            return EditorOutcome.Failed("The editor session ended.")
        }
        return EditorOutcome.Ok(
            EditorVersionsPage(
                versions = current.versions.toEditorSummaries(current.detail.currentVersionId),
                hasMore = current.versionsHasMore,
            ),
        )
    }

    // Wires the editor's Test run panel onto this controller's own [testRun] — the same dry-run logic already
    // exercised directly (see CodeScriptsControllerTestRunTest), read back off state instead of duplicated here.
    private fun buildEditorTestRun(id: String): EditorTestRun =
        EditorTestRun { variables, args ->
            testRun(id, variables, args)
            val current: CodeScriptsState = _state.value
            val error: String? = (current as? CodeScriptsState.Editing)?.testError
            val result: TestRunResult? = (current as? CodeScriptsState.Editing)?.testResult
            when {
                current !is CodeScriptsState.Editing || current.detail.id != id ->
                    EditorOutcome.Failed("The editor session ended.")
                error != null -> EditorOutcome.Failed(error)
                result != null ->
                    EditorOutcome.Ok(
                        EditorTestRunResult(
                            success = result.success,
                            durationMs = result.durationMs,
                            hostCallCount = result.hostCallCount,
                            error = result.error,
                            chatOutput = result.chatOutput,
                            effects = result.capturedEffects.map { effect -> EditorTestRunEffect(effect.name, effect.argsPreview) },
                        ),
                    )
                else -> EditorOutcome.Failed("No result.")
            }
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
            val result: ApiResult<CodeScriptDetail> =
                api.create(CreateScriptBody(name, description?.takeIf { it.isNotBlank() }, sourceCode))
        ) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    /** Delete a script. Reloads the list on success. */

    /**
     * The real, backend-counted blast radius of deleting the code script [id] (S-CONSEQ) - the delete confirm calls this and
     * renders the dependents BEFORE the destructive save can proceed. A lookup that fails is a genuine
     * FAILURE, never a silent zero: the dialog then shows its own "could not check" message rather than an
     * empty radius that reads as verified-safe.
     */
    suspend fun fetchBlastRadius(id: String): ApiResult<BlastRadiusSummary> =
        api.blastRadius(id)

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

    // The page is already showing content (Ready, Empty — the create dialog still works — or Editing) —
    // announce on the shell-level feedback toast rather than a local banner. Only when the page has nothing to
    // show yet does a failure become the page's own Error state.
    private fun failWrite(detail: String) {
        val current: CodeScriptsState = _state.value
        when (current) {
            is CodeScriptsState.Ready, is CodeScriptsState.Empty, is CodeScriptsState.Editing ->
                feedback.error(Res.string.scripts_action_error, detail)
            else -> _state.value = CodeScriptsState.Error(detail)
        }
    }
}

// Maps the network version list onto the platform-agnostic [EditorVersionSummary] the in-editor History view
// renders — "current" is [currentVersionId], not a field on the version row itself.
private fun List<CodeScriptVersion>.toEditorSummaries(currentVersionId: String?): List<EditorVersionSummary> =
    map { version ->
        EditorVersionSummary(
            id = version.id,
            version = version.version,
            validationStatus = version.validationStatus,
            isCurrent = currentVersionId != null && version.id == currentVersionId,
        )
    }

/** The Code Scripts page render state. */
sealed interface CodeScriptsState {
    data object Loading : CodeScriptsState

    data object Empty : CodeScriptsState

    data class Error(val detail: String) : CodeScriptsState

    data class Ready(
        val scripts: List<CodeScriptSummary>,
    ) : CodeScriptsState

    /**
     * The shared multi-file project editor is open (or opening) for [detail] — [openAndEdit]'s working state
     * while [project] and the version history below feed the editor's History / Test run side views
     * (S-CODE-COLLAPSE). There is no separate Compose rendering for this state: the editor itself, a full-screen
     * overlay (web) / its own window (desktop), IS what the operator sees; [CodeScriptsScreen] renders only a
     * brief "opening…" placeholder for the moment between [open] and the editor actually mounting.
     */
    data class Editing(
        val scripts: List<CodeScriptSummary>,
        val detail: CodeScriptDetail,
        val project: ProjectDto,
        /** The script's version history loaded so far (newest first) — backs the rollback list. */
        val versions: List<CodeScriptVersion> = emptyList(),
        /** The last version-history page fetched (1-based) — [loadMoreVersions] fetches [versionsPage] + 1. */
        val versionsPage: Int = 1,
        /** Whether the backend reports a further, not-yet-loaded page of version history. */
        val versionsHasMore: Boolean = false,
        /** True while a "load more" fetch for the version history is in flight. */
        val versionsLoadingMore: Boolean = false,
        val testRunning: Boolean = false,
        val testResult: TestRunResult? = null,
        val testError: String? = null,
    ) : CodeScriptsState
}
