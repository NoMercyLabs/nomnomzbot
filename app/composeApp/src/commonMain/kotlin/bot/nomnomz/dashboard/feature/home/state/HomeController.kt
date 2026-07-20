// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.home.state

import bot.nomnomz.dashboard.core.designsystem.component.PickerOption
import bot.nomnomz.dashboard.core.network.ActivityEvent
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.Category
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.CommandSummary
import bot.nomnomz.dashboard.core.network.CommandsApi
import bot.nomnomz.dashboard.core.network.CommunityApi
import bot.nomnomz.dashboard.core.network.DashboardApi
import bot.nomnomz.dashboard.core.network.DashboardStats
import bot.nomnomz.dashboard.core.network.StreamApi
import bot.nomnomz.dashboard.core.network.StreamInfo
import bot.nomnomz.dashboard.core.network.StreamInfoUpdate
import bot.nomnomz.dashboard.core.network.ViewerOption
import bot.nomnomz.dashboard.core.realtime.DashboardHubClient
import bot.nomnomz.dashboard.core.realtime.HubEvent
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.filterIsInstance

// The Home page's state-holder (frontend-ia.md §3 — the live channel landing). Resolves the active channel,
// then loads its real snapshot, current stream info, and recent activity from the backend in parallel.
// The screen renders [state]; a pull / reconnect calls [load] again.
//
// Real-time: when [hubClient] + [baseUrl] + [accessToken] are supplied, [load] connects the hub after the
// channel resolves so all pages receive live push events for the duration of the shell session.

// The channel-event types the Recent Activity feed labels meaningfully (mirror of ActivityRow's `when` in
// HomeScreen.kt). Anything else — chat messages, and types without a friendly label — is filtered OUT of the
// feed rather than shown as a useless generic "Channel event" or a chat line masquerading as an event.
private val ACTIVITY_EVENT_TYPES: Set<String> = setOf(
    "channel.follow",
    "channel.subscribe",
    "channel.subscription.message",
    "channel.subscription.gift",
    "channel.cheer",
    "channel.raid",
    "channel.channel_points_custom_reward_redemption.add",
    "channel.ban",
    "channel.timeout",
    "channel.moderator.add",
    "channel.moderator.remove",
)

