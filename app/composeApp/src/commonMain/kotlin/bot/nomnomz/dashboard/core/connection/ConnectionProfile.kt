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

/** Which Twitch OAuth dance to run — maps to the backend `state` flow (`user` / `channel_bot`). */
enum class OAuthFlow {
    /** Streamer login — this slice. */
    Streamer,

    /** Bot-account authorization — next slice, same launcher seam. */
    Bot,
}
