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

/** Web browse engine — a deliberate no-op (single-origin, served by its own bot). */
actual fun lanDiscovery(): LanDiscovery = NoOpLanDiscovery

// Web mDNS browse — a deliberate no-op (frontend.md §6). The web build is served by ITS OWN bot, so it
// is single-origin: there is nothing to discover and no LAN browse primitive in the browser. The
// discovered set stays permanently empty; start/stop do nothing. The Connect screen renders no
// discovered rows on web, exactly as intended.
private object NoOpLanDiscovery : LanDiscovery {

    // The web build cannot browse the LAN — the Connect screen hides the discovery section entirely.
    override val isSupported: Boolean = false

    private val _discovered: MutableStateFlow<List<ConnectionProfile>> = MutableStateFlow(emptyList())
    override val discovered: StateFlow<List<ConnectionProfile>> = _discovered.asStateFlow()

    override fun start() = Unit

    override fun stop() = Unit
}
