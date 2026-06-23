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

// The Twitch OAuth launcher (frontend.md §6) — the per-target seam that opens the system browser,
// drives the dance against the backend, and returns the resulting session.
//
//   Desktop: RFC-8252 loopback — bind a transient 127.0.0.1:<port> listener, open the browser to
//            the backend streamer-login URL with redirect=http://127.0.0.1:<port>/cb, capture the
//            tokens the backend delivers to the loopback, then stop the listener.
//   Web:     same-origin redirect — navigate the page to the backend login; the backend completes
//            the dance and returns the session to the served origin.
expect class OAuthLauncher() {
    /**
     * Run the OAuth dance for [flow] against [baseUrl] and return the captured [SessionTokens].
     * On desktop this suspends until the loopback callback fires (or the user cancels / it times
     * out); on web it triggers the redirect and the captured session is read after reload.
     */
    suspend fun authorize(baseUrl: String, flow: OAuthFlow): ApiResult<SessionTokens>
}
