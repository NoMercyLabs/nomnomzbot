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

// The typed bot-account facade. The platform-shared bot's Twitch token is held SERVER-SIDE, so the
// client only ever (1) asks the backend for an authorize URL to open and (2) reads the resulting
// connection status — it never captures a bot token.
//
// Backend routes (AuthController, all admin-gated):
//   GET /api/v1/auth/twitch/bot?redirect_uri=…  →  StatusResponseDto<OAuthStartDto>
//   GET /api/v1/auth/twitch/bot/status          →  StatusResponseDto<BotStatusDto>
//
// Note: unlike the streamer login, `…/auth/twitch/bot` does NOT redirect — it returns the authorize URL
// as JSON. The client opens that URL; the callback returns only `bot_connected=true` to the loopback
// (no token), and the client confirms via [status].
interface BotAuthApi {
    /**
     * Ask the backend for the bot authorize URL to open. [loopbackRedirect] is the desktop loopback the
     * backend should return the `bot_connected=true` signal to (RFC-8252); on web it is blank (the
     * served origin is implicit and the backend returns there itself).
     */
    suspend fun start(loopbackRedirect: String): ApiResult<OAuthStart>

    /** The current bot-account connection status — the authoritative connected check after a connect. */
    suspend fun status(): ApiResult<BotStatus>
}

class RestBotAuthApi(private val client: ApiClient) : BotAuthApi {

    override suspend fun start(loopbackRedirect: String): ApiResult<OAuthStart> {
        val query: String =
            if (loopbackRedirect.isBlank()) "" else "?redirect_uri=${loopbackRedirect.encodeQuery()}"
        return client.getEnvelope("api/v1/auth/twitch/bot$query")
    }

    override suspend fun status(): ApiResult<BotStatus> =
        client.getEnvelope("api/v1/auth/twitch/bot/status")
}
