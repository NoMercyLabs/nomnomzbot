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

// Per-target persistence of the last active [ConnectionProfile] (frontend.md §6). Unlike the session
// tokens, the profile is NOT a secret — it is plain connection metadata (id + base URL) — so it persists
// as plain JSON on both targets:
//
//   Desktop: a file under the OS app-data dir (alongside the token vault).
//   Web:     localStorage (survives tab close, so the served-origin session is remembered like desktop).
expect class ActiveProfileVault() : ActiveProfileStore {
    override suspend fun read(): ConnectionProfile?

    override suspend fun write(profile: ConnectionProfile)

    override suspend fun clear()
}
