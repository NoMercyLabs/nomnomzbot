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

import kotlinx.serialization.Serializable

// The direct-connect heart (frontend.md §6): a backend the dashboard can point at. One app
// serves self-host + SaaS + LAN purely by switching the active profile's base URL.
@Serializable
data class ConnectionProfile(
    val id: String,
    val displayName: String,
    val baseUrl: String,
    val source: ProfileSource,
)

/** How a profile entered the saved list (frontend.md §6). */
@Serializable
enum class ProfileSource {
    /** Typed into the Connect screen's backend-URL field. */
    Manual,

    /** Surfaced by mDNS LAN discovery (native only). */
    Discovered,

    /** Synthesized from `window.location.origin` on the web build (single-origin). */
    ServedOrigin,
}

/** The session tokens issued by the backend auth callback (frontend.md §6). */
@Serializable
data class SessionTokens(
    val accessToken: String,
    val refreshToken: String? = null,
    val expiresAt: Long? = null,
)

/** Which Twitch OAuth dance to run — maps to the backend `state` flow (`user` / `bot`). */
enum class OAuthFlow {
    /** Streamer login — the callback returns the session tokens to the client. */
    Streamer,

    /**
     * Bot-account authorization. Unlike [Streamer], the bot dance keeps its Twitch token SERVER-SIDE:
     * the callback returns only a `bot_connected=true` (or `error=…`) signal to the loopback, never an
     * access token. The connect is therefore driven by [OAuthLauncher.awaitConnect] — open the
     * backend-issued authorize URL, wait for that signal — and confirmed by polling the bot status
     * endpoint, not by capturing tokens (see AuthController `GET /api/v1/auth/twitch/bot`).
     */
    Bot,
}
