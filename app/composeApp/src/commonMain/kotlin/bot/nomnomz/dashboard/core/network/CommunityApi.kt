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

// The typed community facade — the channel's real viewers, sourced from the Twitch API + chat history by the
// backend (no fabricated viewer lists). It lists the members and lets a moderator manage each one: set their
// trust level, ban them, or lift a ban. It also serves the Community Profile page's single-person read (owner
// punch list 2026-09-08 §3) — every per-channel-per-user data point the domain model tracks. State holders
// depend on this interface and fake it in tests without HTTP.
//
// Backend routes (CommunityController / RewardsController):
//   GET    /api/v1/channels/{channelId}/community                    →  PaginatedResponse<CommunityUserDto>
//   GET    /api/v1/channels/{channelId}/community/{userId}/profile    →  StatusResponseDto<ViewerProfileSummaryDto>
//   GET    /api/v1/channels/{channelId}/rewards/leaderboard          →  StatusResponseDto<List<LeaderboardEntryDto>>
//   PUT    /api/v1/channels/{channelId}/community/{userId}/trust      →  StatusResponseDto<UserDetailDto>
//   POST   /api/v1/channels/{channelId}/community/{userId}/ban        →  204 No Content
//   DELETE /api/v1/channels/{channelId}/community/{userId}/ban        →  204 No Content
// The list is a `PaginatedResponse<CommunityUserDto>` (a flat `{ data: [...] }`), so it is read with getDirect
// like the channel list. The writes treat any 2xx as success (the trust PUT returns the refreshed user, which
// the controller re-derives by reloading the list — so it is read through putUnit), and `userId` is the Twitch
// id carried by CommunityMember.id EXCEPT for `profile`, which is keyed on the internal `User.Id` Guid (the
// Directory row's `CommunityMember.internalUserId`) — a Profile is only reachable for a viewer who already has
// a local User row.
interface CommunityApi {
    /** The channel's community — the first page of viewers (chatters + mods) the backend resolves. */
    suspend fun members(channelId: String): ApiResult<List<CommunityMember>>

    /**
     * One page of the channel's community, filtered by [role] (`null`/"all", "follower", "vip", "moderator") and
     * paginated (`GET /community?page=&take=&role=&cursor=`). The followers tab is cursor-paginated straight from
     * Twitch — pass the previous page's [CommunityPage.nextCursor] as [cursor] — while the other tabs are
     * page-numbered ([page]). The envelope carries both continuations so the screen can drive next/prev on any tab.
     */
    suspend fun membersPage(
        channelId: String,
        role: String?,
        page: Int,
        pageSize: Int,
        cursor: String?,
    ): ApiResult<CommunityPage>

    /**
     * Autocomplete over the channel's known viewers by name (`GET /community/search?q=&limit=`, backend
     * `SearchViewers`). Each option's [ViewerOption.id] is the Twitch user id the moderation / trust / VIP / ban
     * writes consume — the id the "pick a viewer" picker feeds those actions. Powers reaching a viewer beyond the
     * current page.
     */
    suspend fun searchViewers(
        channelId: String,
        query: String,
        limit: Int = 20,
    ): ApiResult<List<ViewerOption>>

    /**
     * A single viewer's REAL member state (`GET /community/{userId}`, backend `UserDetailDto`) — the trust level
     * and ban status a moderator acts on. The searched-viewer row fetches this so it shows the truth (Unban for an
     * already-banned viewer, Revoke-VIP for an existing VIP) instead of a synthesized "not banned / not VIP"
     * default. [userId] is the Twitch id from the picker; the detail payload is a field superset of
     * [CommunityMember] (its extra recent-activity / ban-history fields are ignored).
     */
    suspend fun member(channelId: String, userId: String): ApiResult<CommunityMember>

    /**
     * Top chatters by message volume — up to 50 rows ranked by message count (backend `RewardsController
     * .GetLeaderboard`, `GET /rewards/leaderboard`). The endpoint lives in `RewardsController` for historical
     * reasons but is community analytics: it surfaces who is most active in chat.
     */
    suspend fun topChatters(channelId: String): ApiResult<List<ChatActivityEntry>>

    /** Set [userId]'s trust [level] (one of [CommunityTrustLevel]). Non-destructive; takes effect directly. */
    suspend fun setTrust(channelId: String, userId: String, level: String): ApiResult<Unit>

    /** Ban [userId] with [reason]; the backend also enforces it on Twitch. */
    suspend fun ban(channelId: String, userId: String, reason: String): ApiResult<Unit>

    /** Lift the ban on [userId]; the backend also clears it on Twitch. */
    suspend fun unban(channelId: String, userId: String): ApiResult<Unit>

