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

import io.ktor.http.encodeURLPathPart
import kotlinx.serialization.Serializable

// The typed moderation facade. It renders the channel's currently-banned viewers and lets a moderator lift a
// ban — both straight against the real Twitch-backed moderation state on the backend (no fabricated entries).
// State holders depend on the interface (the existing "depend on interfaces" convention) and fake it in
// tests without HTTP.
//
// Backend routes (CommunityController):
//   GET    /api/v1/channels/{channelId}/community/bans            →  PaginatedResponse<BannedUserDto>
//   DELETE /api/v1/channels/{channelId}/community/{userId}/ban    →  204 No Content
// A PaginatedResponse is a flat `{ data: [...] }` object (not the single-value StatusResponseDto envelope),
// so the list is read with getDirect + PaginatedEnvelope rather than getEnvelope. The unban is a bodyless
// DELETE that returns 204, so it goes through deleteUnit; `userId` is the Twitch id carried by BannedUser.id.
interface ModerationApi {
    /** The channel's currently-banned viewers — most recent ban first. */
    suspend fun bans(channelId: String): ApiResult<List<BannedUser>>

    /** Lift the ban on [userId] (the [BannedUser.id]); the backend also clears it on Twitch. */
    suspend fun unban(channelId: String, userId: String): ApiResult<Unit>

    /** The channel's recent moderator action log — newest first (bans / timeouts / unbans / deletes / etc.). */
    suspend fun modLog(channelId: String): ApiResult<List<ModLogEntry>>

    /** Whether emergency Shield Mode is active for the channel. */
    suspend fun shieldMode(channelId: String): ApiResult<ShieldStatus>

    /** Turn Shield Mode on or off ([enabled]). */
    suspend fun setShieldMode(channelId: String, enabled: Boolean): ApiResult<Unit>

    /** The channel's blocked terms — words / phrases auto-removed from chat. */
    suspend fun blockedTerms(channelId: String): ApiResult<List<String>>

    /** Add [term] to the channel's blocked-terms list. */
    suspend fun addBlockedTerm(channelId: String, term: String): ApiResult<Unit>

    /** Remove [term] from the channel's blocked-terms list. */
    suspend fun removeBlockedTerm(channelId: String, term: String): ApiResult<Unit>
}

class RestModerationApi(private val client: ApiClient) : ModerationApi {
    override suspend fun bans(channelId: String): ApiResult<List<BannedUser>> =
        when (
            val page: ApiResult<PaginatedEnvelope<BannedUser>> =
                client.getDirect("api/v1/channels/$channelId/community/bans?page=1&pageSize=25")
        ) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }

    override suspend fun unban(channelId: String, userId: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/channels/$channelId/community/$userId/ban")

    // The mod log is a flat PaginatedResponse on the ModerationController (moderation/log), so read it with
    // getDirect + PaginatedEnvelope like the bans list. First page only here.
    override suspend fun modLog(channelId: String): ApiResult<List<ModLogEntry>> =
        when (
            val page: ApiResult<PaginatedEnvelope<ModLogEntry>> =
                client.getDirect("api/v1/channels/$channelId/moderation/log?page=1&pageSize=25")
        ) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }

    // Shield mode is a single-value StatusResponseDto envelope ({ data: { enabled } }), so getEnvelope reads it.
    override suspend fun shieldMode(channelId: String): ApiResult<ShieldStatus> =
        client.getEnvelope("api/v1/channels/$channelId/moderation/shield")

    override suspend fun setShieldMode(channelId: String, enabled: Boolean): ApiResult<Unit> =
        client.patchUnit(
            "api/v1/channels/$channelId/moderation/shield",
            SetShieldBody(enabled),
        )

    // Blocked terms are a single-value StatusResponseDto envelope ({ data: [ ... ] }) — getEnvelope reads the list.
    override suspend fun blockedTerms(channelId: String): ApiResult<List<String>> =
        client.getEnvelope("api/v1/channels/$channelId/moderation/blocked-terms")

    override suspend fun addBlockedTerm(channelId: String, term: String): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/moderation/blocked-terms", AddTermBody(term))

    // The term rides the URL path, so it must be path-encoded (terms can be multi-word phrases).
    override suspend fun removeBlockedTerm(channelId: String, term: String): ApiResult<Unit> =
        client.deleteUnit(
            "api/v1/channels/$channelId/moderation/blocked-terms/${term.encodeURLPathPart()}"
        )
}

/**
 * One banned viewer (backend `BannedUserDto`). Fields mirror the backend record's camelCase JSON exactly
 * (the contract test guards this): the Twitch id, login/display name, an optional avatar, the ban reason,
 * who issued it, and when.
 */
@Serializable
data class BannedUser(
    val id: String,
    val username: String = "",
    val displayName: String = "",
    val profileImageUrl: String? = null,
    val reason: String = "",
    val bannedBy: String = "",
    val bannedAt: String = "",
)

/**
 * One moderator action-log entry (backend `ModLogEntryDto`). camelCase mirror: the [action] (ban / timeout /
 * delete / ...), the [moderator] who issued it, the [target] viewer, an optional [reason], the [timestamp], and
 * the timeout [duration] in seconds (null for non-timeout actions).
 */
@Serializable
data class ModLogEntry(
    val id: String = "",
    val action: String = "",
    val moderator: String = "",
    val target: String? = null,
    val reason: String? = null,
    val timestamp: String = "",
    val duration: Int? = null,
)

/** The Shield Mode status — the backend's anonymous `{ enabled }` payload (no named backend DTO to guard). */
@Serializable
data class ShieldStatus(val enabled: Boolean = false)

/** The Shield Mode toggle body (backend `ModerationController.SetShieldRequest`). camelCase `enabled`. */
@Serializable
data class SetShieldBody(val enabled: Boolean)

/** The add-blocked-term body (backend `ModerationController.AddTermRequest`). camelCase `term`. */
@Serializable
data class AddTermBody(val term: String)
