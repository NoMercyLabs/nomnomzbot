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

import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// Global session Store (frontend.md §4 — global state lives in injected Store singletons
// exposing StateFlow). This is the FOUNDATION seed of the real ConnectionStore +
// SessionStore (frontend.md §6): an in-memory phase that drives the App gate.
//
// Next slice replaces `connect()` with the real direct-connect flow (ConnectionProfile +
// TokenVault + REST/SignalR client); the StateFlow surface stays, so the gate is unchanged.
class SessionStore {
    private val _phase: MutableStateFlow<SessionPhase> = MutableStateFlow(SessionPhase.NotConnected)

    /** The current session phase the gate observes. */
    val phase: StateFlow<SessionPhase> = _phase.asStateFlow()

    /** Establish a (mock, in-memory) session — moves the gate to the Main shell. */
    fun connect() {
        _phase.value = SessionPhase.Connected
    }

    /** Drop the session — returns the gate to the Connect screen. */
    fun disconnect() {
        _phase.value = SessionPhase.NotConnected
    }
}
