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

// Remembers WHICH channel the operator last had active, so a web reload / desktop relaunch restores the same
// channel context instead of snapping back to the owned-first default (the operator switched to a channel they
// moderate, reloaded, and expects to still be looking at it). The active channel is not a secret — it is a plain
// tenant GUID — so it persists as a plain value, mirroring the [ActiveProfileStore] seam (localStorage on web,
// a file on desktop). Depending on the interface (not the per-target [ActiveChannelVault]) keeps [SessionStore]
// unit-testable with an in-memory fake.
interface ActiveChannelStore {
    /** The last active channel id, or null when none was remembered (fresh session / after logout). */
    suspend fun read(): String?

    /** Persist [channelId] as the active channel so a reload restores it. Called only on an explicit switch. */
    suspend fun write(channelId: String)

    /** Forget the remembered channel (on logout / session drop). */
    suspend fun clear()
}
