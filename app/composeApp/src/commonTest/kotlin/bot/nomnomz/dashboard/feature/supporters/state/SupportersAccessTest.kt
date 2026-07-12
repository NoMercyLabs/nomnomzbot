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

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

// Proves the Supporters page capability gate (supporter-events.md §5): connect / disconnect / enable-toggle are
// held to the Broadcaster-only, Critical `supporters:config:write`, resolved from the caller's held action keys
// (which fold in any per-action override) — not a raw management role. The screen turns this predicate into a
// disable-with-reason ManageGate, so proving the predicate proves the write gate.
class SupportersAccessTest {

    @Test
    fun can_config_only_when_the_config_key_is_held() {
        assertTrue(SupportersAccess.canConfig(setOf(SupportersAccess.ConfigAction)))
        assertTrue(
            SupportersAccess.canConfig(setOf("something:else", SupportersAccess.ConfigAction)),
            "the config key alongside unrelated keys still grants config",
        )
    }

    @Test
    fun cannot_config_without_the_key() {
        // A read-only caller (only holds the read key) and an empty held set can BOTH read the page but neither may
        // wire a money source — the write floor stays closed.
        assertFalse(SupportersAccess.canConfig(emptySet()))
        assertFalse(SupportersAccess.canConfig(setOf(SupportersAccess.ReadAction)))
    }

    @Test
    fun the_action_keys_match_the_backend_contract() {
        // The exact backend `[RequireAction("…")]` keys — a rename on either side must break this, not silently
        // mis-gate the page.
        assertEquals("supporters:read", SupportersAccess.ReadAction)
        assertEquals("supporters:config:write", SupportersAccess.ConfigAction)
    }
}
