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

// The multi-file project editor's optional auxiliary panels (S-CODE-COLLAPSE): version history and a captured
// dry-run. Both used to live on a separate pre-editor page (code scripts) or a Compose dialog reached from the
// list (widgets); this is the seam that lets either caller surface them INSIDE the editor itself — a side view
// next to Explorer/Search/Problems, matching the editor's own activity-bar pattern — instead of a page the
// operator has to leave the editor to reach. `core/editor` stays free of `core/network` on purpose (it is a
// generic bridge shared by widgets and code scripts), so outcomes are reduced to this file's own small
// [EditorOutcome], and callers map their `ApiResult<T>` onto it.

/** The result of one auxiliary-panel action the editor asked the host to perform. */
sealed interface EditorOutcome<out T> {
    data class Ok<T>(val value: T) : EditorOutcome<T>

    data class Failed(val message: String) : EditorOutcome<Nothing>
}

/** One row of the editor's version history list. */
data class EditorVersionSummary(
    val id: String,
    val version: Int,
    val validationStatus: String,
    val isCurrent: Boolean,
)

/** A page of version history, as returned by a load-more, rollback, or delete. */
data class EditorVersionsPage(
    val versions: List<EditorVersionSummary>,
    val hasMore: Boolean,
)

/**
 * Drives the editor's "History" side view. Passed to [ProjectEditorIO.editAndCompile] only when the artifact
 * being edited has real version history to show — the editor hides the History activity item entirely when this
 * is null, rather than showing an always-empty panel.
 */
class EditorHistory(
    /** The first page of version history, newest first, as already loaded by the caller before opening the editor. */
    val initialVersions: List<EditorVersionSummary>,
    /** Whether the backend reports a further, not-yet-loaded page beyond [initialVersions]. */
    val initialHasMore: Boolean,
    /** Fetch the next page and return the FULL list so far (appended), matching [CodeScriptsController]'s own paging. */
    val loadMore: suspend () -> EditorOutcome<EditorVersionsPage>,
    /** Re-publish [versionId] as the active version; returns the refreshed first page on success. */
    val rollback: suspend (versionId: String) -> EditorOutcome<EditorVersionsPage>,
    /** Delete the saved version [versionId] (refused by the backend if it is the published one). */
    val delete: suspend (versionId: String) -> EditorOutcome<EditorVersionsPage>,
)

/** One captured side effect from a test run — a chat message send, a storage write, etc. — never actually performed. */
data class EditorTestRunEffect(val name: String, val argsPreview: String)

/** The outcome of one dry-run of the artifact's current version against sample input. */
data class EditorTestRunResult(
    val success: Boolean,
    val durationMs: Long,
    val hostCallCount: Int,
    val error: String?,
    val chatOutput: List<String>,
    val effects: List<EditorTestRunEffect>,
)

/**
 * Drives the editor's "Test run" panel (folded into the existing Run &amp; test side view). Passed to
 * [ProjectEditorIO.editAndCompile] only when the artifact supports a backend dry-run — currently code scripts;
 * null hides the panel (e.g. for widgets, whose Run view already renders their live iframe preview + fire bar).
 */
class EditorTestRun(
    val run: suspend (variables: Map<String, String>, args: List<String>) -> EditorOutcome<EditorTestRunResult>,
)