class HomeController(
    private val channelsApi: ChannelsApi,
    private val dashboardApi: DashboardApi,
    private val streamApi: StreamApi,
    private val commandsApi: CommandsApi,
    private val communityApi: CommunityApi,
    private val hubClient: DashboardHubClient? = null,
    private val baseUrl: () -> String? = { null },
    private val accessToken: () -> String? = { null },
    /** Called once after the primary channel resolves with the streamer's chat color (#RRGGBB or null). */
    private val onChatColorResolved: ((String?) -> Unit)? = null,
) {
    private val _state: MutableStateFlow<HomeState> = MutableStateFlow(HomeState.Loading)

    /** The page render state: loading / ready (with the snapshot + stream info + activity) / error. */
    val state: StateFlow<HomeState> = _state.asStateFlow()

    // Resolved on first load, reused by stream-edit actions without re-resolving.
    private var channelId: String? = null

    /** Resolve the active channel, then load its live snapshot, stream info, and recent activity. */
    suspend fun load() {
        // Only show the full-page loading state on first load; a refetch after a mutation keeps
        // the current content on screen (no flash) and swaps it when the new data arrives.
        if (_state.value !is HomeState.Ready) _state.value = HomeState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = HomeState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        channelId = channel.id
        onChatColorResolved?.invoke(channel.chatColor)

        val url: String? = baseUrl()
        if (hubClient != null && url != null) {
            // Pass the live token getter (not a snapshot) so the hub reads the current JWT on each reconnect;
            // a REST-driven refresh then can't strand the socket on a stale token (a dead chat feed).
            hubClient.connect(url, accessToken, channel.id)
        }

        when (val statsResult: ApiResult<DashboardStats> = dashboardApi.stats(channel.id)) {
            is ApiResult.Failure -> {
                _state.value = HomeState.Error(statsResult.error.message)
                return
            }
            is ApiResult.Ok -> {
                // Load stream info and activity concurrently after stats; failures are non-fatal.
                val streamInfo: StreamInfo? =
                    when (val r: ApiResult<StreamInfo> = streamApi.info(channel.id)) {
                        is ApiResult.Ok -> r.value
                        is ApiResult.Failure -> null
                    }
                val activity: List<ActivityEvent> =
                    when (val r: ApiResult<List<ActivityEvent>> = dashboardApi.activity(channel.id)) {
                        // Only surface events the feed can label meaningfully — chat messages and unlabeled
                        // types otherwise render a useless generic "Channel event" (or a chat line as an "event").
                        is ApiResult.Ok -> r.value.filter { it.type in ACTIVITY_EVENT_TYPES }
                        is ApiResult.Failure -> emptyList()
                    }
                val topCommands: List<CommandSummary> =
                    when (val r: ApiResult<List<CommandSummary>> = commandsApi.list(channel.id)) {
                        is ApiResult.Ok -> r.value.sortedByDescending { it.useCount }.take(5)
                        is ApiResult.Failure -> emptyList()
                    }
                _state.value = HomeState.Ready(
                    stats = statsResult.value,
                    streamInfo = streamInfo,
                    activity = activity,
                    topCommands = topCommands,
                )
            }
        }
    }

    /**
     * Update stream title, game, and/or tags. Merges the backend response into the current state — into
     * [HomeState.Ready.streamInfo] AND the [HomeState.Ready.stats] the live banner renders, so the saved title
     * shows immediately instead of only after the next full reload.
     */
    suspend fun updateStreamInfo(title: String?, gameName: String?, tags: List<String>?) {
        val channel: String = channelId ?: return
        val update: StreamInfoUpdate = StreamInfoUpdate(title = title, gameName = gameName, tags = tags)
        when (val result: ApiResult<StreamInfo> = streamApi.update(channel, update)) {
            is ApiResult.Failure -> {
                val current: HomeState = _state.value
                if (current is HomeState.Ready) {
                    _state.value = current.copy(streamError = result.error.message)
                }
            }
            is ApiResult.Ok -> {
                val current: HomeState = _state.value
                if (current is HomeState.Ready) {
                    _state.value = current.copy(
                        stats = current.stats.copy(
                            streamTitle = result.value.title,
                            gameName = result.value.gameName,
                        ),
                        streamInfo = result.value,
                        streamError = null,
                    )
                }
            }
        }
    }

    /**
     * Autocomplete Twitch categories for the stream-info "game" picker. Maps each match to a [PickerOption]
     * whose [PickerOption.id] is the Twitch category id and [PickerOption.label] the canonical game name — the
     * stream update writes only the NAME. Best-effort: empty on failure or before the channel resolves.
     */
    suspend fun searchCategories(query: String): List<PickerOption> {
        val channel: String = channelId ?: return emptyList()
        return when (val result: ApiResult<List<Category>> = streamApi.searchCategories(channel, query)) {
            is ApiResult.Ok -> result.value.map { PickerOption(id = it.id, label = it.name) }
            is ApiResult.Failure -> emptyList()
        }
    }

    /**
     * Autocomplete raid targets for the raid dialog. NOTE: community/search only finds the channel's OWN known
     * viewers/chatters by name (the available endpoint) — it yields the Twitch user id the raid write consumes.
     * Best-effort: empty on failure or before the channel resolves.
     */
    suspend fun searchRaidTargets(query: String): List<PickerOption> {
        val channel: String = channelId ?: return emptyList()
        return when (val result: ApiResult<List<ViewerOption>> = communityApi.searchViewers(channel, query)) {
            is ApiResult.Ok ->
                result.value.map { PickerOption(id = it.id, label = it.label, sublabel = it.subLabel) }
            is ApiResult.Failure -> emptyList()
        }
    }

    /**
     * Subscribe to hub events — updates the home state in real-time:
     * - [HubEvent.StreamStatusChanged]: toggles live/offline and updates viewer count.
     * - [HubEvent.StreamInfoChanged]: applies a title/category change (channel.update) to the live banner —
     *   including one made by another operator or straight on Twitch, not just this session's own edit.
     * - [HubEvent.ChannelEvent]: prepends to the activity feed (cap 20) so new events appear instantly.
     */
    suspend fun subscribeToHub(hubEvents: SharedFlow<HubEvent>) {
        hubEvents.collect { evt ->
            val current: HomeState = _state.value
            if (current is HomeState.Ready) {
                when (evt) {
                    is HubEvent.StreamStatusChanged ->
                        _state.value = current.copy(
                            stats = current.stats.copy(isLive = evt.status.isLive)
                        )
                    is HubEvent.StreamInfoChanged ->
                        _state.value = current.copy(
                            stats = current.stats.copy(
                                streamTitle = evt.info.title,
                                gameName = evt.info.gameName,
                            ),
                            streamInfo = current.streamInfo?.copy(
                                title = evt.info.title,
                                gameName = evt.info.gameName,
                            ),
                        )
                    is HubEvent.ChannelEvent -> {
                        // Skip chat + unlabeled types — they'd render a useless "Channel event" (or a chat line
                        // masquerading as an event). Same set the initial load filters on.
                        if (evt.event.type in ACTIVITY_EVENT_TYPES) {
                            val newEvent: ActivityEvent = ActivityEvent(
                                id = evt.event.timestamp,
                                type = evt.event.type,
                                userId = evt.event.userId,
                                username = evt.event.userDisplayName,
                                timestamp = evt.event.timestamp,
                            )
                            _state.value = current.copy(
                                activity = (listOf(newEvent) + current.activity).take(20)
                            )
                        }
                    }
                    is HubEvent.RewardRedeemed -> {
                        // A channel-point redemption is pushed as its OWN hub event, NOT a generic ChannelEvent —
                        // so without this branch it fell through and only appeared on a manual reload. Prepend it
                        // live to the activity feed (its type is already in ACTIVITY_EVENT_TYPES and rendered).
                        val newEvent: ActivityEvent = ActivityEvent(
                            id = evt.event.redemptionId,
                            type = "channel.channel_points_custom_reward_redemption.add",
                            userId = evt.event.userId,
                            username = evt.event.userDisplayName,
                            // Carry the reward name in `data` as the SAME {"rewardTitle":…} JSON the REST activity
                            // feed emits, so the row shows WHICH reward was redeemed — live and on reload alike.
                            data = buildJsonObject { put("rewardTitle", evt.event.rewardTitle) }.toString(),
                            timestamp = evt.event.timestamp,
                        )
                        _state.value = current.copy(
                            activity = (listOf(newEvent) + current.activity).take(20)
                        )
                    }
                    else -> Unit
                }
            }
        }
    }
}

/** The Home page render state. */
sealed interface HomeState {
    data object Loading : HomeState

    data class Ready(
        val stats: DashboardStats,
        val streamInfo: StreamInfo? = null,
        val activity: List<ActivityEvent> = emptyList(),
        val topCommands: List<CommandSummary> = emptyList(),
        /** Non-null when the last [HomeController.updateStreamInfo] call failed. */
        val streamError: String? = null,
    ) : HomeState

    data class Error(val detail: String) : HomeState
}
