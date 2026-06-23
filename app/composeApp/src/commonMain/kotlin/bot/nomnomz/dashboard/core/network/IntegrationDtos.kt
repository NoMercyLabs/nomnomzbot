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

// Hand-authored mirrors of the backend onboarding/integration contracts for this slice. These move into
// the committed OpenAPI-generated layer (core/network/generated) when the generator task lands; the
// facades keep the same surface, so callers don't change.

/**
 * One channel the signed-in user owns/moderates (`GET /api/v1/channels` → PaginatedResponse). The
 * dashboard resolves "my channel" from this list to drive the per-channel integration routes
 * (`/channels/{channelId}/...`), where `Id` is the tenant (channel) GUID, not the Twitch id.
 */
@Serializable
data class ChannelSummary(
    val id: String,
    val login: String = "",
    val displayName: String = "",
    val profileImageUrl: String? = null,
    val isLive: Boolean = false,
    val role: String = "",
)

/**
 * The platform-shared bot account connection status (`GET /api/v1/auth/twitch/bot/status` →
 * StatusResponseDto<BotStatusDto>). The bot's Twitch token is held server-side; the client only ever
 * sees this status.
 */
@Serializable
data class BotStatus(
    val connected: Boolean,
    val login: String? = null,
    val displayName: String? = null,
    val profileImageUrl: String? = null,
)

/**
 * The authorize URL + signed single-use state the client opens to start a connect
 * (`OAuthStartDto` — used by both the bot start and the generic integration connect).
 */
@Serializable
data class OAuthStart(
    val authorizeUrl: String,
    val state: String,
)

/** The connect request body for the generic integration flow (`ConnectIntegrationRequest`). */
@Serializable
data class ConnectIntegrationBody(
    val scopeSetKey: String,
    val returnUrl: String? = null,
)

/**
 * Per-provider integration status for the integrations screen
 * (`GET /channels/{channelId}/integrations/status` → StatusResponseDto<List<IntegrationStatusDto>>).
 * No secrets — purely the screen's read model.
 */
@Serializable
data class IntegrationStatus(
    val provider: String,
    val connected: Boolean,
    val accountName: String? = null,
    val grantedScopeSets: List<String> = emptyList(),
    val needsReauth: Boolean = false,
)
