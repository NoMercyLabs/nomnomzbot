// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.pipelines.state

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.TestRunResult
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/**
 * The minimal state-holder behind [bot.nomnomz.dashboard.feature.pipelines.ui.PipelineTestRunDialog] on any
 * screen that binds a single pipeline to something — commands, event responses, timers (S047-remaining). It
 * knows nothing about the caller's own domain state (a command, an event response, a timer): the caller supplies
 * [testRun], a closure over its own resolved channel id and its own `PipelinesApi`, that dry-runs one pipeline id
 * with sample variables. This mirrors [PipelinesController.testRun] exactly, just addressed by an explicit
 * pipeline id instead of "whichever pipeline the editor currently has open".
 */
class PipelineTestRunController(
    private val testRun: suspend (pipelineId: String, variables: Map<String, String>) -> ApiResult<TestRunResult>,
) {
    private val _state: MutableStateFlow<PipelineTestRunUiState> = MutableStateFlow(PipelineTestRunUiState())

    /** The dry-run's render state: whether it is running, the last captured result, or the last failure. */
    val state: StateFlow<PipelineTestRunUiState> = _state.asStateFlow()

    /** Dry-run [pipelineId] with sample [variables]; updates [state] with the captured result or the failure. */
    suspend fun run(pipelineId: String, variables: Map<String, String>) {
        _state.value = _state.value.copy(running = true, error = null)
        when (val result: ApiResult<TestRunResult> = testRun(pipelineId, variables)) {
            is ApiResult.Ok -> _state.value = PipelineTestRunUiState(running = false, result = result.value, error = null)
            is ApiResult.Failure -> _state.value = _state.value.copy(running = false, error = result.error.message)
        }
    }

    /** Clear the last result/error — called when the dialog is (re)opened for a different pipeline. */
    fun reset() {
        _state.value = PipelineTestRunUiState()
    }
}

/** [PipelineTestRunController]'s render state — see [PipelineTestRunController.state]. */
data class PipelineTestRunUiState(
    val running: Boolean = false,
    val result: TestRunResult? = null,
    val error: String? = null,
)
