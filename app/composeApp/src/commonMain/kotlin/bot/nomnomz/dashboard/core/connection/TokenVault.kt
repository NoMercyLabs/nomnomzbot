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

// Per-target secure custody of the session tokens, keyed by profile (frontend.md §6) — matching
// the backend secret-custody rule (OS-native vault on desktop; first-party sessionStorage on web).
//
//   Desktop: persisted under the OS app-data dir (this slice); the OS keychain (DPAPI/Keychain/
//            libsecret) replaces the file store in the secret-custody slice — same interface.
//   Web:     sessionStorage (cleared on tab close); the refresh token rides the backend session.
expect class TokenVault() : SessionTokenStore {
    override suspend fun read(profileId: String): SessionTokens?

    override suspend fun write(profileId: String, tokens: SessionTokens)

    override suspend fun clear(profileId: String)
}
