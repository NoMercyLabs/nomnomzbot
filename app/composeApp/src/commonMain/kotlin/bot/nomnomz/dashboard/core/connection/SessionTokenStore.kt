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

// The common contract the SessionStore depends on for token custody — the per-target [TokenVault]
// implements it. Depending on the interface (not the expect class) keeps the store testable with a
// fake and lets the secret-custody slice swap the OS-keychain impl behind the same seam.
interface SessionTokenStore {
    suspend fun read(profileId: String): SessionTokens?

    suspend fun write(profileId: String, tokens: SessionTokens)

    suspend fun clear(profileId: String)
}
