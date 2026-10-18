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

import bot.nomnomz.dashboard.core.network.BuildError

// The editor's optional "Test run" panel (S-CODE-COLLAPSE): a captured dry-run of the artifact, surfaced inside
// the editor itself. Outcomes use [EditorOutcome] so `core/editor` stays free of `core/network` for the callers'
// `ApiResult<T>`; only the editor-underlinable [BuildError] is shared.

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
    val variablesSet: Map<String, String>? = null,
    val console: List<String>? = null,
    val timeline: List<EditorTestRunTimelineEntry> = emptyList(),
    /** Where the failing run threw, as editor-underlinable errors; empty when the run passed. */
    val errors: List<BuildError> = emptyList(),
)

/** One row of the ordered test-run timeline; [kind] is chat, effect or console. */
data class EditorTestRunTimelineEntry(val seq: Int, val kind: String, val text: String)

/**
 * Drives the editor's "Test run" panel (folded into the existing Run &amp; test side view). Passed to
 * [ProjectEditorIO.editAndCompile] only when the artifact supports a backend dry-run — currently code scripts;
 * null hides the panel (e.g. for widgets, whose Run view already renders their live iframe preview + fire bar).
 */
class EditorTestRun(
    /** The real event samples a run can fire; empty hides the trigger picker (the run stays "Manual"). */
    val triggers: List<EditorTestTrigger> = emptyList(),
    /** The panel's own words for the trigger and role pickers, resolved by the caller (never literal in the page). */
    val labels: EditorTestRunLabels = EditorTestRunLabels(),
    val run: suspend (
        variables: Map<String, String>,
        args: List<String>,
        trigger: String?,
        role: String?,
        // The files open in the editor right now; empty when the page sent none (the saved version then runs).
        files: Map<String, String>,
    ) -> EditorOutcome<EditorTestRunResult>,
)

/** One trigger sample in the picker: its [id] goes back on the run, [label] is shown, [variables] pre-fill the box. */
data class EditorTestTrigger(val id: String, val label: String, val variables: Map<String, String>)

/** Localized words the test panel's pickers need; [roles] is keyed by the role token the server accepts. */
data class EditorTestRunLabels(
    val manual: String = "",
    val trigger: String = "",
    val role: String = "",
    val roles: Map<String, String> = emptyMap(),
    val timeline: String = "",
    val timelineChat: String = "",
    val timelineEffect: String = "",
    val timelineConsole: String = "",
)
