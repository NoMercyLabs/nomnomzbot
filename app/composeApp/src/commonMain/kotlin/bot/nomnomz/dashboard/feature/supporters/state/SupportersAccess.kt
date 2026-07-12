// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.supporters.state

// The Supporters page's capability gate (supporter-events.md §5). Reading the page (connections + the event feed)
// floors at Moderator (`supporters:read`, enforced by the shell nav), while connect / disconnect / enable-toggle
// are Broadcaster-only and Critical — they add or remove a money source — gated on `supporters:config:write`. The
// page reflects that write floor through the caller's RESOLVED held action keys
// (`ResolvedAccess.heldActionKeys`, which folds in any per-action override), never a raw management role — so a
// Moderator can watch the feed while only a Broadcaster (holding `supporters:config:write`) can wire a provider.
// This is a pure predicate over the held-key set, testable without rendering Compose; the screen only turns the
// outcome into a disable-with-reason `ManageGate`.
object SupportersAccess {
    /** The backend action key that gates reading the Supporters page (SupportersController read actions). */
    const val ReadAction: String = "supporters:read"

    /** The Broadcaster-only key gating connect / disconnect / enable-toggle (a money source, Critical). */
    const val ConfigAction: String = "supporters:config:write"

    /**
     * Whether the caller may connect / disconnect / toggle a supporter provider — they hold [ConfigAction]. Drives
     * whether the connection tiles' write controls are live or disabled-with-reason.
     */
    fun canConfig(heldActionKeys: Set<String>): Boolean = ConfigAction in heldActionKeys
}
