// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.network

// The typed auth facade (frontend.md §3.1) — the only auth integration point for screens. It exposes
// streamer-session reads against the live backend; the request/response/envelope plumbing lives on the
// shared [ApiClient].
//
// Backend routes (read-only contract, AuthController):
//   GET /api/v1/auth/me  →  StatusResponseDto<CurrentUserDto>   (requires Authorization: Bearer)
class AuthApi(private val client: ApiClient) {

    /** The signed-in streamer for the active session, proving the captured JWT is valid. */
    suspend fun me(): ApiResult<CurrentUser> = client.getEnvelope("api/v1/auth/me")
}
