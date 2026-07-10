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
     * Run the token-returning OAuth dance for [flow] against [baseUrl] and return the captured
     * [SessionTokens]. On desktop this suspends until the loopback callback fires (or the user cancels
     * / it times out); on web it triggers the redirect and the captured session is read after reload.
     */
    suspend fun authorize(baseUrl: String, flow: OAuthFlow): ApiResult<SessionTokens>

    /**
     * Run the token-returning LOGIN redirect for a non-Twitch [providerKey] (e.g. `youtube` / `kick` /
     * `twitter`) against [baseUrl] via the generic per-provider authorize route
     * (`/api/v1/auth/{providerKey}/authorize`). Same two shapes as [authorize]: on desktop the loopback
     * captures the returned tokens; on web the page navigates to the backend and this never resolves — the
     * session arrives on reload via readReturnedSession. Twitch keeps its dedicated [authorize] path.
     */
    suspend fun authorizeProvider(baseUrl: String, providerKey: String): ApiResult<SessionTokens>

    /**
     * Run a token-LESS connect dance and resolve when the provider returns. Used for the bot-account
     * and integration connects, where the resulting token is stored SERVER-SIDE and only a
     * success/error signal returns to the client (no [SessionTokens]).
     *
     * [authorizeUrlFor] is handed the redirect the client wants the backend to return to (the desktop
     * loopback URL; on web, an empty string — the served origin is implicit) and must yield the
     * provider/backend authorize URL to open. On desktop the loopback callback resolves this; on web
     * the page navigates away and never resolves (the caller confirms by re-polling status on reload).
     */
    suspend fun awaitConnect(authorizeUrlFor: suspend (redirect: String) -> ApiResult<String>): ApiResult<Unit>
}
