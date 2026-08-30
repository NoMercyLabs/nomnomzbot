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

// Contract for opening the multi-file dev-platform code editor over the dashboard (dev-platform.md §5, Pillar 3).
// This is the multi-file successor to CustomCodeEditor: instead of one source string it edits a whole project —
// a `path → content` file map with a fixed entry file — the "proper `src/` folder" that is the base of the
// editor environment. Like its single-file predecessor it is a *compile*-on-save surface: each save round-trips
// the full project to the bot, which re-builds it server-side (the trust boundary) and hot-reloads the artifact,
// so the editor stays open and shows the build result inline instead of returning on the first save. It closes
// only when the operator explicitly closes it.
//
// A single-file artifact is simply a one-entry project, so both widgets and code scripts open this one editor.
//
// Per-target implementations live in the wasmJs and jvm source sets:
//   Web (wasmJs):   a served page (`/editor/index.html` — real HTML/CSS/ES modules, CDN-cacheable) mounted in an
//                   iframe in the app's shadow root; this file is only the postMessage bridge (open/save/compiled/
//                   close). Monaco, the file tree, the problems panel, and the esbuild/Vue live preview are that
//                   page's own business, not Kotlin's.
//   Desktop (jvm):  a non-modal Swing dialog with a file list + a monospace text area per file, a "Save & Compile"
//                   button, and a result label — the coroutine drives each compile off the button.
interface ProjectEditorIO {
    /**
     * Opens the editor titled [title] on the project [initialFiles] (`path → content`) whose build entry is
     * [entryPath], with highlighting hinted by [language] (e.g. `vue` / `script`). The operator edits, adds,
     * renames, and deletes files (never the [entryPath] file, which the manifest pins), and on each
     * "Save & Compile" the editor invokes [compile] with the CURRENT full file map and renders the returned
     * [CompileFeedback] inline (green on success, red on the real build error). The editor stays open across
     * compiles; suspends until the operator closes it, then returns.
     *
     * [sdkTypes] is the generated `nnz.d.ts` ambient declarations for the artifact's context (widget vs script),
     * fetched by the caller from `GET /api/v1/sdk/types.d.ts`. The web editor feeds it to an in-browser TypeScript
     * language service so `nnz.` autocompletes with the typed SDK surface and inline diagnostics flag misuse. Empty
     * when the declarations could not be fetched — the editor then simply omits autocomplete (a pure enhancement).
     *
     * [eventSubscriptions] is the widget's PERSISTED `EventSubscriptions` — the same list the overlay manifest
     * actually reads at runtime. The web editor's fire bar (test-fire-an-event) prefers this authoritative source
     * over scanning the source text for `.on('x')` calls, so the bar can't silently drift from what the widget
     * really receives; it falls back to the source scan only when this list is empty (a brand-new custom widget
     * that has not saved any declared subscriptions yet — S062 tracks making that list itself editable).
     */
    suspend fun editAndCompile(
        title: String,
        initialFiles: Map<String, String>,
        entryPath: String,
        language: String,
        sdkTypes: String = "",
        eventSubscriptions: List<String> = emptyList(),
        compile: suspend (Map<String, String>) -> CompileFeedback,
    )
}

// The per-target [ProjectEditorIO] implementation. Controllers depend on the interface and fake it in tests;
// production wiring constructs the platform actual.
expect class ProjectEditor() : ProjectEditorIO {
    override suspend fun editAndCompile(
        title: String,
        initialFiles: Map<String, String>,
        entryPath: String,
        language: String,
        sdkTypes: String,
        eventSubscriptions: List<String>,
        compile: suspend (Map<String, String>) -> CompileFeedback,
    )
}

/** The outcome of a compile the editor renders inline — green on success, red with the real build error. */
data class CompileFeedback(val ok: Boolean, val message: String)
