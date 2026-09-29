// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.connect.state

import bot.nomnomz.dashboard.core.connection.ConnectionProfile
import bot.nomnomz.dashboard.core.connection.SessionTokens
import bot.nomnomz.dashboard.core.network.AuthPayload

/**
 * The act-as steps the sign-in flow runs at its three identity seams: the boot restore, the refresh answer it
 * gets, and a logout. Implemented by the act-as coordinator; [NoActAs] for a flow that never acts as anyone.
 */
interface ActAsConnectHooks {
    /** The act-as token this client must send with the boot refresh (native); null on web (HttpOnly cookie). */
    fun heldActAsToken(): String?

    /** The boot refresh answered with an open act-as session: open it. True when the target's session is live. */
    suspend fun resume(profile: ConnectionProfile, operatorTokens: SessionTokens?, answer: AuthPayload): Boolean

    /** The boot refresh answered with the operator's own session: forget any act-as state left from before. */
    fun onOperatorSession()

    /** A logout pressed while acting has run: drop the act-as state and reload, so no data of the target survives. */
    fun onLoggedOutWhileActing()
}

/** A sign-in flow with no act-as (tests, or a surface that never impersonates). */
object NoActAs : ActAsConnectHooks {
    override fun heldActAsToken(): String? = null

    override suspend fun resume(profile: ConnectionProfile, operatorTokens: SessionTokens?, answer: AuthPayload): Boolean =
        false

    override fun onOperatorSession() = Unit

    override fun onLoggedOutWhileActing() = Unit
}
