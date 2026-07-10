// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.quotes.state

// The Quotes page's capability gate. A broadcaster can lower `quotes:write` / `quotes:delete` to a VIP/Sub via a
// per-action override (roles-permissions), so the page gates its create/edit and delete controls on the caller's
// RESOLVED held action keys (`ResolvedAccess.heldActionKeys`) rather than on a management role. A delegated VIP
// who holds `quotes:write` but not `quotes:delete` may add/edit but not delete; a Moderator/Editor/Broadcaster
// clears both default floors and so holds both keys — their controls are unchanged. These are pure predicates
// over the held-key set, independent of each other, so the wrong control can never be lit for the wrong key —
// testable without rendering Compose; the screen only turns the outcome into a disable-with-reason `ManageGate`.
object QuotesAccess {
    /** The backend action key that gates creating and editing a quote (QuotesController write actions). */
    const val WriteAction: String = "quotes:write"

    /** The backend action key that gates deleting a quote (QuotesController delete action). */
    const val DeleteAction: String = "quotes:delete"

    /** Whether the caller may create/edit a quote — they hold [WriteAction] in their resolved [heldActionKeys]. */
    fun canWrite(heldActionKeys: Set<String>): Boolean = WriteAction in heldActionKeys

    /** Whether the caller may delete a quote — they hold [DeleteAction] in their resolved [heldActionKeys]. */
    fun canDelete(heldActionKeys: Set<String>): Boolean = DeleteAction in heldActionKeys
}
