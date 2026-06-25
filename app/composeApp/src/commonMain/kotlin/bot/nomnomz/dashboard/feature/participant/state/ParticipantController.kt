// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.participant.state

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.CatalogItem
import bot.nomnomz.dashboard.core.network.ChannelAppearance
import bot.nomnomz.dashboard.core.network.CurrencyAccount
import bot.nomnomz.dashboard.core.network.DashboardApi
import bot.nomnomz.dashboard.core.network.DashboardStats
import bot.nomnomz.dashboard.core.network.GamePlay
import bot.nomnomz.dashboard.core.network.GamePlayResult
import bot.nomnomz.dashboard.core.network.GameSummary
import bot.nomnomz.dashboard.core.network.LeaderboardEntry
import bot.nomnomz.dashboard.core.network.MusicSnapshot
import bot.nomnomz.dashboard.core.network.MusicApi
import bot.nomnomz.dashboard.core.network.EconomyApi
import bot.nomnomz.dashboard.core.network.ParticipantApi
import bot.nomnomz.dashboard.core.network.PronounOption
import bot.nomnomz.dashboard.core.network.SavingsJar
import bot.nomnomz.dashboard.core.network.SystemApi
import bot.nomnomz.dashboard.core.network.UserActivity
import bot.nomnomz.dashboard.core.network.UserProfile
import bot.nomnomz.dashboard.feature.shell.nav.ParticipantStanding
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The PARTICIPANT rung's state-holder (Rung 0) — the single holder behind the six participant screens. Unlike the
// management controllers it does NOT re-resolve the channel: the shell already resolved the caller's access
// (channel + own user GUID + community standing + permit capabilities) via `/effective/me`, so this is constructed
// WITH that context and addresses every self-service call by the caller's own id. Every read/write hits an existing
// backend route through the typed [ParticipantApi] / read-only economy + music + dashboard facades — no fabricated
// data. The standing-driven unlocks (sub-only lanes, higher pending limits, sub leaderboards) are decided here from
// [standing], surfaced in the page state for the screen to render.
class ParticipantController(
    private val channelId: String,
    private val userId: String?,
    val standing: ParticipantStanding,
    private val capabilities: List<String>,
    private val participantApi: ParticipantApi,
    private val dashboardApi: DashboardApi,
    private val economyApi: EconomyApi,
    private val musicApi: MusicApi,
    private val systemApi: SystemApi,
) {
    private val _myChannel: MutableStateFlow<MyChannelState> = MutableStateFlow(MyChannelState.Loading)
    private val _nowPlaying: MutableStateFlow<NowPlayingState> = MutableStateFlow(NowPlayingState.Loading)
    private val _leaderboards: MutableStateFlow<LeaderboardsState> =
        MutableStateFlow(LeaderboardsState.Loading)
    private val _store: MutableStateFlow<StoreState> = MutableStateFlow(StoreState.Loading)
    private val _games: MutableStateFlow<ParticipantGamesState> =
        MutableStateFlow(ParticipantGamesState.Loading)
    private val _me: MutableStateFlow<MeState> = MutableStateFlow(MeState.Loading)

    /** The My-Channel home: the caller's own profile/standing/activity + the channel's public summary. */
    val myChannel: StateFlow<MyChannelState> = _myChannel.asStateFlow()

    /** Now-playing + queue, plus the caller's own song-request submission state. */
    val nowPlaying: StateFlow<NowPlayingState> = _nowPlaying.asStateFlow()

    /** The leaderboard ranking + the caller's own opt-in/opt-out state. */
    val leaderboards: StateFlow<LeaderboardsState> = _leaderboards.asStateFlow()

    /** The caller's balance + the catalog (read + purchase) + community jars + transfers. */
    val store: StateFlow<StoreState> = _store.asStateFlow()

    /** The channel's games + the caller's own play state and history. */
    val games: StateFlow<ParticipantGamesState> = _games.asStateFlow()

    /** The caller's own data: profile (pronouns), activity summary, and participation footprint. */
    val me: StateFlow<MeState> = _me.asStateFlow()

    /** True when the caller may transfer points — they hold the `economy:transfer:write` capability. */
    val canTransfer: Boolean
        get() = capabilities.contains(TransferCapability)

    /** Whether sub-only affordances unlock — a sub or above sees the sub lane and sub leaderboards. */
    val subscriberUnlocked: Boolean
        get() = standing.isSubscriberOrAbove

    /** The per-request pending song cap — a sub/VIP gets a higher allowance than a plain viewer. */
    val pendingSongLimit: Int
        get() =
            when {
                standing.isVipOrAbove -> VipPendingSongs
                standing.isSubscriberOrAbove -> SubPendingSongs
                else -> BasePendingSongs
            }

    // ── My Channel ───────────────────────────────────────────────────────────

    /** Load the caller's own profile + activity and the channel's public summary. */
    suspend fun loadMyChannel() {
        _myChannel.value = MyChannelState.Loading
        if (!hasContext()) {
            _myChannel.value = MyChannelState.Error(NoChannelError)
            return
        }

        val stats: DashboardStats =
            when (val result: ApiResult<DashboardStats> = dashboardApi.stats(channelId)) {
                is ApiResult.Failure -> {
                    _myChannel.value = MyChannelState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        val profile: UserProfile =
            when (val result: ApiResult<UserProfile> = participantApi.myProfile(userId!!)) {
                is ApiResult.Failure -> {
                    _myChannel.value = MyChannelState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        when (val result: ApiResult<UserActivity> = participantApi.myActivity(userId)) {
            is ApiResult.Failure -> _myChannel.value = MyChannelState.Error(result.error.message)
            is ApiResult.Ok ->
                _myChannel.value =
                    MyChannelState.Ready(
                        profile = profile,
                        activity = result.value,
                        channel = stats,
                        standing = standing,
                    )
        }
    }

    // ── Now Playing / Queue ──────────────────────────────────────────────────

    /** Load the live now-playing + queue snapshot. */
    suspend fun loadNowPlaying() {
        _nowPlaying.value = NowPlayingState.Loading
        if (!hasContext()) {
            _nowPlaying.value = NowPlayingState.Error(NoChannelError)
            return
        }
        when (val result: ApiResult<MusicSnapshot> = musicApi.queue(channelId)) {
            is ApiResult.Failure -> _nowPlaying.value = NowPlayingState.Error(result.error.message)
            is ApiResult.Ok ->
                _nowPlaying.value =
                    NowPlayingState.Ready(
                        snapshot = result.value,
                        pendingLimit = pendingSongLimit,
                        subscriberLaneUnlocked = subscriberUnlocked,
                    )
        }
    }

    /**
     * Submit a song-request [query] as the caller. On success reloads the queue so the new track appears; on
     * failure surfaces the reason on the current Ready state without losing the rendered queue.
     */
    suspend fun submitSongRequest(query: String, requestedBy: String?) {
        if (!hasContext() || query.isBlank()) return
        when (
            val result: ApiResult<Unit> =
                participantApi.submitSongRequest(channelId, query.trim(), requestedBy)
        ) {
            is ApiResult.Ok -> loadNowPlaying()
            is ApiResult.Failure -> {
                val current: NowPlayingState = _nowPlaying.value
                if (current is NowPlayingState.Ready) {
                    _nowPlaying.value = current.copy(actionError = result.error.message)
                }
            }
        }
    }

    // ── Leaderboards ─────────────────────────────────────────────────────────

    /** Load the channel's top-holders ranking. Sub-only context drives whether the sub leaderboard is shown. */
    suspend fun loadLeaderboards() {
        _leaderboards.value = LeaderboardsState.Loading
        if (!hasContext()) {
            _leaderboards.value = LeaderboardsState.Error(NoChannelError)
            return
        }
        when (
            val result: ApiResult<List<LeaderboardEntry>> =
                economyApi.leaderboard(channelId, LeaderboardTop)
        ) {
            is ApiResult.Failure -> _leaderboards.value = LeaderboardsState.Error(result.error.message)
            is ApiResult.Ok ->
                _leaderboards.value =
                    LeaderboardsState.Ready(
                        ranking = result.value,
                        subscriberBoardUnlocked = subscriberUnlocked,
                    )
        }
    }

    /** Opt the caller IN to public leaderboards, then reflect it on the Ready state. */
    suspend fun optInToLeaderboards() {
        if (!hasContext()) return
        afterLeaderboardToggle(participantApi.leaderboardOptIn(channelId, userId!!), optedIn = true)
    }

    /** Opt the caller OUT of public leaderboards, then reflect it on the Ready state. */
    suspend fun optOutOfLeaderboards() {
        if (!hasContext()) return
        afterLeaderboardToggle(participantApi.leaderboardOptOut(channelId, userId!!), optedIn = false)
    }

    private fun afterLeaderboardToggle(result: ApiResult<Unit>, optedIn: Boolean) {
        val current: LeaderboardsState = _leaderboards.value
        if (current !is LeaderboardsState.Ready) return
        _leaderboards.value =
            when (result) {
                is ApiResult.Ok -> current.copy(optedIn = optedIn, actionError = null)
                is ApiResult.Failure -> current.copy(actionError = result.error.message)
            }
    }

    // ── My Points & Store ────────────────────────────────────────────────────

    /** Load the caller's balance, the catalog, and the community jars. */
    suspend fun loadStore() {
        _store.value = StoreState.Loading
        if (!hasContext()) {
            _store.value = StoreState.Error(NoChannelError)
            return
        }

        val account: CurrencyAccount =
            when (val result: ApiResult<CurrencyAccount> = participantApi.myAccount(channelId)) {
                is ApiResult.Failure -> {
                    _store.value = StoreState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        val catalog: List<CatalogItem> =
            when (val result: ApiResult<List<CatalogItem>> = participantApi.catalog(channelId)) {
                is ApiResult.Failure -> {
                    _store.value = StoreState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        when (val result: ApiResult<List<SavingsJar>> = participantApi.jars(channelId)) {
            is ApiResult.Failure -> _store.value = StoreState.Error(result.error.message)
            is ApiResult.Ok ->
                _store.value =
                    StoreState.Ready(
                        account = account,
                        catalog = catalog,
                        jars = result.value,
                        canTransfer = canTransfer,
                    )
        }
    }

    /** Redeem catalog [itemId] for the caller, then reload so the new balance + stock show. */
    suspend fun purchase(itemId: String, inputArgs: String?) {
        if (!hasContext()) return
        afterStoreWrite(participantApi.purchase(channelId, itemId, inputArgs))
    }

    /** Contribute [amount] of the caller's points to [jarId], then reload so the new balance + jar show. */
    suspend fun contributeToJar(jarId: String, amount: Long) {
        if (!hasContext() || amount <= 0) return
        afterStoreWrite(participantApi.contributeToJar(channelId, jarId, amount))
    }

    /** Transfer [amount] of the caller's points to [toViewerUserId], then reload so the new balance shows. */
    suspend fun transfer(toViewerUserId: String, amount: Long, reason: String?) {
        if (!hasContext() || !canTransfer || amount <= 0 || toViewerUserId.isBlank()) return
        afterStoreWrite(
            participantApi.transfer(
                channelId = channelId,
                fromViewerUserId = userId!!,
                toViewerUserId = toViewerUserId,
                amount = amount,
                reason = reason,
            )
        )
    }

    private suspend fun afterStoreWrite(result: ApiResult<Unit>) {
        when (result) {
            is ApiResult.Ok -> loadStore()
            is ApiResult.Failure -> {
                val current: StoreState = _store.value
                if (current is StoreState.Ready) {
                    _store.value = current.copy(actionError = result.error.message)
                }
            }
        }
    }

    // ── Games ────────────────────────────────────────────────────────────────

    /** Load the channel's games and the caller's own play history. */
    suspend fun loadGames() {
        _games.value = ParticipantGamesState.Loading
        if (!hasContext()) {
            _games.value = ParticipantGamesState.Error(NoChannelError)
            return
        }

        val games: List<GameSummary> =
            when (val result: ApiResult<List<GameSummary>> = participantApi.games(channelId)) {
                is ApiResult.Failure -> {
                    _games.value = ParticipantGamesState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        when (val result: ApiResult<List<GamePlay>> = participantApi.myGameHistory(channelId, userId!!)) {
            is ApiResult.Failure -> _games.value = ParticipantGamesState.Error(result.error.message)
            is ApiResult.Ok ->
                _games.value =
                    ParticipantGamesState.Ready(
                        games = games.filter { it.isEnabled },
                        history = result.value,
                    )
        }
    }

    /** Play [gameConfigId] for [betAmount] as the caller, then reload history; surface the settled outcome. */
    suspend fun playGame(gameConfigId: String, betAmount: Long) {
        if (!hasContext() || betAmount <= 0) return
        when (
            val result: ApiResult<GamePlayResult> =
                participantApi.playGame(channelId, gameConfigId, betAmount)
        ) {
            is ApiResult.Ok -> {
                val outcome: GamePlayResult = result.value
                loadGames()
                val current: ParticipantGamesState = _games.value
                if (current is ParticipantGamesState.Ready) {
                    _games.value = current.copy(lastOutcome = outcome, actionError = null)
                }
            }
            is ApiResult.Failure -> {
                val current: ParticipantGamesState = _games.value
                if (current is ParticipantGamesState.Ready) {
                    _games.value = current.copy(actionError = result.error.message)
                }
            }
        }
    }

    // ── Me ───────────────────────────────────────────────────────────────────

    /** Load the caller's own profile, activity summary, participation footprint, and the pronoun catalogue. */
    suspend fun loadMe() {
        _me.value = MeState.Loading
        if (userId == null) {
            _me.value = MeState.Error(NoChannelError)
            return
        }

        val profile: UserProfile =
            when (val result: ApiResult<UserProfile> = participantApi.myProfile(userId)) {
                is ApiResult.Failure -> {
                    _me.value = MeState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        val activity: UserActivity =
            when (val result: ApiResult<UserActivity> = participantApi.myActivity(userId)) {
                is ApiResult.Failure -> {
                    _me.value = MeState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        val pronouns: List<PronounOption> =
            when (val result: ApiResult<List<PronounOption>> = systemApi.pronouns()) {
                is ApiResult.Failure -> emptyList()
                is ApiResult.Ok -> result.value
            }

        when (val result: ApiResult<List<ChannelAppearance>> = participantApi.myChannels(userId)) {
            is ApiResult.Failure -> _me.value = MeState.Error(result.error.message)
            is ApiResult.Ok ->
                _me.value =
                    MeState.Ready(
                        profile = profile,
                        activity = activity,
                        channels = result.value,
                        standing = standing,
                        pronouns = pronouns,
                    )
        }
    }

    /** Update the caller's pronoun selection and refresh the Me state on success. */
    suspend fun updatePronoun(pronounId: Int?) {
        if (userId == null) return
        val current: MeState.Ready = (_me.value as? MeState.Ready) ?: return
        _me.value = current.copy(profileSaving = true, profileError = null)

        when (
            val result: ApiResult<UserProfile> =
                participantApi.updateMyProfile(
                    userId = userId,
                    displayName = null,
                    email = null,
                    pronounId = pronounId,
                )
        ) {
            is ApiResult.Failure ->
                _me.value = current.copy(profileSaving = false, profileError = result.error.message)
            is ApiResult.Ok ->
                _me.value =
                    current.copy(
                        profile = result.value,
                        profileSaving = false,
                        profileError = null,
                    )
        }
    }

    // A self-service call needs both the channel context and the caller's own user id; the no-channel fail-closed
    // state has neither, so the screens surface an error instead of issuing a bad request.
    private fun hasContext(): Boolean = channelId.isNotBlank() && userId != null

    private companion object {
        const val NoChannelError: String = "No active channel — reconnect and try again."
        const val TransferCapability: String = "economy:transfer:write"
        const val LeaderboardTop: Int = 25
        const val BasePendingSongs: Int = 1
        const val SubPendingSongs: Int = 3
        const val VipPendingSongs: Int = 5
    }
}
