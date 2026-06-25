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

import kotlinx.serialization.Serializable

// The typed bot-account facade. The platform-shared bot's Twitch token is held SERVER-SIDE, so the
// client only ever (1) asks the backend for an authorize URL to open and (2) reads the resulting
// connection status — it never captures a bot token.
//
// Two connect paths mirror the streamer login (a Twitch client SECRET is OPTIONAL throughout):
//   • REDIRECT (a secret is configured): GET …/auth/twitch/bot returns an authorize URL the client opens;
//     the callback returns only `bot_connected=true` to the loopback (no token), confirmed via [status].
//   • DEVICE CODE (no secret — the shipped public client or BYOC): POST …/auth/twitch/bot/device mints a
//     user code the operator approves at twitch.tv/activate; the client polls …/bot/device/poll until the
//     shared bot is connected + vaulted server-side. This is what makes the bot connect work with no secret.
//
// Backend routes (AuthController, all admin-gated):
//   GET  /api/v1/auth/twitch/bot?redirect_uri=…  →  StatusResponseDto<OAuthStartDto>
//   GET  /api/v1/auth/twitch/bot/status          →  StatusResponseDto<BotStatusDto>
//   POST /api/v1/auth/twitch/bot/device          →  StatusResponseDto<DeviceCodeStartDto>
//   POST /api/v1/auth/twitch/bot/device/poll     →  StatusResponseDto<DeviceBotPollDto>
interface BotAuthApi {
    /**
     * Ask the backend for the bot authorize URL to open (the redirect path, used when a client secret is
     * configured). [loopbackRedirect] is the desktop loopback the backend should return the
     * `bot_connected=true` signal to (RFC-8252); on web it is blank (the served origin is implicit).
     */
    suspend fun start(loopbackRedirect: String): ApiResult<OAuthStart>

    /**
     * Begin the no-secret bot device login: the backend mints a short user code the operator approves at
     * [DeviceCodeStart.verificationUri]; the client then polls with [DeviceCodeStart.deviceCode].
     */
    suspend fun startDeviceLogin(): ApiResult<DeviceCodeStart>

    /**
     * Poll a bot device login once. Until the operator approves, [DeviceBotPoll.status] is `pending` /
     * `slow_down`; on `authorized` the shared bot account is connected + vaulted server-side and
     * [DeviceBotPoll.bot] carries its identity.
     */
    suspend fun pollDeviceLogin(deviceCode: String): ApiResult<DeviceBotPoll>

    /** The current bot-account connection status — the authoritative connected check after a connect. */
    suspend fun status(): ApiResult<BotStatus>

    /**
     * Disconnect the platform-shared bot account, revoking its Twitch token server-side (admin-only). After this
     * the operator can connect a different bot account via [start] / [startDeviceLogin] — this is how the bot is
     * "changed": disconnect, then connect the new one.
     */
    suspend fun disconnect(): ApiResult<Unit>
}

class RestBotAuthApi(private val client: ApiClient) : BotAuthApi {

    override suspend fun start(loopbackRedirect: String): ApiResult<OAuthStart> {
        val query: String =
            if (loopbackRedirect.isBlank()) "" else "?redirect_uri=${loopbackRedirect.encodeQuery()}"
        return client.getEnvelope("api/v1/auth/twitch/bot$query")
    }

    override suspend fun startDeviceLogin(): ApiResult<DeviceCodeStart> =
        client.postEnvelope("api/v1/auth/twitch/bot/device")

    override suspend fun pollDeviceLogin(deviceCode: String): ApiResult<DeviceBotPoll> =
        client.postEnvelope("api/v1/auth/twitch/bot/device/poll", BotDevicePollBody(deviceCode))

    override suspend fun status(): ApiResult<BotStatus> =
        client.getEnvelope("api/v1/auth/twitch/bot/status")

    override suspend fun disconnect(): ApiResult<Unit> =
        client.deleteUnit("api/v1/auth/twitch/bot")
}

/** One bot device-login poll: the loop status, plus the connected bot on `authorized`. */
@Serializable
data class DeviceBotPoll(val status: String, val bot: BotStatus? = null)

/** The bot device-poll request body — the opaque device code from [DeviceCodeStart]. */
@Serializable
private data class BotDevicePollBody(val deviceCode: String)
