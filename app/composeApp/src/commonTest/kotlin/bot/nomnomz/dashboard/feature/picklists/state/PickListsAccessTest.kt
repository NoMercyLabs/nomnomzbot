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

import kotlin.test.Test
import kotlin.test.assertFalse
import kotlin.test.assertTrue

// Proves the Pick Lists page's capability gate (the write/delete split the PickListsScreen renders through
// ManageGate). The consequence under test: the create/edit control and the delete control are gated on DIFFERENT
// resolved keys and never crossed — so a broadcaster who lowered only `picklists:write` to a VIP grants editing
// without granting deletion. If the wiring regressed (delete gated on the write key, or vice-versa), a delegated
// VIP would silently gain or lose the destructive control; these cases fail for exactly that reason.
class PickListsAccessTest {

    @Test
    fun write_key_present_without_delete_key_enables_edit_but_not_delete() {
        // A VIP the broadcaster delegated list-EDITING to holds `picklists:write` but not `picklists:delete` (plus
        // `picklists:read` to see the page at all). Create/edit is enabled; delete stays off.
        val held: Set<String> = setOf("picklists:read", "picklists:write")

        assertTrue(PickListsAccess.canWrite(held), "picklists:write must enable create/edit")
        assertFalse(PickListsAccess.canDelete(held), "delete must stay disabled without picklists:delete")
    }

    @Test
    fun delete_key_gates_only_delete_and_not_write() {
        // The gates are independent and not crossed: a caller holding only `picklists:delete` may delete but not edit.
        val held: Set<String> = setOf("picklists:delete")

        assertFalse(PickListsAccess.canWrite(held), "delete permission alone must not enable editing")
        assertTrue(PickListsAccess.canDelete(held))
    }

    @Test
    fun both_keys_enable_both_controls_and_an_empty_set_disables_both() {
        // An Editor+ clears both default floors, so their resolved held set contains both keys → both controls live.
        val both: Set<String> = setOf("picklists:write", "picklists:delete")
        assertTrue(PickListsAccess.canWrite(both))
        assertTrue(PickListsAccess.canDelete(both))

        // A caller below the floors (or a fail-closed resolve) holds neither → both controls disabled-with-reason.
        assertFalse(PickListsAccess.canWrite(emptySet()))
        assertFalse(PickListsAccess.canDelete(emptySet()))
    }
}
