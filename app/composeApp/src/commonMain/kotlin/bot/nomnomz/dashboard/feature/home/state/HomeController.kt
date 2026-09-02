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
import bot.nomnomz.dashboard.core.designsystem.resolveRowLabel
import bot.nomnomz.dashboard.core.network.ActionRequiredItem
import bot.nomnomz.dashboard.core.network.ActivityEvent
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.AutomodConfig
import bot.nomnomz.dashboard.core.network.Category
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.CommandSummary
import bot.nomnomz.dashboard.core.network.CommandsApi
import bot.nomnomz.dashboard.core.network.CommunityApi
import bot.nomnomz.dashboard.core.network.DashboardApi
import bot.nomnomz.dashboard.core.network.DashboardStats
import bot.nomnomz.dashboard.core.network.IntegrationStatus
import bot.nomnomz.dashboard.core.network.IntegrationsApi
import bot.nomnomz.dashboard.core.network.ModerationApi
import bot.nomnomz.dashboard.core.network.ModerationQueueItem
import bot.nomnomz.dashboard.core.network.NotificationsApi
import bot.nomnomz.dashboard.core.network.ResolvedAutomodQueueItem
import bot.nomnomz.dashboard.core.network.UserModerationContext
import bot.nomnomz.dashboard.core.network.PipelineSummary
import bot.nomnomz.dashboard.core.network.PipelinesApi
import bot.nomnomz.dashboard.core.network.ReplayResult
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
    private val notificationsApi: NotificationsApi,
    private val pipelinesApi: PipelinesApi,
    private val integrationsApi: IntegrationsApi,
    private val moderationApi: ModerationApi,
    private val hubClient: DashboardHubClient? = null,
    private val baseUrl: () -> String? = { null },
    private val accessToken: () -> String? = { null },
    /** Called once after the primary channel resolves with the streamer's chat color (#RRGGBB or null). */
    private val onChatColorResolved: ((String?) -> Unit)? = null,
) {
    private val _state: MutableStateFlow<HomeState> = MutableStateFlow(HomeState.Loading)

    /** The page render state: loading / ready (with the snapshot + stream info + activity) / error. */
    val state: StateFlow<HomeState> = _state.asStateFlow()

    private val _heldReview: MutableStateFlow<HeldReviewState?> = MutableStateFlow(null)

    /** The held-message review dialog's state — null while it is closed (mirrors ModerationController's
     * per-user context dialog pattern). */
    val heldReview: StateFlow<HeldReviewState?> = _heldReview.asStateFlow()

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
                val commandsResult: ApiResult<List<CommandSummary>> = commandsApi.list(channel.id)
                val topCommands: List<CommandSummary> =
                    when (commandsResult) {
                        is ApiResult.Ok -> commandsResult.value.sortedByDescending { it.useCount }.take(5)
                        is ApiResult.Failure -> emptyList()
                    }
                val actionRequired: List<ActionRequiredItem> =
                    when (val r: ApiResult<List<ActionRequiredItem>> = notificationsApi.actionRequired(channel.id)) {
                        is ApiResult.Ok -> r.value
                        is ApiResult.Failure -> emptyList()
                    }
                val pipelinesResult: ApiResult<List<PipelineSummary>> = pipelinesApi.list(channel.id)
                val integrationsResult: ApiResult<List<IntegrationStatus>> = integrationsApi.status(channel.id)

                // Each dimension counts as "confirmed empty" only on a successful read that came back empty —
                // a failed read is treated as NOT empty (unknown), so a real read failure never manufactures a
                // false "you're new here" checklist for an already-configured channel.
                val commandsConfirmedEmpty: Boolean = commandsResult is ApiResult.Ok && commandsResult.value.isEmpty()
                val pipelinesConfirmedEmpty: Boolean = pipelinesResult is ApiResult.Ok && pipelinesResult.value.isEmpty()
                val integrationsConfirmedEmpty: Boolean =
                    integrationsResult is ApiResult.Ok && integrationsResult.value.none { it.connected }

                // First-run checklist shows only when EVERY dimension is confirmed empty — truthful state: it
                // disappears the moment the channel has any real commands, pipelines, or a connected integration.
                val firstRunSteps: List<FirstRunStep> =
                    if (commandsConfirmedEmpty && pipelinesConfirmedEmpty && integrationsConfirmedEmpty) {
                        listOf(
                            FirstRunStep(kind = FirstRunStepKind.ConnectIntegration, deepLinkRoute = "Integrations"),
                            FirstRunStep(kind = FirstRunStepKind.CreateCommand, deepLinkRoute = "Commands"),
                            FirstRunStep(kind = FirstRunStepKind.CreatePipeline, deepLinkRoute = "Pipelines"),
                        )
                    } else {
                        emptyList()
                    }

                _state.value = HomeState.Ready(
                    stats = statsResult.value,
                    streamInfo = streamInfo,
                    activity = activity,
                    topCommands = topCommands,
                    actionRequired = actionRequired,
                    firstRunSteps = firstRunSteps,
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
            is ApiResult.Ok ->
                result.value.map {
                    PickerOption(id = it.id, label = resolveRowLabel(it.name, typeLabel = "Category", discriminatorSource = it.id))
                }
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
     * Replay the captured alert/TTS payload for [eventId] to currently-subscribed widgets — for when a
     * WebSocket drop meant OBS/overlay/TTS missed it live (backend `dashboard:replay`, Mod floor). Tracks the
     * outcome per-event in [HomeState.Ready.replayStatus] so only THIS row shows in-flight/disabled and its own
     * distinct result — other rows are unaffected. A 404 means nothing was captured for this event: that is
     * surfaced as [ReplayStatus.NothingToReplay], never disguised as [ReplayStatus.Replayed].
     */
    suspend fun replay(eventId: String) {
        val channel: String = channelId ?: return
        val before: HomeState = _state.value
        if (before !is HomeState.Ready) return
        _state.value = before.copy(replayStatus = before.replayStatus + (eventId to ReplayStatus.InFlight))

        val outcome: ReplayStatus =
            when (val result: ApiResult<ReplayResult> = dashboardApi.replay(channel, eventId)) {
                is ApiResult.Ok -> ReplayStatus.Replayed(result.value.widgetsNotified)
                is ApiResult.Failure ->
                    if (result.error.status == 404) {
                        ReplayStatus.NothingToReplay
                    } else {
                        ReplayStatus.Failed(result.error.message)
                    }
            }

        val after: HomeState = _state.value
        if (after is HomeState.Ready) {
            _state.value = after.copy(replayStatus = after.replayStatus + (eventId to outcome))
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
                            // The backend keys RenderedAlertCapture (and therefore Replay) by the domain
                            // EventId, not Twitch's own redemptionId — using redemptionId here made every
                            // live-pushed redemption row 404 on Replay even though a capture existed under
                            // the correct id (a reload, which fetches via REST, never showed the bug since
                            // GetActivity already returns the right id).
                            id = evt.event.eventId ?: evt.event.redemptionId,
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

    // ─── Attention inbox (S-OWN22) ────────────────────────────────────────────

    /**
     * Dismiss [item] via the persisted dismissal endpoint; on success the item leaves [HomeState.Ready.actionRequired]
     * immediately AND stays gone across reloads (the backend excludes dismissed keys from the next read).
     * No optimistic update — a failed dismiss leaves the item in place with the backend's error verbatim.
     */
    suspend fun dismissAttentionItem(item: ActionRequiredItem) {
        val channel: String = channelId ?: return
        when (val result: ApiResult<Unit> = notificationsApi.dismissActionRequired(channel, listOf(item.id))) {
            is ApiResult.Ok -> removeAttentionItem(item.id)
            is ApiResult.Failure -> {
                val current: HomeState = _state.value
                if (current is HomeState.Ready) {
                    _state.value = current.copy(attentionError = result.error.message)
                }
            }
        }
    }

    /**
     * Open the held-message review dialog for [item] (kind `held_chat_message`): loads the pending AutoMod
     * queue rows for the item's [ActionRequiredItem.queueItemIds], plus — best-effort — the chatter's
     * moderation context (trust/heat badges) and the channel's heat auto-timeout threshold.
     */
    suspend fun openHeldReview(item: ActionRequiredItem) {
        val channel: String = channelId ?: return
        _heldReview.value = HeldReviewState.Loading
        val rows: List<ModerationQueueItem> =
            when (val result: ApiResult<List<ModerationQueueItem>> = moderationApi.automodQueue(channel, "pending")) {
                is ApiResult.Failure -> {
                    _heldReview.value = HeldReviewState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value.filter { it.id in item.queueItemIds }
            }
        // Context + threshold are enrichment, not gates: a failed read renders the dialog without badges
        // rather than blocking the review actions (which is why their failures fold to null/default).
        val context: UserModerationContext? =
            item.sourceUserId?.let { userId ->
                when (val result: ApiResult<UserModerationContext> = moderationApi.userContext(channel, userId)) {
                    is ApiResult.Ok -> result.value
                    is ApiResult.Failure -> null
                }
            }
        val heatThreshold: Int =
            when (val result: ApiResult<AutomodConfig> = moderationApi.automod(channel)) {
                is ApiResult.Ok -> result.value.heatTimeoutThreshold
                is ApiResult.Failure -> DEFAULT_HEAT_THRESHOLD
            }
        _heldReview.value = HeldReviewState.Ready(
            item = item,
            messages = rows,
            userContext = context,
            heatThreshold = heatThreshold,
        )
    }

    /** Close the held-message review dialog (state only — nothing is resolved or lost). */
    fun closeHeldReview() {
        _heldReview.value = null
    }

    /**
     * Resolve ONE held message: [action] is `approve` (Allow) or `deny` (Block); a deny may carry a
     * [followUp] (`timeout` needs [timeoutSeconds], `ban` takes an optional [reason]). No optimistic update:
     * - clean success → the message leaves the dialog, the Home item's count/ids shrink (gone when empty);
     * - partial success (deny stood, follow-up failed) → NOTHING is removed and the follow-up failure text
     *   shows verbatim, so a "blocked but the ban failed" outcome is never mistaken for done;
     * - failure → nothing is removed, the backend's error shows verbatim.
     */
    suspend fun resolveHeldMessage(
        queueItemId: String,
        action: String,
        followUp: String? = null,
        timeoutSeconds: Int? = null,
        reason: String? = null,
    ) {
        val channel: String = channelId ?: return
        val ready: HeldReviewState.Ready = _heldReview.value as? HeldReviewState.Ready ?: return
        when (
            val result: ApiResult<ResolvedAutomodQueueItem> =
                moderationApi.resolveAutomodQueueItem(channel, queueItemId, action, followUp, timeoutSeconds, reason)
        ) {
            is ApiResult.Failure -> _heldReview.value = ready.copy(actionError = result.error.message)
            is ApiResult.Ok -> {
                val followUpError: String? = result.value.followUpError
                if (followUpError != null) {
                    _heldReview.value = ready.copy(actionError = followUpError)
                } else {
                    removeResolvedHeldMessage(ready, queueItemId)
                }
            }
        }
    }

    /**
     * Resolve EVERY remaining held message in the open dialog (the bulk all-from-this-user row) with the same
     * action/follow-up. Sequential, stop-free: each message applies its own outcome (success removes it,
     * failure or partial outcome keeps it with the error), so one Helix hiccup never voids the rest.
     */
    suspend fun resolveAllHeldMessages(
        action: String,
        followUp: String? = null,
        timeoutSeconds: Int? = null,
        reason: String? = null,
    ) {
        val open: HeldReviewState.Ready = _heldReview.value as? HeldReviewState.Ready ?: return
        open.messages.map { it.id }.forEach { queueItemId ->
            resolveHeldMessage(queueItemId, action, followUp, timeoutSeconds, reason)
        }
    }

    /** Add [term] (a held message's text) to the channel's blocked-terms list. Success/failure is surfaced
     * on the open dialog; the held message itself stays pending — blocking the term does not resolve it. */
    suspend fun blockTermFromHeldMessage(term: String) {
        val channel: String = channelId ?: return
        val ready: HeldReviewState.Ready = _heldReview.value as? HeldReviewState.Ready ?: return
        when (val result: ApiResult<Unit> = moderationApi.addBlockedTerm(channel, term)) {
            is ApiResult.Ok -> _heldReview.value = ready.copy(actionError = null, blockedTerm = term)
            is ApiResult.Failure -> _heldReview.value = ready.copy(actionError = result.error.message)
        }
    }

    // A cleanly-resolved message leaves the dialog and shrinks its Home item (count/ids); the LAST message
    // removes the item and closes the dialog — the group is fully handled.
    private fun removeResolvedHeldMessage(ready: HeldReviewState.Ready, queueItemId: String) {
        val remaining: List<ModerationQueueItem> = ready.messages.filterNot { it.id == queueItemId }
        val remainingIds: List<String> = ready.item.queueItemIds.filterNot { it == queueItemId }
        if (remaining.isEmpty()) {
            removeAttentionItem(ready.item.id)
            _heldReview.value = null
            return
        }
        val shrunkItem: ActionRequiredItem = ready.item.copy(queueItemIds = remainingIds, count = remaining.size)
        _heldReview.value = ready.copy(item = shrunkItem, messages = remaining, actionError = null)
        val current: HomeState = _state.value
        if (current is HomeState.Ready) {
            _state.value = current.copy(
                actionRequired = current.actionRequired.map { if (it.id == shrunkItem.id) shrunkItem else it }
            )
        }
    }

    private fun removeAttentionItem(itemId: String) {
        val current: HomeState = _state.value
        if (current is HomeState.Ready) {
            _state.value = current.copy(
                actionRequired = current.actionRequired.filterNot { it.id == itemId },
                attentionError = null,
            )
        }
    }
}

/**
 * Maps an [ActionRequiredItem.kind] to the [bot.nomnomz.dashboard.feature.shell.nav.ShellRoute] name the
 * item's Review action navigates to — the frontend owns this mapping now; the wire's `deepLinkRoute` (a URL
 * path, not a route name — the old dead click) is no longer consumed. Null for an unknown kind: no navigation
 * is honest, a wrong page is not.
 */
fun attentionRouteFor(kind: String): String? =
    when (kind) {
        "held_chat_message" -> "Moderation"
        "integration_token_dead" -> "Integrations"
        else -> null
    }

/** The three-way severity scale an [ActionRequiredItem.severity] maps onto (fixes the old binarisation
 * that rendered `info` as "Warning"). Unknown future values read as [Warning] — attention-worthy, not scary. */
enum class AttentionSeverity {
    Critical,
    Warning,
    Info,
}

/** Maps the wire severity (`critical` | `warning` | `info`) to [AttentionSeverity]. */
fun attentionSeverityFor(severity: String): AttentionSeverity =
    when (severity) {
        "critical" -> AttentionSeverity.Critical
        "info" -> AttentionSeverity.Info
        else -> AttentionSeverity.Warning
    }

// The bot's default heat auto-timeout threshold (backend AutomodConfigDto default) — used when the automod
// config read fails so the held-message dialog can still color heat consistently.
private const val DEFAULT_HEAT_THRESHOLD: Int = 80

/** The held-message review dialog's load/render state. */
sealed interface HeldReviewState {
    data object Loading : HeldReviewState

    /**
     * The pending queue rows for the opened item ([messages], full content snapshots), the chatter's
     * moderation context when it loaded ([userContext] — trust/heat badges), and the channel's heat
     * auto-timeout threshold. [actionError] is the last action's backend error — verbatim, including the
     * partial "deny stood but the follow-up failed" outcome. [blockedTerm] is the last term successfully
     * added to the blocked-terms list (a visible confirmation, never a fake one).
     */
    data class Ready(
        val item: ActionRequiredItem,
        val messages: List<ModerationQueueItem>,
        val userContext: UserModerationContext? = null,
        val heatThreshold: Int = DEFAULT_HEAT_THRESHOLD,
        val actionError: String? = null,
        val blockedTerm: String? = null,
    ) : HeldReviewState

    data class Error(val detail: String) : HeldReviewState
}

/** The Home page render state. */
sealed interface HomeState {
    data object Loading : HomeState

    data class Ready(
        val stats: DashboardStats,
        val streamInfo: StreamInfo? = null,
        val activity: List<ActivityEvent> = emptyList(),
        val topCommands: List<CommandSummary> = emptyList(),
        /** Real, already-detected conditions needing the streamer's attention — empty when nothing is wrong,
         * never a fabricated "all good" positive. Renders as the Home hero tile only when non-empty. */
        val actionRequired: List<ActionRequiredItem> = emptyList(),
        /**
         * Suggested next actions for a channel with no commands, no pipelines, and no connected integration —
         * empty for any channel with real configured content (truthful state, never a stale "still onboarding").
         */
        val firstRunSteps: List<FirstRunStep> = emptyList(),
        /** Non-null when the last [HomeController.updateStreamInfo] call failed. */
        val streamError: String? = null,
        /** Non-null when the last attention-inbox dismiss failed — the backend's error verbatim. */
        val attentionError: String? = null,
        /** Per-[ActivityEvent.id] outcome of the last Replay click on that row — absent = never replayed this session. */
        val replayStatus: Map<String, ReplayStatus> = emptyMap(),
    ) : HomeState

    data class Error(val detail: String) : HomeState
}

/** One suggested first-run next step, shown on Home while the channel has no real configured content yet. */
data class FirstRunStep(
    val kind: FirstRunStepKind,
    /** A [bot.nomnomz.dashboard.feature.shell.nav.ShellRoute] name — navigated to on click. */
    val deepLinkRoute: String,
)

/** What the step suggests — the screen resolves each kind to its label/icon. */
enum class FirstRunStepKind {
    ConnectIntegration,
    CreateCommand,
    CreatePipeline,
}

/** The Replay-button outcome for one activity row (see [HomeController.replay]). */
sealed interface ReplayStatus {
    /** The replay request for this row is in flight — its button is disabled while this state holds. */
    data object InFlight : ReplayStatus

    /** The backend re-broadcast the captured payload to [widgetsNotified] currently-subscribed widgets. */
    data class Replayed(val widgetsNotified: Int) : ReplayStatus

    /** A real 404: nothing was captured for this event — distinct from any other failure, never a fake success. */
    data object NothingToReplay : ReplayStatus

    /** Any other failure — [message] is the backend's error text. */
    data class Failed(val message: String) : ReplayStatus
}
