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

// The typed PARTICIPANT facade — the community-plane self-service a viewer/sub/VIP runs for their OWN data
// (roles-permissions.md community plane: economy:account:read, economy:catalog:read/purchase, economy:jars:*,
// economy:transfer:write, music:request:submit, economy:leaderboards:opt-in/out, economy:games:play). Every call
// hits an EXISTING backend route (no new backend); nothing is fabricated — balances, catalog, jars, queue all
// come from the real economy/music controllers. The participant addresses their own records by their platform
// User GUID (the caller's `ResolvedAccess.userId`); the backend binds the actor/buyer/player from the JWT and
// re-checks every write regardless, so passing the id is for the route, not trust.
//
// Backend routes (all under `api/v1/channels/{channelId}`):
//   CurrencyController             GET  economy/accounts/me                             → StatusResponseDto<CurrencyAccountDto>   (community/Everyone — self-bound)
//                                  POST economy/transfer                  (TransferCommand)            (economy:transfer:write)
//   CatalogController              GET  economy/catalog                                 → PaginatedResponse<CatalogItemDto>      (economy:catalog:read)
//                                  POST economy/catalog/{itemId}/purchase (PurchaseRequest)            (economy:catalog:purchase)
//   SavingsJarsController          GET  economy/jars                                    → StatusResponseDto<List<SavingsJarDto>> (economy:jars:read)
//                                  POST economy/jars/{jarId}/contribute   (JarContributeRequest)       (economy:jars:contribute)
//   EconomyLeaderboardsController  POST economy/leaderboards/opt-in/{viewerUserId}                     (economy:leaderboards:opt-in)
//                                  POST economy/leaderboards/opt-out/{viewerUserId}                    (economy:leaderboards:opt-out)
//   GamesController                POST economy/games/{gameConfigId}/play (PlayGameRequest)            (economy:games:play)
//                                  GET  economy/games/history                           → PaginatedResponse<GamePlayDto>         (economy:games:history:read)
//   MusicController                POST music/queue                       (SongRequestDto)             (Everyone — no action floor)
interface ParticipantApi {
    /** The caller's own wallet on [channelId] (the backend binds the subject from the JWT and get-or-creates it). */
    suspend fun myAccount(channelId: String): ApiResult<CurrencyAccount>

    /** The channel's purchasable store items the caller may read (first page). */
    suspend fun catalog(channelId: String): ApiResult<List<CatalogItem>>

    /** Redeem catalog [itemId] for the caller (buyer + level bound server-side; [inputArgs] is the item's input). */
    suspend fun purchase(channelId: String, itemId: String, inputArgs: String?): ApiResult<Unit>

    /** The channel's community savings jars the caller can see and contribute to. */
    suspend fun jars(channelId: String): ApiResult<List<SavingsJar>>

    /** Contribute [amount] of the caller's points into [jarId] (contributor bound server-side). */
    suspend fun contributeToJar(channelId: String, jarId: String, amount: Long): ApiResult<Unit>

    /** Transfer [amount] of the caller's points to [toViewerUserId] (actor bound server-side; optional [reason]). */
    suspend fun transfer(
        channelId: String,
        fromViewerUserId: String,
        toViewerUserId: String,
        amount: Long,
        reason: String?,
    ): ApiResult<Unit>

    /** Opt [viewerUserId] (the caller) IN to public leaderboards on [channelId]. */
    suspend fun leaderboardOptIn(channelId: String, viewerUserId: String): ApiResult<Unit>

    /** Opt [viewerUserId] (the caller) OUT of public leaderboards on [channelId]. */
    suspend fun leaderboardOptOut(channelId: String, viewerUserId: String): ApiResult<Unit>

    /** The channel's playable games the caller may read (the same catalogue the manager configures). */
    suspend fun games(channelId: String): ApiResult<List<GameSummary>>

    /** Play [gameConfigId] for [betAmount] as the caller (player + level bound server-side). */
    suspend fun playGame(channelId: String, gameConfigId: String, betAmount: Long): ApiResult<GamePlayResult>

    /** The caller's own recent game-play history on [channelId] (scoped to [playerUserId] = the caller). */
    suspend fun myGameHistory(channelId: String, playerUserId: String): ApiResult<List<GamePlay>>

    /** Submit a song-request [query] to the channel's queue as the caller (community — no management floor). */
    suspend fun submitSongRequest(channelId: String, query: String, requestedBy: String?): ApiResult<Unit>

    /** The caller's own profile (display name, avatar, pronouns) by their platform [userId]. */
    suspend fun myProfile(userId: String): ApiResult<UserProfile>

    /** Update the caller's own profile ([displayName], [email], [pronounId]) by their platform [userId]. */
    suspend fun updateMyProfile(userId: String, displayName: String?, email: String?, pronounId: Int?): ApiResult<UserProfile>

    /** The channels the caller appears in as a participant (their follow/watch footprint) by their [userId]. */
    suspend fun myChannels(userId: String): ApiResult<List<ChannelAppearance>>

