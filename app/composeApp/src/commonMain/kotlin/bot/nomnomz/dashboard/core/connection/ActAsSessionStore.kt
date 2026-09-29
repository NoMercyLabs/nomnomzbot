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
 * The act-as (admin impersonation) session marker that survives an app reload, so a reload while acting boots
 * straight back into the target's session instead of silently returning to the operator.
 *
 * It holds the act-as ACCESS token only. That token is access-only (the server mints no refresh token for it),
 * time-boxed to the backing support session and revocable server-side. The operator's refresh token never lands
 * here: on web it stays in the HttpOnly cookie, on desktop in the OS vault.
 */
interface ActAsSessionStore {
    /** The live act-as session, or null when the operator is not acting as anyone. */
    fun read(): PersistedActAs?

    fun write(session: PersistedActAs)

    fun clear()

    /** Leave a one-shot notice for the next boot (an act-as session that ended across a reload). */
    fun putNotice(notice: ActAsEndNotice)

    /** Read and remove the pending notice, so it shows exactly once. */
    fun takeNotice(): ActAsEndNotice?
}

/**
 * The act-as session carried across a reload. [returnLocation] is where the operator was when they started
 * acting (web: the route hash), so Exit lands them back there; it is never read while acting.
 */
@Serializable
data class PersistedActAs(
    val accessToken: String,
    val expiresAt: String,
    val accessGrantId: String,
    val displayName: String,
    val returnLocation: String,
)

/** Why an act-as session ended outside the operator's own Exit click, told to the operator after the reload. */
@Serializable
data class ActAsEndNotice(val reason: ActAsEndReason, val detail: String? = null)

@Serializable
enum class ActAsEndReason {
    /** The act-as token was rejected or ran out: the session had already ended server-side. */
    Expired,

    /** The operator exited, but the server did not confirm the grant was revoked. */
    RevokeFailed,
}

/**
 * The per-target custody for [PersistedActAs]:
 *   Web:     sessionStorage — survives a reload of THIS tab only, is never shared with other tabs, and is gone
 *            when the tab closes.
 *   Desktop: process memory — survives the in-process app restart, never a relaunch.
 */
expect class ActAsSessionVault() : ActAsSessionStore {
    override fun read(): PersistedActAs?

    override fun write(session: PersistedActAs)

    override fun clear()

    override fun putNotice(notice: ActAsEndNotice)

    override fun takeNotice(): ActAsEndNotice?
}
