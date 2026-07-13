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

// Contract for opening a full-screen code editor over the dashboard and returning the edited source. The
// custom-widget code editor (Overlays page) uses it to author a widget's HTML/CSS/JS. Per-target
// implementations live in the wasmJs and jvm source sets:
//
//   Web (wasmJs):   a full-screen DOM overlay hosting a CodeMirror 6 editor (VS Code-like — line numbers,
//                   syntax highlighting, bracket matching, auto-indent), loaded from a CDN with a monospace
//                   <textarea> fallback when the CDN is unreachable (offline self-host). Its outcome is staged
//                   on a global slot and polled by the Kotlin side — the same handshake as AudioFilePicker.
//   Desktop (jvm):  a native modal Swing dialog with a monospace text area and Save / Cancel.
interface CustomCodeEditorIO {
    /**
     * Opens the editor titled [title], seeded with [initialCode], with highlighting for [language]
     * (e.g. "html"). Suspends until the user saves or cancels. Returns the edited source on save (which may be
     * an empty string — the operator cleared the code), or null if the edit was cancelled.
     */
    suspend fun edit(title: String, initialCode: String, language: String): String?
}

// The per-target [CustomCodeEditorIO] implementation. The controller depends on the interface and fakes it in
// tests; production wiring constructs the platform actual.
expect class CustomCodeEditor() : CustomCodeEditorIO {
    override suspend fun edit(title: String, initialCode: String, language: String): String?
}
