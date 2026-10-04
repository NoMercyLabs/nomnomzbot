// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.editor

// One open editor page, seen from the host: answers each page message by calling the caller's
// [compile]/[EditorHistory]/[EditorTestRun] and posting the reply through [post]. Transport-free — the web
// host posts into an iframe, the desktop host evaluates into a native web view — so the whole save/history/
// test-run round trip is the same code on both targets.
class EditorBridgeSession(
    private val title: String,
    private val initialFiles: Map<String, String>,
    private val entryPath: String,
    private val language: String,
    private val sdkTypes: String,
    private val sdkTypesUnavailable: Boolean,
    private val previewWidget: EditorPreviewWidget?,
    private val history: EditorHistory?,
    private val testRun: EditorTestRun?,
    private val compile: suspend (Map<String, String>) -> CompileFeedback,
    private val post: (String) -> Unit,
    // The page's words in the dashboard's language; sent with every open, widgets included.
    private val uiLabels: suspend () -> Map<String, String> = { emptyMap() },
) {
    /** Handles one page message. Returns false once the page asked to close, true while the session goes on. */
    suspend fun handle(message: EditorInboundMessage): Boolean {
        when (message.type) {
            EditorBridgeProtocol.READY ->
                post(
                    EditorBridgeProtocol.open(
                        title, initialFiles, entryPath, language, sdkTypes, sdkTypesUnavailable, previewWidget, history,
                        testRun, uiLabels(),
                    )
                )
            EditorBridgeProtocol.SAVE -> post(EditorBridgeProtocol.compiled(compile(message.files)))
            EditorBridgeProtocol.HISTORY_LOAD_MORE -> postHistory(history?.loadMore?.invoke())
            EditorBridgeProtocol.HISTORY_ROLLBACK -> postHistory(history?.rollback?.invoke(message.versionId))
            EditorBridgeProtocol.HISTORY_DELETE -> postHistory(history?.delete?.invoke(message.versionId))
            EditorBridgeProtocol.TEST_RUN -> postTestRun(testRun?.run?.invoke(message.variables, message.args, message.trigger, message.role, message.files))
            EditorBridgeProtocol.CLOSE -> return false
        }
        return true
    }

    // A null outcome means the caller passed no [EditorHistory] — the page should never offer the action, so it
    // is answered as a visible failure rather than left as a hung "Loading…" row.
    private fun postHistory(outcome: EditorOutcome<EditorVersionsPage>?) {
        when (outcome) {
            is EditorOutcome.Ok -> post(EditorBridgeProtocol.historyPage(outcome.value))
            is EditorOutcome.Failed -> post(EditorBridgeProtocol.historyError(outcome.message))
            null -> post(EditorBridgeProtocol.historyError("History is not available for this project."))
        }
    }

    private fun postTestRun(outcome: EditorOutcome<EditorTestRunResult>?) {
        when (outcome) {
            is EditorOutcome.Ok -> post(EditorBridgeProtocol.testRunResult(outcome.value))
            is EditorOutcome.Failed -> post(EditorBridgeProtocol.testRunFailure(outcome.message))
            null -> post(EditorBridgeProtocol.testRunFailure("Test run is not available for this project."))
        }
    }
}