    /** Grant VIP status to [userId] on Twitch. Requires the channel's `channel:manage:vips` scope. */
    suspend fun addVip(channelId: String, userId: String): ApiResult<Unit>

    /** Revoke VIP status from [userId] on Twitch. Requires the channel's `channel:manage:vips` scope. */
    suspend fun removeVip(channelId: String, userId: String): ApiResult<Unit>

    /** Send a /shoutout to [targetTwitchUserId] in the channel. Requires `moderator:manage:shoutouts`. */
    suspend fun shoutout(channelId: String, targetTwitchUserId: String): ApiResult<Unit>

    /** Channel community counts (followers / subscribers / VIPs / moderators). */
    suspend fun stats(channelId: String): ApiResult<CommunityStats>

    /**
     * The Community Profile page's single-person view (`GET /community/{userId}/profile`, owner punch list
     * 2026-09-08 §3) — every per-channel-per-user data point the domain model tracks. [userId] is the internal
     * `User.Id` Guid ([CommunityMember.internalUserId] / [ViewerIdentity.userId]), not the Twitch id.
     */
    suspend fun profile(channelId: String, userId: String): ApiResult<ViewerProfileSummary>
}

class RestCommunityApi(private val client: ApiClient) : CommunityApi {

    override suspend fun members(channelId: String): ApiResult<List<CommunityMember>> {
        // Walk every page so the whole community list shows — flat `{ data, hasMore, nextPage }`.
        return client.getAllPages { page -> "api/v1/channels/$channelId/community?page=$page&pageSize=100" }
    }

    override suspend fun membersPage(
        channelId: String,
        role: String?,
        page: Int,
        pageSize: Int,
        cursor: String?,
    ): ApiResult<CommunityPage> {
        val roleParam: String =
            if (role.isNullOrBlank() || role == "all") "" else "&role=${role.encodeQuery()}"
        val cursorParam: String = if (cursor.isNullOrBlank()) "" else "&cursor=${cursor.encodeQuery()}"
        return client.getDirect(
            "api/v1/channels/$channelId/community?page=$page&take=$pageSize$roleParam$cursorParam"
        )
    }

    override suspend fun searchViewers(
        channelId: String,
        query: String,
        limit: Int,
    ): ApiResult<List<ViewerOption>> =
        client.getEnvelope(
            "api/v1/channels/$channelId/community/search?q=${query.encodeQuery()}&limit=$limit"
        )

    override suspend fun member(channelId: String, userId: String): ApiResult<CommunityMember> =
        // Backend routing prefers the literal /community/search over the {userId} template, so a numeric Twitch
        // id resolves to GetUserDetail (UserDetailDto), which deserializes straight into CommunityMember.
        client.getEnvelope("api/v1/channels/$channelId/community/${userId.encodeQuery()}")

    override suspend fun topChatters(channelId: String): ApiResult<List<ChatActivityEntry>> =
        client.getEnvelope("api/v1/channels/$channelId/rewards/leaderboard")

    override suspend fun setTrust(channelId: String, userId: String, level: String): ApiResult<Unit> =
        // The endpoint returns the refreshed UserDetailDto, but the page re-derives its state by reloading the
        // list — so the body is ignored and the write goes through putUnit (any 2xx is success).
        client.putUnit(
            "api/v1/channels/$channelId/community/$userId/trust",
            SetTrustLevelBody(level),
        )

    override suspend fun ban(channelId: String, userId: String, reason: String): ApiResult<Unit> =
        client.postUnit(
            "api/v1/channels/$channelId/community/$userId/ban",
            BanBody(reason),
        )