    /** The caller's own activity summary (messages, watch hours, commands) by their [userId]. */
    suspend fun myActivity(userId: String): ApiResult<UserActivity>
}

class RestParticipantApi(private val client: ApiClient) : ParticipantApi {

    override suspend fun myAccount(channelId: String): ApiResult<CurrencyAccount> =
        client.getEnvelope("api/v1/channels/$channelId/economy/accounts/me")

    override suspend fun catalog(channelId: String): ApiResult<List<CatalogItem>> =
        // PaginatedResponse is the flat `{ data: [...] }` body (not the single-value envelope), so getDirect.
        when (
            val page: ApiResult<PaginatedEnvelope<CatalogItem>> =
                client.getDirect("api/v1/channels/$channelId/economy/catalog?page=1&pageSize=50")
        ) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }

    override suspend fun purchase(
        channelId: String,
        itemId: String,
        inputArgs: String?,
    ): ApiResult<Unit> =
        // The buyer + role level are bound from the JWT server-side; the body carries only the item input.
        client.postUnit(
            "api/v1/channels/$channelId/economy/catalog/$itemId/purchase",
            PurchaseBody(inputArgs = inputArgs),
        )

    override suspend fun jars(channelId: String): ApiResult<List<SavingsJar>> =
        client.getEnvelope("api/v1/channels/$channelId/economy/jars")

    override suspend fun contributeToJar(
        channelId: String,
        jarId: String,
        amount: Long,
    ): ApiResult<Unit> =
        // The contributor is bound from the JWT server-side; the body carries only the amount.
        client.postUnit(
            "api/v1/channels/$channelId/economy/jars/$jarId/contribute",
            JarContributeBody(amount = amount),
        )

    override suspend fun transfer(
        channelId: String,
        fromViewerUserId: String,
        toViewerUserId: String,
        amount: Long,
        reason: String?,
    ): ApiResult<Unit> =
        // The actor is bound from the JWT server-side; `from` is the caller, `to` is the chosen recipient.
        client.postUnit(
            "api/v1/channels/$channelId/economy/transfer",
            TransferBody(
                fromViewerUserId = fromViewerUserId,
                toViewerUserId = toViewerUserId,
                amount = amount,
                reason = reason,
            ),
        )

    override suspend fun leaderboardOptIn(channelId: String, viewerUserId: String): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/economy/leaderboards/opt-in/$viewerUserId")

    override suspend fun leaderboardOptOut(channelId: String, viewerUserId: String): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/economy/leaderboards/opt-out/$viewerUserId")

    override suspend fun games(channelId: String): ApiResult<List<GameSummary>> =
        client.getEnvelope("api/v1/channels/$channelId/economy/games")

    override suspend fun playGame(
        channelId: String,
        gameConfigId: String,
        betAmount: Long,
    ): ApiResult<GamePlayResult> =
        // The player + role level are bound from the JWT server-side; the body carries only the bet.
        client.postEnvelope(
            "api/v1/channels/$channelId/economy/games/$gameConfigId/play",
            PlayGameBody(betAmount = betAmount),
        )

    override suspend fun myGameHistory(
        channelId: String,
        playerUserId: String,
    ): ApiResult<List<GamePlay>> =
        when (
            val page: ApiResult<PaginatedEnvelope<GamePlay>> =
                client.getDirect(
                    "api/v1/channels/$channelId/economy/games/history?playerUserId=$playerUserId&page=1&pageSize=25"
                )
        ) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }

    override suspend fun submitSongRequest(
        channelId: String,
        query: String,
        requestedBy: String?,
    ): ApiResult<Unit> =
        client.postUnit(
            "api/v1/channels/$channelId/music/queue",
            SongRequestBody(query = query, requestedBy = requestedBy),
        )

    override suspend fun myProfile(userId: String): ApiResult<UserProfile> =
        client.getEnvelope("api/v1/users/$userId/profile")

    override suspend fun updateMyProfile(
        userId: String,
        displayName: String?,
        email: String?,
        pronounId: Int?,
    ): ApiResult<UserProfile> =
        client.putEnvelope("api/v1/users/$userId/profile", UpdateProfileBody(displayName, email, pronounId))

    override suspend fun myChannels(userId: String): ApiResult<List<ChannelAppearance>> =
        client.getEnvelope("api/v1/users/$userId/channels")

    override suspend fun myActivity(userId: String): ApiResult<UserActivity> =
        client.getEnvelope("api/v1/users/$userId/stats")
}

/**
 * The caller's wallet (backend `CurrencyAccountDto`). Field names mirror the DTO camelCase. The participant
 * surface shows [balance] (their spendable points) plus the lifetime context; [isFrozen] disables self-service
 * spending in the UI (the backend enforces it regardless).
 */
@Serializable
data class CurrencyAccount(
    val id: String = "",
    val viewerUserId: String = "",
    val viewerTwitchUserId: String = "",
    val balance: Long = 0,
    val lifetimeEarned: Long = 0,
    val lifetimeSpent: Long = 0,
    val isFrozen: Boolean = false,
    val lastActivityAt: String? = null,
)

