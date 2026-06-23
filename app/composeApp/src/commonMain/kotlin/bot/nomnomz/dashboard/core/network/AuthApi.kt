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

// The typed auth facade (frontend.md §3.1) — the only auth integration point for screens. It exposes the
// streamer Device Code Flow login + session reads against the live backend. An interface (like [SystemApi])
// so state holders depend on the abstraction and fake it in tests without HTTP; [RestAuthApi] is the live
// impl over the shared [ApiClient].
//
// Backend routes (AuthController):
//   GET  /api/v1/auth/me                  →  StatusResponseDto<CurrentUserDto>   (requires Authorization: Bearer)
//   POST /api/v1/auth/twitch/device       →  StatusResponseDto<DeviceCodeStart>  (anonymous)
//   POST /api/v1/auth/twitch/device/poll  →  StatusResponseDto<DeviceLoginPoll>  (anonymous)
interface AuthApi {
    /** The signed-in streamer for the active session, proving the captured JWT is valid. */
    suspend fun me(): ApiResult<CurrentUser>

    /**
     * Begin the no-secret streamer login: the backend mints a short user code the operator approves at
     * [DeviceCodeStart.verificationUri]; the client then polls with [DeviceCodeStart.deviceCode].
     */
    suspend fun startDeviceLogin(): ApiResult<DeviceCodeStart>

    /**
     * Poll a streamer device login once. Until the operator approves, [DeviceLoginPoll.status] is
     * `pending` / `slow_down`; on `authorized` the session tokens ride [DeviceLoginPoll.auth].
     */
    suspend fun pollDeviceLogin(deviceCode: String): ApiResult<DeviceLoginPoll>
}

class RestAuthApi(private val client: ApiClient) : AuthApi {

    override suspend fun me(): ApiResult<CurrentUser> = client.getEnvelope("api/v1/auth/me")

    override suspend fun startDeviceLogin(): ApiResult<DeviceCodeStart> =
        client.postEnvelope("api/v1/auth/twitch/device")

    override suspend fun pollDeviceLogin(deviceCode: String): ApiResult<DeviceLoginPoll> =
        client.postEnvelope("api/v1/auth/twitch/device/poll", DevicePollBody(deviceCode))
}

/** A started device authorization — the user code to show + the verification URL + the poll handle/interval. */
@Serializable
data class DeviceCodeStart(
    val deviceCode: String,
    val userCode: String,
    val verificationUri: String,
    val interval: Int = 5,
    val expiresIn: Int = 1800,
)

/** One device-login poll: the loop status, plus the issued auth on `authorized`. */
@Serializable
data class DeviceLoginPoll(val status: String, val auth: AuthPayload? = null)

/** The poll request body — the opaque device code from [DeviceCodeStart]. */
@Serializable
private data class DevicePollBody(val deviceCode: String)
