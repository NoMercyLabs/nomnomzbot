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

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.CodeScriptDetail
import bot.nomnomz.dashboard.core.network.CodeScriptSummary
import bot.nomnomz.dashboard.core.network.CodeScriptVersion
import bot.nomnomz.dashboard.core.network.CodeScriptsApi
import bot.nomnomz.dashboard.core.network.CreateScriptBody
import bot.nomnomz.dashboard.core.network.CreateVersionBody
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The Code Scripts page's state-holder. Lists all scripts, opens a detail/editor view for one, and drives
// create / new-version / publish-version / enable-toggle / delete. Reloads the list on every successful
// write; opens a script re-fetches its detail (with the current version's source code).
class CodeScriptsController(private val api: CodeScriptsApi) {
    private val _state: MutableStateFlow<CodeScriptsState> = MutableStateFlow(CodeScriptsState.Loading)

    /** The page render state. */
    val state: StateFlow<CodeScriptsState> = _state.asStateFlow()

    /** Load (or reload) the full script list. */
    suspend fun load() {
        _state.value = CodeScriptsState.Loading
        when (val result: ApiResult<List<CodeScriptSummary>> = api.list()) {
            is ApiResult.Failure -> _state.value = CodeScriptsState.Error(result.error.message)
            is ApiResult.Ok ->
                _state.value =
                    if (result.value.isEmpty()) CodeScriptsState.Empty
                    else CodeScriptsState.Ready(scripts = result.value)
        }
    }

    /** Open a script for editing: fetches its detail (source code) and transitions to [CodeScriptsState.Editing]. */
    suspend fun open(id: String) {
        val current: CodeScriptsState = _state.value
        val scripts: List<CodeScriptSummary> =
            if (current is CodeScriptsState.Ready) current.scripts
            else emptyList()

        when (val result: ApiResult<CodeScriptDetail> = api.get(id)) {
            is ApiResult.Failure -> {
                if (current is CodeScriptsState.Ready) {
                    _state.value = current.copy(actionError = result.error.message)
                }
            }
            is ApiResult.Ok ->
                _state.value =
                    CodeScriptsState.Editing(
                        scripts = scripts,
                        detail = result.value,
                        editorSource = result.value.currentVersion?.sourceCode ?: "",
                    )
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

    /** Update the local editor buffer (not persisted until [saveVersion] is called). */
    fun updateEditorSource(source: String) {
        val current: CodeScriptsState = _state.value
        if (current is CodeScriptsState.Editing) {
            _state.value = current.copy(editorSource = source)
        }
    }

    /** Append a new version; [publish] = save-and-swap immediately. Reloads the detail and list on success. */
    suspend fun saveVersion(id: String, source: String, publish: Boolean) {
        when (val result: ApiResult<CodeScriptVersion> = api.createVersion(id, CreateVersionBody(source, publish))) {
            is ApiResult.Ok -> { open(id); loadListSilent() }
            is ApiResult.Failure -> setEditingError(result.error.message)
        }
    }

    /** Publish a past version as the active one. */
    suspend fun publishVersion(scriptId: String, versionId: String) {
        when (val result: ApiResult<CodeScriptSummary> = api.publishVersion(scriptId, versionId)) {
            is ApiResult.Ok -> { open(scriptId); loadListSilent() }
            is ApiResult.Failure -> setEditingError(result.error.message)
        }
    }

    /** Toggle enabled/disabled. */
    suspend fun setEnabled(id: String, enabled: Boolean) {
        when (val result: ApiResult<CodeScriptSummary> = api.setEnabled(id, enabled)) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    /** Create a new script. Reloads the list on success. */
    suspend fun create(name: String, description: String?, sourceCode: String) {
        when (val result: ApiResult<CodeScriptSummary> = api.create(CreateScriptBody(name, description?.takeIf { it.isNotBlank() }, sourceCode))) {
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
            if (current is CodeScriptsState.Ready) current.copy(actionError = detail)
            else if (current is CodeScriptsState.Editing) current.copy(actionError = detail)
            else CodeScriptsState.Error(detail)
    }

    private fun setEditingError(detail: String) {
        val current: CodeScriptsState = _state.value
        if (current is CodeScriptsState.Editing) {
            _state.value = current.copy(actionError = detail)
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

    /** The editor is open for [detail]. [editorSource] tracks the live buffer (may differ from [detail.currentVersion.sourceCode]). */
    data class Editing(
        val scripts: List<CodeScriptSummary>,
        val detail: CodeScriptDetail,
        val editorSource: String,
        val actionError: String? = null,
    ) : CodeScriptsState
}
