// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.connection

import kotlin.test.Test
import kotlin.test.assertEquals

// Proves the session state machine that drives the App gate (frontend.md §5). The gate
// renders Connect while NotConnected and the Main shell while Connected, so these phase
// transitions ARE the routing decision — if they break, the gate routes wrong.
class SessionStoreTest {

    @Test
    fun starts_not_connected_so_the_gate_shows_connect() {
        val store = SessionStore()
        assertEquals(SessionPhase.NotConnected, store.phase.value)
    }

    @Test
    fun connect_moves_to_connected_so_the_gate_shows_the_shell() {
        val store = SessionStore()
        store.connect()
        assertEquals(SessionPhase.Connected, store.phase.value)
    }

    @Test
    fun disconnect_returns_to_not_connected_so_the_gate_shows_connect_again() {
        val store = SessionStore()
        store.connect()
        store.disconnect()
        assertEquals(SessionPhase.NotConnected, store.phase.value)
    }
}
