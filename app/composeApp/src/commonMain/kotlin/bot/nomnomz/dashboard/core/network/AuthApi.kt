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
//   GET  /api/v1/auth/providers            →  StatusResponseDto<List<LoginProviderDto>>  (anonymous)
//   GET  /api/v1/auth/me                    →  StatusResponseDto<CurrentUserDto>   (requires Authorization: Bearer)
//   POST /api/v1/auth/{provider}/device      →  StatusResponseDto<DeviceCodeStart>  (anonymous)
//   POST /api/v1/auth/{provider}/device/poll →  StatusResponseDto<DeviceLoginPoll>  (anonymous)
//   POST /api/v1/auth/refresh               →  StatusResponseDto<AuthPayload>      (anonymous; renew a stale access token)
interface AuthApi {
    /**
     * The login providers this backend offers (platform-identity §4). The login screen renders one button
     * per ENABLED provider, so it never shows a dead button. Anonymous — read before any session exists.
     */
    suspend fun providers(): ApiResult<List<LoginProvider>>

    /** The signed-in streamer for the active session, proving the captured JWT is valid. */
    suspend fun me(): ApiResult<CurrentUser>

    /**
     * Begin the no-secret login for [provider] (defaults to `twitch`): the backend mints a short user code
     * the operator approves at [DeviceCodeStart.verificationUri]; the client then polls with
     * [DeviceCodeStart.deviceCode].
     */
    suspend fun startDeviceLogin(provider: String = "twitch"): ApiResult<DeviceCodeStart>

    /**
     * Poll a device login once for [provider] (defaults to `twitch`). Until the operator approves,
     * [DeviceLoginPoll.status] is `pending` / `slow_down`; on `authorized` the session tokens ride
     * [DeviceLoginPoll.auth].
     */
    suspend fun pollDeviceLogin(provider: String = "twitch", deviceCode: String): ApiResult<DeviceLoginPoll>

    /**
     * Renew the session — the "remembered" restore path on boot when the persisted access token has expired.
     * Native passes the refresh token it holds; web passes null and the backend reads its HttpOnly cookie
     * instead. The backend rotates the refresh token (returned in the body for native, kept in the cookie for
     * web), so a native caller persists whatever [AuthPayload.refreshToken] comes back.
     */
    suspend fun refresh(refreshToken: String?): ApiResult<AuthPayload>

    /**
     * End the session server-side: revoke this session's refresh token AND — on web — delete the HttpOnly
     * refresh cookie. Without this call a "logout" only clears local state, leaving the cookie in place, so a
     * reload silently re-authenticates via [refresh] and lands back on the dashboard. Must be called while the
     * access token is still armed (before local custody is dropped), so the request carries the bearer the
     * `[Authorize]`d endpoint requires. Best-effort: the caller drops local custody regardless of the result.
     */
    suspend fun logout(): ApiResult<Unit>
}

class RestAuthApi(private val client: ApiClient) : AuthApi {

    override suspend fun providers(): ApiResult<List<LoginProvider>> =
        client.getEnvelope("api/v1/auth/providers")

    override suspend fun me(): ApiResult<CurrentUser> = client.getEnvelope("api/v1/auth/me")

    override suspend fun startDeviceLogin(provider: String): ApiResult<DeviceCodeStart> =
        client.postEnvelope("api/v1/auth/$provider/device")

    override suspend fun pollDeviceLogin(
        provider: String,
        deviceCode: String,
    ): ApiResult<DeviceLoginPoll> =
        client.postEnvelope(
            "api/v1/auth/$provider/device/poll${clientQuery()}",
            DevicePollBody(deviceCode),
        )

    override suspend fun refresh(refreshToken: String?): ApiResult<AuthPayload> =
        client.postEnvelope("api/v1/auth/refresh${clientQuery()}", RefreshBody(refreshToken))

    // The cookie is scoped to /api/v1/auth, so the browser attaches it to this same-origin POST and honours
    // the response's Set-Cookie deletion; postUnit treats any 2xx as success and ignores the status envelope.
    override suspend fun logout(): ApiResult<Unit> = client.postUnit("api/v1/auth/logout")
}

// Advertise the client class to the cookie-sensitive auth endpoints (device poll, refresh) so the backend
// applies browser HttpOnly-cookie custody on web and token-in-body custody on native. Empty on native.
private fun clientQuery(): String = authClientClass()?.let { "?client=$it" } ?: ""

/**
 * A login provider the client may offer on the login screen (platform-identity §4) — a wire descriptor from
 * `GET /api/v1/auth/providers`. [enabled] is the descriptor being registered AND its feature flag resolving
 * true for THIS deployment, so the login screen renders only enabled providers (never a dead button).
 * [flows] are the handshake tokens supported for it (`device_code` | `auth_code` | `auth_code_pkce`), which
 * pick the login path (device-code poll vs. full-page authorize redirect).
 */
@Serializable
data class LoginProvider(
    val key: String,
    val displayName: String,
    val flows: List<String> = emptyList(),
    val enabled: Boolean = false,
)

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

/**
 * The refresh request body — the stored refresh token to exchange. Null on web (the token rides the HttpOnly
 * cookie, not the body); the client's JSON omits nulls, so web sends `{}` and the backend reads the cookie.
 */
@Serializable
private data class RefreshBody(val refreshToken: String?)
