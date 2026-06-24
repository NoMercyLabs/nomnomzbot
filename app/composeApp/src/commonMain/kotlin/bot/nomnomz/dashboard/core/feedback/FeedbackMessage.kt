// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.feedback

import org.jetbrains.compose.resources.StringResource

// One process-wide outcome message the app frame surfaces after an action or an OAuth/connect return
// (frontend.md "the app frame hosts the message"). A controller emits it the instant it knows the
// outcome; the shell-level host (the single FeedbackHost in the app frame) renders it on whatever page
// is showing, so the message survives a page navigation or a post-OAuth page rebuild.
//
// i18n: the text is carried as a [StringResource] reference (+ optional [formatArgs]) — NEVER a
// pre-rendered string — so the host resolves it with `stringResource` in the chosen language. A dynamic
// detail (e.g. a backend error message) is passed as a format arg into a template key that has a "%1$s"
// slot, mirroring the existing `*_action_error` strings. This keeps every user-facing string a resource.

/** Whether the outcome was a success, a failure, or a neutral notice — drives the host's styling + dwell. */
enum class FeedbackKind {
    Success,
    Error,
    Info,
}

/**
 * A single outcome the app frame should surface. [label] is the localized template; [formatArgs] fill its
 * `%1$s`/`%1$d` slots (e.g. the backend's error detail) and are applied by the host via `stringResource`.
 */
data class FeedbackMessage(
    val kind: FeedbackKind,
    val label: StringResource,
    val formatArgs: List<Any> = emptyList(),
)
