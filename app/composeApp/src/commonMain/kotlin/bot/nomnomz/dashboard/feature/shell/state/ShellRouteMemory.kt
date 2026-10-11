// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.shell.state

import bot.nomnomz.dashboard.core.connection.SessionStore
import bot.nomnomz.dashboard.core.navigation.ShellAddress
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute

/**
 * The shell's saved-address rule. The saved address (web: the address-bar hash and its history entries) belongs to
 * the signed-in account. An act-as session never reads it (the target opens on their own default page), never
 * writes it (the target's pages never enter the operator's history), and never follows browser Back/Forward
 * through it. Exit reloads the app at the page the operator started acting from.
 *
 * The address names a channel only until the shell first writes it; from then on the address is the shell's output,
 * not its input. A channel switch remounts the shell while the address still names the previous channel, so reading
 * that login again switched straight back (and on). One instance lives for the whole app, outside the shell.
 */
class ShellRouteMemory(
    private val readSaved: () -> ShellAddress,
    private val writeSaved: (ShellAddress) -> Unit,
    private val sessionStore: SessionStore,
) {
    private var shellOwnsTheAddress: Boolean = false

    /** The address the shell opens on; its login is null once the shell has written its own address. */
    fun openingAddress(): ShellAddress {
        if (sessionStore.isActingAs) return ShellAddress(login = null, route = ShellRoute.Home)
        val saved: ShellAddress = readSaved()
        return if (shellOwnsTheAddress) saved.copy(login = null) else saved
    }

    /** Save [address] as the current location, never while acting. */
    fun save(address: ShellAddress) {
        if (sessionStore.isActingAs) return
        writeSaved(address)
        shellOwnsTheAddress = true
    }

    /** The address a browser Back/Forward move asks for, or null when the move must not steer the shell. */
    fun externalMove(address: ShellAddress): ShellAddress? = if (sessionStore.isActingAs) null else address
}
