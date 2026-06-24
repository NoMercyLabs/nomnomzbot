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

// Remembers WHICH backend the dashboard was last signed in against, so a relaunch can restore that
// session (frontend.md §6 — the "remembered" tier). The session tokens live in the [SessionTokenStore]
// keyed by profile id, but that id is minted randomly per connect — without the profile persisted, a
// boot has no key to look it up by. This store closes that gap: it holds the last active
// [ConnectionProfile] (id + base URL), written on connect and cleared on disconnect.
//
// Depending on the interface (not the per-target [ActiveProfileVault]) keeps the [SessionStore] testable
// with an in-memory fake, mirroring the [SessionTokenStore] seam.
interface ActiveProfileStore {
    suspend fun read(): ConnectionProfile?

    suspend fun write(profile: ConnectionProfile)

    suspend fun clear()
}
