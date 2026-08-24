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

// The run-history surface's capability gate (frontend-ia.md §7 — hide below the read floor). Read-only: there
// is no write control to gate, only whether the caller may see the run history at all. A caller below the
// read floor never sees the "History" entry point (hidden, not disabled-with-reason, per §7's read-floor rule).
object PipelineExecutionHistoryAccess {
    /** The backend action key that gates reading pipeline run history (PipelineExecutionsController). */
    const val ReadAction: String = "pipelines:read"

    /** Whether the caller may see the run-history surface — they hold [ReadAction] in their resolved keys. */
    fun canRead(heldActionKeys: Set<String>): Boolean = ReadAction in heldActionKeys
}