/**
 * One purchasable store item (backend `CatalogItemDto`). The participant surface reads [name]/[description]/[cost]
 * and the stock/cooldown context; only [isEnabled] items are offered, and a sold-out item ([stockRemaining] == 0)
 * disables its purchase. `permission` is the standing/role the backend requires — surfaced as context, enforced
 * server-side.
 */
@Serializable
data class CatalogItem(
    val id: String = "",
    val name: String = "",
    val description: String? = null,
    val sinkType: String = "",
    val cost: Long = 0,
    val iconUrl: String? = null,
    val isEnabled: Boolean = false,
    val permission: String = "",
    val cooldownSeconds: Int = 0,
    val cooldownPerUser: Boolean = false,
    val stockLimit: Int? = null,
    val stockRemaining: Int? = null,
    val maxPerViewerPerStream: Int? = null,
    // Operator/store-management fields (backend CatalogItemDto): bound pipeline, sort order, and audit stamps.
    val pipelineId: String? = null,
    val sortOrder: Int = 0,
    val createdAt: String = "",
    val updatedAt: String = "",
)

/**
 * A community savings jar (backend `SavingsJarDto`). The participant surface reads [name]/[balance]/[goalAmount]
 * to show progress and contributes to an [isOpen] jar; a closed jar shows but disables contribution.
 */
@Serializable
data class SavingsJar(
    val id: String = "",
    val ownerBroadcasterId: String = "",
    val name: String = "",
    val description: String? = null,
    val goalAmount: Long? = null,
    val balance: Long = 0,
    val iconUrl: String? = null,
    val isOpen: Boolean = false,
)

/**
 * The settled outcome of one game play (backend `GamePlayResultDto`). The participant surface shows [outcome],
 * the [betAmount]/[payoutAmount]/[netResult], and the resulting [balanceAfter] so the player sees what changed.
 */
@Serializable
data class GamePlayResult(
    val id: Long = 0,
    val gameType: String = "",
    val outcome: String = "",
    val betAmount: Long = 0,
    val payoutAmount: Long = 0,
    val netResult: Long = 0,
    val balanceAfter: Long = 0,
)

/**
 * One past game play (backend `GamePlayDto`) — the participant's own history rows: what they bet, the [outcome],
 * and the [netResult]. `gameConfigId` ties the row to a game; `createdAt` orders the list.
 */
@Serializable
data class GamePlay(
    val id: Long = 0,
    val gameConfigId: String = "",
    val betAmount: Long = 0,
    val outcome: String = "",
    val payoutAmount: Long = 0,
    val netResult: Long = 0,
    val createdAt: String = "",
)

/**
 * The caller's profile (backend `UserProfileDto`). The participant "Me" screen reads and writes
 * [displayName], [email], and [pronounId] via `PUT /users/{userId}/profile`.
 */
@Serializable
data class UserProfile(
    val id: String = "",
    val username: String = "",
    val displayName: String = "",
    val profileImageUrl: String? = null,
    val email: String? = null,
    val pronoun: String? = null,
    val pronounId: Int? = null,
    val createdAt: String = "",
    val lastLoginAt: String = "",
)

/**
 * One channel the caller appears in (backend `UserChannelAppearanceDto`). [channelId] is the channel's Guid,
 * enabling the per-channel picker on the participant screen.
 */
@Serializable
data class ChannelAppearance(
    val channelId: String = "",
    val channelName: String = "",
    val followDate: String = "",
    val messages: Int = 0,
    val watchTime: String = "",
)

/**
 * The caller's own activity summary (backend `UserStatsDto`) — the "my data" footprint the Me screen shows:
 * message count, watch hours, the channels they appear in, and commands used.
 */
@Serializable
data class UserActivity(
    val messageCount: Int = 0,
    val watchHours: Double = 0.0,
    val channelsCount: Int = 0,
    val commandsUsed: Int = 0,
    val firstSeen: String? = null,
    val lastActive: String? = null,
    val exportAvailable: Boolean = false,
)

/** Request body for a catalog purchase (backend `PurchaseRequest`); buyer + level + item are bound server-side. */
@Serializable
private data class PurchaseBody(val inputArgs: String?)

/** Request body for a jar contribution (backend `JarContributeRequest`); jar + contributor are bound server-side. */
@Serializable
private data class JarContributeBody(val amount: Long)

/** Request body for a points transfer (backend `TransferCommand`); the actor is bound server-side. */
@Serializable
data class TransferBody(
    val fromViewerUserId: String,
    val toViewerUserId: String,
    val amount: Long,
    val reason: String?,
)

/** Request body for a game play (backend `PlayGameRequest`); player + level + game are bound server-side. */
@Serializable
private data class PlayGameBody(val betAmount: Long)

/** Request body for updating the caller's own profile (backend `UpdateUserProfileRequest`). */
@Serializable
private data class UpdateProfileBody(val displayName: String?, val email: String?, val pronounId: Int?)

/** Request body for a song-request submission (backend `SongRequestDto`): the search query + an optional name. */
@Serializable
private data class SongRequestBody(val query: String, val requestedBy: String?)
