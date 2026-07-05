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

// Per-target persistence of the last active channel id (frontend.md §6). Like the active profile — and unlike
// the session tokens — the channel id is NOT a secret (a plain tenant GUID), so it persists as a plain value:
//
//   Desktop: a file under the OS app-data dir (alongside the token + profile vaults).
//   Web:     localStorage (survives a tab reload, so the served-origin session restores the same channel).
expect class ActiveChannelVault() : ActiveChannelStore {
    override suspend fun read(): String?

    override suspend fun write(channelId: String)

    override suspend fun clear()
}
