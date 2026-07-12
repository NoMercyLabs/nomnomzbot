// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.picklists.state

// The Pick Lists page's capability gate. A broadcaster can lower `picklists:write` / `picklists:delete` to a
// VIP/Sub via a per-action override (roles-permissions), so the page gates its create/edit and delete controls on
// the caller's RESOLVED held action keys (`ResolvedAccess.heldActionKeys`) rather than on a management role. A
// delegated VIP who holds `picklists:write` but not `picklists:delete` may add/edit but not delete; a
// Moderator/Editor/Broadcaster clears both default floors and so holds both keys — their controls are unchanged.
// These are pure predicates over the held-key set, independent of each other, so the wrong control can never be lit
// for the wrong key — testable without rendering Compose; the screen only turns the outcome into a
// disable-with-reason `ManageGate`.
object PickListsAccess {
    /** The backend action key that gates creating and editing a pick-list (PickListsController write actions). */
    const val WriteAction: String = "picklists:write"

    /** The backend action key that gates deleting a pick-list (PickListsController delete action). */
    const val DeleteAction: String = "picklists:delete"

    /** Whether the caller may create/edit a pick-list — they hold [WriteAction] in their resolved [heldActionKeys]. */
    fun canWrite(heldActionKeys: Set<String>): Boolean = WriteAction in heldActionKeys

    /** Whether the caller may delete a pick-list — they hold [DeleteAction] in their resolved [heldActionKeys]. */
    fun canDelete(heldActionKeys: Set<String>): Boolean = DeleteAction in heldActionKeys
}
