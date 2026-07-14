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

// Contract for opening a full-screen code editor over the dashboard for a compile-on-save widget source.
//
// The widget backend has no "save the source" endpoint — authored source is compiled into an append-only
// version (POST .../widgets/{id}/compile), so the editor is inherently a *compile*-on-save surface: each save
// round-trips to the bot, the bundle builds, and the overlay hot-reloads. The editor therefore stays open and
// shows the build result inline instead of returning on the first save. It closes only when the operator
// explicitly closes it.
//
// Per-target implementations live in the wasmJs and jvm source sets:
//   Web (wasmJs):   a full-screen DOM overlay hosting a CodeMirror 6 editor (VS Code-like — line numbers,
//                   syntax highlighting, bracket matching, auto-indent), loaded from a CDN with a monospace
//                   <textarea> fallback when the CDN is unreachable (offline self-host). Its state is staged on
//                   a global slot and polled by the Kotlin side — the same handshake as AudioFilePicker.
//   Desktop (jvm):  a non-modal Swing dialog with a monospace text area, a "Save & Compile" button, and a
//                   result label — the coroutine drives each compile off the button and posts the result back.
interface CustomCodeEditorIO {
    /**
     * Opens the editor titled [title], seeded with [initialCode], with highlighting hinted by [language]
     * (e.g. "vanilla" / "vue"). On each "Save & Compile" the editor invokes [compile] with the current source
     * and renders the returned [CompileFeedback] inline (green on success, red on failure); the editor stays
     * open across compiles. Suspends until the operator closes the editor, then returns.
     */
    suspend fun editAndCompile(
        title: String,
        initialCode: String,
        language: String,
        compile: suspend (String) -> CompileFeedback,
    )
}

/** The inline result of one compile: whether the build succeeded and the short message to show the operator. */
data class CompileFeedback(val ok: Boolean, val message: String)

// The per-target [CustomCodeEditorIO] implementation. The controller depends on the interface and fakes it in
// tests; production wiring constructs the platform actual.
expect class CustomCodeEditor() : CustomCodeEditorIO {
    override suspend fun editAndCompile(
        title: String,
        initialCode: String,
        language: String,
        compile: suspend (String) -> CompileFeedback,
    )
}
