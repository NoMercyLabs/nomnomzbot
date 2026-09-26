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

// Desktop project editor: the bot's own served editor page — the one the web build shows — hosted in the
// operating system's native web view (WebView2 / WKWebView / WebKitGTK; never a bundled Chromium). There is
// no substitute editor: when the web view cannot run, a design-system window says why and how to fix it.
actual class ProjectEditor : ProjectEditorIO {
    actual override suspend fun editAndCompile(
        title: String,
        initialFiles: Map<String, String>,
        entryPath: String,
        language: String,
        sdkTypes: String,
        eventSubscriptions: List<String>,
        history: EditorHistory?,
        testRun: EditorTestRun?,
        compile: suspend (Map<String, String>) -> CompileFeedback,
    ) {
        val unavailable: EditorUnavailableReason =
            openInNativeWebView(title, initialFiles, entryPath, language, sdkTypes, eventSubscriptions, history, testRun, compile)
                ?: return
        EditorUnavailableWindow.show(unavailable)
    }

    // Runs the editor to completion and returns null, or returns why it could not run.
    private suspend fun openInNativeWebView(
        title: String,
        initialFiles: Map<String, String>,
        entryPath: String,
        language: String,
        sdkTypes: String,
        eventSubscriptions: List<String>,
        history: EditorHistory?,
        testRun: EditorTestRun?,
        compile: suspend (Map<String, String>) -> CompileFeedback,
    ): EditorUnavailableReason? {
        val pageUrl: String =
            EditorWebViewBridge.editorPageUrl(ProjectEditorHost.botOrigin())
                ?: return EditorUnavailableReason.NoBotConnected
        when (val availability: NativeWebViewAvailability = NativeWebViewProbe.probe()) {
            NativeWebViewAvailability.Available -> Unit
            NativeWebViewAvailability.MissingWebView2Runtime -> return EditorUnavailableReason.MissingWebView2Runtime
            is NativeWebViewAvailability.Unavailable ->
                return EditorUnavailableReason.SystemWebViewFailed(availability.detail)
        }
        val outcome: WebViewEditorOutcome =
            WebViewProjectEditor.editAndCompile(pageUrl, title) { post: (String) -> Unit ->
                EditorBridgeSession(
                    title, initialFiles, entryPath, language, sdkTypes, eventSubscriptions, history, testRun, compile, post,
                )
            }
        return when (outcome) {
            WebViewEditorOutcome.Closed -> null
            is WebViewEditorOutcome.PageDidNotLoad -> EditorUnavailableReason.PageDidNotLoad(outcome.pageUrl)
        }
    }
}
