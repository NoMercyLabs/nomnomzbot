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

// The admin-plane surface for the shared platform bot (S-BOT-PLATFORM-UI, GET/POST
// /api/v1/admin/platform-bot/*). Gated server-side on `platform:bot:manage` — its own key, not implied by
// any broader admin key. Distinct from [BotAuthApi]: that one is the SETUP-WIZARD bootstrap path (open
// until first-run finishes); this one is the ONLY route left to re-connect or replace the bot once setup
// is done, and it requires a justification plus a re-verified counted blast radius before a swap applies.

import kotlinx.serialization.Serializable

/** The real, three-way platform-bot state — never collapse "never connected" and "token unreadable"
 * (an ENCRYPTION_KEY rotation) into the same thing; the operator needs to tell them apart. */
@Serializable
data class PlatformBotAdminStatus(
    val state: String,
    val botUsername: String? = null,
    val botDisplayName: String? = null,
)

object PlatformBotAdminState {
    const val NEVER_CONNECTED: String = "NeverConnected"
    const val CONNECTED_AND_WORKING: String = "ConnectedAndWorking"
    const val CONNECTED_TOKEN_UNUSABLE: String = "ConnectedTokenUnusable"
}

/** The counted blast radius a reconnect would touch — every channel with no bot of its own. */
@Serializable
data class PlatformBotReconnectPreview(val affectedChannelCount: Int)

interface PlatformBotAdminApi {
    /** The real current state, read from the stored credential. */
    suspend fun status(): ApiResult<PlatformBotAdminStatus>

    /** The counted blast radius a reconnect would touch — must be rendered before one is armed. */
    suspend fun previewReconnect(justification: String): ApiResult<PlatformBotReconnectPreview>

    /** Begins the reconnect device login. The backend rejects a stale [confirmedAffectedChannelCount]. */
    suspend fun startReconnect(
        justification: String,
        confirmedAffectedChannelCount: Int,
    ): ApiResult<DeviceCodeStart>

    /** Polls the reconnect device login once; on `authorized` the shared bot credential is REPLACED. */
    suspend fun pollReconnect(
        deviceCode: String,
        justification: String,
        confirmedAffectedChannelCount: Int,
    ): ApiResult<DeviceBotPoll>
}

@Serializable
private data class ReconnectRequestBody(
    val justification: String,
    val confirmedAffectedChannelCount: Int,
)

@Serializable
private data class ReconnectPollRequestBody(
    val deviceCode: String,
    val justification: String,
    val confirmedAffectedChannelCount: Int,
)

class RestPlatformBotAdminApi(private val client: ApiClient) : PlatformBotAdminApi {
    override suspend fun status(): ApiResult<PlatformBotAdminStatus> =
        client.getEnvelope("api/v1/admin/platform-bot")

    override suspend fun previewReconnect(justification: String): ApiResult<PlatformBotReconnectPreview> =
        client.getEnvelope(
            "api/v1/admin/platform-bot/reconnect/preview?justification=${justification.encodeQuery()}",
        )

    override suspend fun startReconnect(
        justification: String,
        confirmedAffectedChannelCount: Int,
    ): ApiResult<DeviceCodeStart> =
        client.postEnvelope(
            "api/v1/admin/platform-bot/reconnect/device",
            ReconnectRequestBody(justification, confirmedAffectedChannelCount),
        )

    override suspend fun pollReconnect(
        deviceCode: String,
        justification: String,
        confirmedAffectedChannelCount: Int,
    ): ApiResult<DeviceBotPoll> =
        client.postEnvelope(
            "api/v1/admin/platform-bot/reconnect/device/poll",
            ReconnectPollRequestBody(deviceCode, justification, confirmedAffectedChannelCount),
        )
}
