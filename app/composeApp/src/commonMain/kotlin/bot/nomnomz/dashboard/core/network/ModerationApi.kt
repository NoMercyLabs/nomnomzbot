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

    /** The channel's AutoMod filter configuration (link / caps / banned-phrases / emote-spam). */
    suspend fun automod(channelId: String): ApiResult<AutomodConfig>

    /** Persist the whole AutoMod [config] (the backend POST takes the full config; a toggle re-sends it). */
    suspend fun saveAutomod(channelId: String, config: AutomodConfig): ApiResult<Unit>

    /** The channel's moderation filter rules (newest first). */
    suspend fun rules(channelId: String): ApiResult<List<ModerationRule>>

    /** Create a new filter rule. Returns the full row (including the assigned [ModerationRule.id]). */
    suspend fun createRule(channelId: String, body: CreateModerationRuleBody): ApiResult<ModerationRule>

    /** Enable or disable a filter rule ([enabled]) — a partial PUT carrying only the flag. */
    suspend fun setRuleEnabled(channelId: String, ruleId: Int, enabled: Boolean): ApiResult<Unit>

    /** Delete a filter rule. */
    suspend fun deleteRule(channelId: String, ruleId: Int): ApiResult<Unit>

    /**
     * Perform a moderation action (ban / timeout / unban) on [targetUserId]. [action] is one of `"ban"`,
     * `"timeout"`, or `"unban"`; [durationSeconds] is only required for `"timeout"` (ignored otherwise);
     * [reason] is optional.
     */
    suspend fun performAction(
        channelId: String,
        action: String,
        targetUserId: String,
        durationSeconds: Int? = null,
        reason: String? = null,
    ): ApiResult<Unit>

    /** Today's moderation counters for the stats banner. */
    suspend fun stats(channelId: String): ApiResult<ModerationStats>

    /**
     * The bot's OWN recorded moderation history for [userId] (a Twitch id): ban / timeout / warn / unban counts,
     * the last action, and the most recent actions. NOTE: only actions this bot recorded (dashboard / command /
     * EventSub) — not the viewer's complete Twitch record; the panel labels it as such.
     */
    suspend fun userContext(channelId: String, userId: String): ApiResult<UserModerationContext>

    /**
     * Send a chat announcement to [channelId]. [color] is one of `"blue"`, `"green"`, `"orange"`, `"purple"`,
     * or `"primary"` (= the channel's accent) — defaults to `"primary"` when omitted. Requires
     * `moderator:manage:announcements`.
     */
    suspend fun announce(channelId: String, message: String, color: String?): ApiResult<Unit>

    /**
     * Issue a Twitch warning to [userId] with [reason] (they must acknowledge it before chatting again).
     * Returns the action result — [ModerationActionResult.success] is false with a [ModerationActionResult.message]
     * when the channel's grant can't warn (honest degradation, still HTTP 200). Requires
     * `moderator:manage:warnings`.
     */
    suspend fun warn(channelId: String, userId: String, reason: String): ApiResult<ModerationActionResult>

    /**
     * Flag [userId] as suspicious — [status] is `active_monitoring` (watch) or `restricted` (hold all their
     * messages). Requires `moderator:manage:suspicious_users`.
     */
    suspend fun setSuspicious(channelId: String, userId: String, status: String): ApiResult<Unit>

    /** Clear the suspicious flag on [userId]. */
    suspend fun clearSuspicious(channelId: String, userId: String): ApiResult<Unit>

    /** The channel's pending unban-request appeals (viewers appealing a ban on Twitch). Live Twitch read. */
    suspend fun unbanRequests(channelId: String): ApiResult<List<UnbanRequest>>

    /**
     * Resolve an unban request [requestId]: [approve] lifts the ban, else it is denied. [note] is an optional
     * resolution message. Live Twitch write.
     */
    suspend fun resolveUnbanRequest(
        channelId: String,
        requestId: String,
        approve: Boolean,
        note: String?,
    ): ApiResult<Unit>

    /**
     * Lift a ban on [targetTwitchUserId] — in THIS channel ([scope] = "this_channel") or across EVERY channel the
     * operator moderates ([scope] = "all_moderated"; the reverse of the network ban, issued as the operator's own
     * token, best-effort). Returns the per-channel outcome (one row for "this_channel").
     */
    suspend fun networkUnban(
        channelId: String,
        targetTwitchUserId: String,
        scope: String,
    ): ApiResult<NetworkBanResult>
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

    // AutoMod is a single-value StatusResponseDto envelope ({ data: { … } }) — getEnvelope reads the config.
    override suspend fun automod(channelId: String): ApiResult<AutomodConfig> =
        client.getEnvelope("api/v1/channels/$channelId/moderation/automod")

    // The POST body IS the full AutomodConfigDto; the controller reloads after, so any 2xx is success.
    override suspend fun saveAutomod(channelId: String, config: AutomodConfig): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/moderation/automod", config)

    override suspend fun rules(channelId: String): ApiResult<List<ModerationRule>> =
        when (
            val page: ApiResult<PaginatedEnvelope<ModerationRule>> =
                client.getDirect("api/v1/channels/$channelId/moderation/rules?page=1&pageSize=25")
        ) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }

    // POST creates a new rule; the backend returns a StatusResponseDto<ModerationRuleDetail> with the new row.
    override suspend fun createRule(
        channelId: String,
        body: CreateModerationRuleBody,
    ): ApiResult<ModerationRule> = client.postEnvelope("api/v1/channels/$channelId/moderation/rules", body)

    // Partial PUT — only the flag; the rule's name / action / settings stay untouched on the backend.
    override suspend fun setRuleEnabled(
        channelId: String,
        ruleId: Int,
        enabled: Boolean,
    ): ApiResult<Unit> =
        client.putUnit(
            "api/v1/channels/$channelId/moderation/rules/$ruleId",
            UpdateRuleBody(isEnabled = enabled),
        )

    override suspend fun deleteRule(channelId: String, ruleId: Int): ApiResult<Unit> =
        client.deleteUnit("api/v1/channels/$channelId/moderation/rules/$ruleId")

    override suspend fun stats(channelId: String): ApiResult<ModerationStats> =
        client.getEnvelope("api/v1/channels/$channelId/moderation/stats")

    // Single-value StatusResponseDto envelope ({ data: { … } }) — getEnvelope reads the context object.
    override suspend fun userContext(channelId: String, userId: String): ApiResult<UserModerationContext> =
        client.getEnvelope("api/v1/channels/$channelId/moderation/users/$userId/context")

    // The POST body is a ChatController.AnnounceRequest (message, color?); the backend calls Helix on the tenant's behalf.
    override suspend fun announce(channelId: String, message: String, color: String?): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/chat/announce", AnnounceBody(message, color))

    // The POST body is a PerformModerationActionRequest (action, targetUserId, durationSeconds?, reason?).
    // The backend is under ModerationController at /moderation/actions.
    override suspend fun performAction(
        channelId: String,
        action: String,
        targetUserId: String,
        durationSeconds: Int?,
        reason: String?,
    ): ApiResult<Unit> =
        client.postUnit(
            "api/v1/channels/$channelId/moderation/actions",
            ModerationActionBody(action, targetUserId, durationSeconds, reason),
        )

    // POST /moderation/warn — StatusResponseDto<ModerationActionResult>; postEnvelope reads `.data` so the
    // caller can surface a success=false / message (missing-scope degradation) rather than assume it worked.
    override suspend fun warn(
        channelId: String,
        userId: String,
        reason: String,
    ): ApiResult<ModerationActionResult> =
        client.postEnvelope(
            "api/v1/channels/$channelId/moderation/warn",
            WarnBody(targetUserId = userId, reason = reason),
        )

    // POST /moderation/suspicious — the caller reloads the user context after, so any 2xx is success here.
    override suspend fun setSuspicious(
        channelId: String,
        userId: String,
        status: String,
    ): ApiResult<Unit> =
        client.postUnit(
            "api/v1/channels/$channelId/moderation/suspicious",
            SetSuspiciousBody(targetUserId = userId, status = status),
        )

    override suspend fun clearSuspicious(channelId: String, userId: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/channels/$channelId/moderation/suspicious/$userId")

    // Single-value StatusResponseDto envelope ({ data: [ ... ] }) — getEnvelope reads the appeals list.
    override suspend fun unbanRequests(channelId: String): ApiResult<List<UnbanRequest>> =
        client.getEnvelope(
            "api/v1/channels/$channelId/moderation/unban-requests?status=pending"
        )

    override suspend fun resolveUnbanRequest(
        channelId: String,
        requestId: String,
        approve: Boolean,
        note: String?,
    ): ApiResult<Unit> =
        client.postUnit(
            "api/v1/channels/$channelId/moderation/unban-requests/$requestId/resolve",
            ResolveUnbanBody(approve = approve, note = note),
        )

    // Mirror of the network ban: POST /moderation/actions/unban -> StatusResponseDto<NetworkBanResultDto>.
    override suspend fun networkUnban(
        channelId: String,
        targetTwitchUserId: String,
        scope: String,
    ): ApiResult<NetworkBanResult> =
        client.postEnvelope(
            "api/v1/channels/$channelId/moderation/actions/unban",
            UnbanUserBody(targetTwitchUserId = targetTwitchUserId, scope = scope),
        )
}

