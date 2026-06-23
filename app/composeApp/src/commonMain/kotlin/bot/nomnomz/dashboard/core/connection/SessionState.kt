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

/** The connection/session phase that drives the App gate (frontend.md §5). */
enum class SessionPhase {
    /** No active connection — the gate shows the Connect screen. */
    NotConnected,

    /** A session exists — the gate shows the Main shell. */
    Connected,
}