    override suspend fun unban(channelId: String, userId: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/channels/$channelId/community/$userId/ban")

    override suspend fun addVip(channelId: String, userId: String): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/community/$userId/vip")

    override suspend fun removeVip(channelId: String, userId: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/channels/$channelId/community/$userId/vip")

    override suspend fun shoutout(channelId: String, targetTwitchUserId: String): ApiResult<Unit> =
        client.postUnit(
            "api/v1/channels/$channelId/moderation/shoutout",
            ShoutoutBody(targetTwitchUserId),
        )

    override suspend fun stats(channelId: String): ApiResult<CommunityStats> =
        client.getEnvelope("api/v1/channels/$channelId/community/stats")

    override suspend fun profile(channelId: String, userId: String): ApiResult<ViewerProfileSummary> =
        client.getEnvelope("api/v1/channels/$channelId/community/${userId.encodeQuery()}/profile")
}

/** Channel community counts (backend `CommunityStatsDto`) — the followers / subscribers / VIPs / moderators panel. */
@Serializable
data class CommunityStats(
    val followers: Int = 0,
    val subscribers: Int = 0,
    val vips: Int = 0,
    val moderators: Int = 0,
)

/** The trust levels the backend `SetTrustLevelRequest` accepts — the closed set the row's picker offers. */
object CommunityTrustLevel {
    const val Viewer: String = "viewer"
    const val Subscriber: String = "subscriber"
    const val Vip: String = "vip"
    const val Moderator: String = "moderator"

    /** Ordered for the picker — least to most privileged. */
    val all: List<String> = listOf(Viewer, Subscriber, Vip, Moderator)
}

/** Request body for the trust write (backend `SetTrustLevelRequest`): the new trust level. */
@Serializable
data class SetTrustLevelBody(val level: String)

/** Request body for the ban write (backend `BanRequest`): the moderator-supplied reason. */
@Serializable
data class BanBody(val reason: String)

/** Request body for the shoutout action (backend `ModerationController.ShoutoutRequest`). */
@Serializable
data class ShoutoutBody(val targetTwitchUserId: String)

/**
 * A community member (backend `CommunityUserDto`): the viewer's identity plus the standing badges the row
 * shows. The field names are the serialized (camelCase) names of `CommunityUserDto`; the client deliberately
 * reads it (ApiClient's Json ignores unknown keys), including the per-viewer activity stats surfaced in rows.
 */
@Serializable
data class CommunityMember(
    val id: String,
    /**
     * The viewer's internal platform-identity ULID (backend `internalUserId`), nullable when the backend has no
     * resolved user row yet. This — not the Twitch [id] — is what addresses the channel-scoped analytics profile
     * (`analytics/viewers/{internalUserId}`), so a moderator can read ANY viewer's stats, not only their own.
     */
    val internalUserId: String? = null,
    val username: String = "",
    val displayName: String = "",
    val profileImageUrl: String? = null,
    val trustLevel: String = "viewer",
    val isBanned: Boolean = false,
    /** Per-viewer channel-scoped activity (backend CommunityUserDto): messages sent, watch hours, commands run. */
    val messageCount: Int = 0,
    val watchHours: Double = 0.0,
    val commandsUsed: Int = 0,
    /** ISO-8601 first-/last-seen timestamps (backend DateTime). */
    val firstSeen: String = "",
    val lastSeen: String = "",
)

/**
 * One page of the community list (backend `PaginatedResponse<CommunityUserDto>`). [data] is the page's rows;
 * [nextPage] is the next 1-based page number for the page-numbered tabs (null at the end), [nextCursor] is the
 * Twitch continuation token for the cursor-paginated followers tab (null at the end), and [hasMore] tells the
 * screen whether a "next" affordance should be live. [total] is the full count where the backend knows it.
 */
@Serializable
data class CommunityPage(
    val data: List<CommunityMember> = emptyList(),
    val nextPage: Int? = null,
    val nextCursor: String? = null,
    val hasMore: Boolean = false,
    val total: Int? = null,
)

/**
 * One viewer option for the community picker (backend `CommunityController.ViewerOptionDto`). [id] is the Twitch
 * user id the moderation / trust / VIP / ban writes consume; [label] is the display name and [subLabel] the
 * username.
 */
@Serializable
data class ViewerOption(
    val id: String = "",
    val label: String = "",
    val subLabel: String = "",
)

/**
 * A chat-activity leaderboard row (backend `LeaderboardEntryDto` from `RewardsController.GetLeaderboard`):
 * [rank] (1-based), [userId] (Twitch user id), [displayName], and [points] which is the viewer's all-time
 * message count (the field is named "points" on the wire for historical reasons).
 */
@Serializable
data class ChatActivityEntry(
    val rank: Int = 0,
    val userId: String = "",
    val displayName: String = "",
    val points: Int = 0,
)

// ── Community Profile page (owner punch list 2026-09-08 §3) ─────────────────────────────────────────────────
//
// Everything the domain model tracks about one person within one channel, mirrored from the backend
// `ViewerProfileSummaryDto` and its groups. A null/empty group means the person genuinely has no data there
// yet — never a fetch that silently failed. Several groups reuse existing typed DTOs rather than re-declare
// the same shape a second time: [UserModerationHistorySummary] / [UserTrustSummary] (ModerationApi.kt — the
// SAME summary the Moderation Desk quick-action popup already renders), [ViewerAnalyticsProfile] /
// [WatchStreak] (AnalyticsApi.kt), [CurrencyAccountSummary] (EconomyApi.kt), [PermitGrant] (RolesApi.kt),
// [UserTtsVoice] (TtsApi.kt), and [Quote] (QuotesApi.kt).

/** The Community Profile page's single-person view (backend `ViewerProfileSummaryDto`). */
@Serializable
data class ViewerProfileSummary(
    val identity: ViewerIdentity = ViewerIdentity(),
    val moderationHistory: UserModerationHistorySummary? = null,
    val trust: UserTrustSummary? = null,
    val activity: ViewerAnalyticsProfile? = null,
    val streak: WatchStreak? = null,
    val economy: ViewerEconomy = ViewerEconomy(),
    val permits: ViewerPermits = ViewerPermits(),
    val overrides: ViewerOverrides = ViewerOverrides(),
    val commandUsage: ViewerCommandUsage = ViewerCommandUsage(),
    val freeFormData: List<ViewerDatum> = emptyList(),
    // Bounded first page (newest first) of quotes attributed to this person. The backend's own doc comment
    // names a `?userId=` filter on `GET /quotes` to page further, but the live `QuotesController` route
    // (`api/v1/quotes`, tenant resolved from the JWT) carries neither a `{channelId}` segment nor a `userId`
    // query parameter today — so only this embedded first page is wired here; paging beyond it needs that
    // backend filter to exist first (flagged, not fabricated).
    val recentQuotes: List<Quote> = emptyList(),
    val totalQuoteCount: Int = 0,
)

/** Identity & standing (owner punch list §3 row 1) — backend `ViewerIdentityDto`. */
@Serializable
data class ViewerIdentity(
    val userId: String = "",
    val twitchUserId: String? = null,
    val username: String = "",
    val displayName: String = "",
    val profileImageUrl: String? = null,
    val pronoun: String? = null,
    val altPronoun: String? = null,
    val linkedIdentities: List<LinkedIdentity> = emptyList(),
    // The ladder-valued chat-badge standing (Plane A — CommunityStanding: everyone/subscriber/vip/…), a
    // system-derived read: it reflects Twitch follow/sub/VIP/mod state, not something this page writes.
    val communityStanding: String = "everyone",
    val subTier: String? = null,
    // The management-role side (Plane B) — set/cleared here via RolesApi (roles:manage).
    val managementRole: String? = null,
    val memberSinceUtc: String? = null,
    val firstSeenUtc: String = "",
)

/** One linked platform identity (backend `LinkedIdentityDto`). */
@Serializable
data class LinkedIdentity(
    val provider: String = "",
    val providerUsername: String = "",
    val providerDisplayName: String? = null,
    val providerAvatarUrl: String? = null,
    val isPrimary: Boolean = false,
)

/** Economy & games (owner punch list §3 row 4) — backend `ViewerEconomyDto`. Display-only on this page. */
@Serializable
data class ViewerEconomy(
    val wallet: CurrencyAccountSummary? = null,
    val giveawayEntryCount: Int = 0,
    val giveawayWinCount: Int = 0,
    val leaderboardOptedOut: Boolean = false,
)

/**
 * Permits & consent (owner punch list §3 row 5) — backend `ViewerPermitsDto`. [activePermits] is genuinely
 * editable (grant/revoke via RolesApi's permit endpoints); [ageConsentGranted] has no mutation endpoint on
 * this page — it is the viewer's own GDPR consent record, display-only here.
 */
@Serializable
data class ViewerPermits(
    val activePermits: List<PermitGrant> = emptyList(),
    val ageConsentGranted: Boolean? = null,
    val ageConsentConfirmedUtc: String? = null,
)

/**
 * Custom bot behavior overrides (owner punch list §3 row 6) — backend `ViewerOverridesDto`. All three fields
 * are genuinely editable: [shoutoutMessageTemplate] / [raidMessageTemplate] via ModerationApi's shoutout-
 * override endpoints (keyed on the person's Twitch id), [ttsVoice] via TtsApi's per-viewer voice endpoints.
 */
@Serializable
data class ViewerOverrides(
    val shoutoutMessageTemplate: String? = null,
    val raidMessageTemplate: String? = null,
    val ttsVoice: UserTtsVoice? = null,
)

/** Command usage (owner punch list §3 row 7) — backend `ViewerCommandUsageDto`. Display-only. */
@Serializable
data class ViewerCommandUsage(
    val totalCommandsUsed: Int = 0,
    val lastUsedUtc: String? = null,
)

/**
 * One free-form `ViewerDatum` key/value pair (owner punch list §3 row 8) — backend `ViewerDatumDto`. The
 * existing generic "Community Data" editor (ViewerDataApi) reads/writes these, keyed on [ViewerIdentity.userId].
 */
@Serializable
data class ViewerDatum(val key: String = "", val value: String = "")