/** Today's moderation counters (backend `GET /moderation/stats` anonymous object). */
@Serializable
data class ModerationStats(
    val bansToday: Int = 0,
    val timeouts: Int = 0,
    val deletedMessages: Int = 0,
    val automodActions: Int = 0,
)

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

/**
 * A viewer's per-user moderation rap sheet (backend `UserModerationContextDto`). The counts + recent actions are
 * the bot's OWN recorded history (dashboard / command / EventSub), NOT the viewer's complete Twitch record — the
 * panel labels it as such. camelCase mirror (the contract test guards this).
 */
@Serializable
data class UserModerationContext(
    val userId: String = "",
    val username: String? = null,
    val banCount: Int = 0,
    val timeoutCount: Int = 0,
    val warnCount: Int = 0,
    val unbanCount: Int = 0,
    val lastActionType: String? = null,
    val lastActionAt: String? = null,
    val recentActions: List<ModerationActionLog> = emptyList(),
)

/**
 * One recorded moderation action in a viewer's [UserModerationContext.recentActions] (backend
 * `ModerationActionLog`). camelCase mirror: the [action], who issued it ([moderatorUsername]), the target, an
 * optional [reason], the timeout [durationSeconds] (null for non-timeouts), and the [timestamp].
 */
@Serializable
data class ModerationActionLog(
    val id: String = "",
    val action: String = "",
    val moderatorId: String = "",
    val moderatorUsername: String = "",
    val targetUserId: String? = null,
    val targetUsername: String? = null,
    val reason: String? = null,
    val durationSeconds: Int? = null,
    val timestamp: String = "",
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

/**
 * The channel's AutoMod filter configuration (backend `AutomodConfigDto`) — four independent filters. camelCase
 * mirror; surfaced read-only for now (the per-filter toggle / edit is a follow-up).
 */
@Serializable
data class AutomodConfig(
    val linkFilter: AutomodLinkFilter = AutomodLinkFilter(),
    val capsFilter: AutomodCapsFilter = AutomodCapsFilter(),
    val bannedPhrases: AutomodBannedPhrases = AutomodBannedPhrases(),
    val emoteSpam: AutomodEmoteSpam = AutomodEmoteSpam(),
)

/** AutoMod link filter (backend `AutomodLinkFilterDto`) — blocks links except the [whitelist]. */
@Serializable
data class AutomodLinkFilter(val enabled: Boolean = false, val whitelist: List<String> = emptyList())

/** AutoMod caps filter (backend `AutomodCapsFilterDto`) — flags messages over [threshold]% caps. */
@Serializable
data class AutomodCapsFilter(val enabled: Boolean = false, val threshold: Int = 0)

/** AutoMod banned-phrases filter (backend `AutomodBannedPhrasesDto`) — blocks the listed [phrases]. */
@Serializable
data class AutomodBannedPhrases(val enabled: Boolean = false, val phrases: List<String> = emptyList())

/** AutoMod emote-spam filter (backend `AutomodEmoteSpamDto`) — flags messages over [maxEmotes] emotes. */
@Serializable
data class AutomodEmoteSpam(val enabled: Boolean = false, val maxEmotes: Int = 0)

/**
 * A moderation filter rule (backend `ModerationRuleDetail` — the list-row projection). camelCase mirror; the page
 * reads the subset the rules list shows ([name], [type], [action], [isEnabled], optional [durationSeconds] /
 * [reason]) — the polymorphic `settings` dict + `exemptRoles` are edited in the (follow-up) rule editor.
 */
@Serializable
data class ModerationRule(
    val id: Int = 0,
    val name: String = "",
    val type: String = "",
    val isEnabled: Boolean = false,
    val action: String = "",
    val durationSeconds: Int? = null,
    val reason: String? = null,
)

/** A partial moderation-rule update (backend `UpdateModerationRuleRequest`) — a toggle sends just [isEnabled]. */
@Serializable
data class UpdateRuleBody(val isEnabled: Boolean? = null)

/**
 * Create-rule body (backend `CreateModerationRuleRequest`). [type] is one of: `"profanity"`, `"links"`,
 * `"caps"`, `"emotes"`, `"spam"`. [action] is one of: `"delete"`, `"timeout"`, `"ban"`. [durationSeconds]
 * is only used when [action] is `"timeout"`.
 */
@Serializable
data class CreateModerationRuleBody(
    val name: String,
    val type: String,
    val action: String,
    val durationSeconds: Int? = null,
    val reason: String? = null,
)

// ModerationActionBody lives in ChatApi.kt (package-shared) — imported from there.

/** Request body for the announce action (backend `ChatController.AnnounceRequest`). */
@Serializable
data class AnnounceBody(val message: String, val color: String? = null)

/** Request body for a warning (backend `WarnUserRequest`). [targetUserId] is the viewer's Twitch id. */
@Serializable
data class WarnBody(val targetUserId: String, val reason: String)

/**
 * Request body to flag a viewer (backend `SetSuspiciousStatusRequest`). [status] is `active_monitoring` or
 * `restricted`.
 */
@Serializable
data class SetSuspiciousBody(val targetUserId: String, val status: String)

/**
 * The outcome of a moderation action (backend `ModerationActionResult`): [success], plus a [message] the UI
 * surfaces when it is false (e.g. the channel's grant can't perform the action — honest degradation).
 */
@Serializable
data class ModerationActionResult(val success: Boolean = false, val message: String? = null)

/**
 * A pending unban-request appeal (backend `UnbanRequestDto`). The viewer ([userLogin]/[userName]) appealed a
 * ban; [text] is their message. A subset of the DTO — the fields the queue renders.
 */
@Serializable
data class UnbanRequest(
    val id: String = "",
    val userId: String = "",
    val userLogin: String = "",
    val userName: String = "",
    val text: String = "",
    val status: String = "",
    val createdAt: String = "",
)

/** Request body to resolve an unban request (backend `ResolveUnbanRequestRequest`). */
@Serializable
data class ResolveUnbanBody(val approve: Boolean, val note: String? = null)

/**
 * Request body for a network un-nuke (backend `UnbanUserRequest`). [scope] is `this_channel` or
 * `all_moderated`. Mirrors `BanUserBody` (which lives in ChatApi.kt for the ban side).
 */
@Serializable
data class UnbanUserBody(val targetTwitchUserId: String, val scope: String)
