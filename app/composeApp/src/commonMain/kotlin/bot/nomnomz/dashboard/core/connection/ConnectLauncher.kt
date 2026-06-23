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

import bot.nomnomz.dashboard.core.network.ApiResult

// The token-less connect seam the integrations state holder depends on (the existing "depend on
// interfaces" convention). [OAuthLauncher] is an expect/actual class — not directly fakeable — so the
// holder talks to this interface and the real flow is the thin [OAuthConnectLauncher] adapter over the
// launcher. Tests substitute a fake that drives the authorize-URL provider and returns a canned outcome.
interface ConnectLauncher {
    /**
     * Run a token-less connect dance: hand [authorizeUrlFor] the redirect the backend should return to
     * (the desktop loopback; empty on web), open the resulting authorize URL, and resolve when the
     * provider returns (desktop) — the token is vaulted server-side, so only a success/error is surfaced.
     */
    suspend fun awaitConnect(
        authorizeUrlFor: suspend (redirect: String) -> ApiResult<String>
    ): ApiResult<Unit>
}

/** The real adapter: delegates straight to the per-target [OAuthLauncher]. */
class OAuthConnectLauncher(private val launcher: OAuthLauncher) : ConnectLauncher {
    override suspend fun awaitConnect(
        authorizeUrlFor: suspend (redirect: String) -> ApiResult<String>
    ): ApiResult<Unit> = launcher.awaitConnect(authorizeUrlFor)
}
