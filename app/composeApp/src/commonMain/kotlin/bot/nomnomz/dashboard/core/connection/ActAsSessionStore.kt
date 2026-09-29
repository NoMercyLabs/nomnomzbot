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

import kotlinx.serialization.Serializable

/**
 * The NON-SECRET act-as state that has to cross an app reload: where the operator was when they started acting
 * (so Exit lands them back there) and a one-shot notice for the next boot. It never holds a token and it never
 * decides who a boot is — that comes from `/auth/refresh` alone ([bot.nomnomz.dashboard.core.network.AuthPayload.impersonation]).
 */
interface ActAsSessionStore {
    /** Where the operator was when they started acting in this tab, or null when they have not. */
    fun readReturnLocation(): String?

    fun writeReturnLocation(location: String)

    /** Forget the return location (the act-as session is over). A pending notice is kept. */
    fun clear()

    /** Leave a one-shot notice for the next boot (an act-as session that ended across a reload). */
    fun putNotice(notice: ActAsEndNotice)

    /** Read and remove the pending notice, so it shows exactly once. */
    fun takeNotice(): ActAsEndNotice?
}

/** Why an act-as session ended outside the operator's own Exit click, told to the operator after the reload. */
@Serializable
data class ActAsEndNotice(val reason: ActAsEndReason)

@Serializable
enum class ActAsEndReason {
    /** The support session was over (expired, or ended elsewhere): the server handed back the operator. */
    Expired,
}

/**
 * The per-target custody for [ActAsSessionStore]:
 *   Web:     sessionStorage — this tab only, gone when the tab closes.
 *   Desktop: process memory — survives the in-process app restart, never a relaunch.
 */
expect class ActAsSessionVault() : ActAsSessionStore {
    override fun readReturnLocation(): String?

    override fun writeReturnLocation(location: String)

    override fun clear()

    override fun putNotice(notice: ActAsEndNotice)

    override fun takeNotice(): ActAsEndNotice?
}
