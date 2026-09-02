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

import bot.nomnomz.dashboard.core.network.ActionRequiredItem
import bot.nomnomz.dashboard.core.network.ActivityEvent
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.CommandSummary
import bot.nomnomz.dashboard.core.network.CommandsApi
import bot.nomnomz.dashboard.core.network.CommunityApi
import bot.nomnomz.dashboard.core.network.CommunityMember
import bot.nomnomz.dashboard.core.network.CommunityStats
import bot.nomnomz.dashboard.core.network.CreateCommandBody
import bot.nomnomz.dashboard.core.network.Category
import bot.nomnomz.dashboard.core.network.ChannelSearchResult
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.DashboardApi
import bot.nomnomz.dashboard.core.network.DashboardStats
import bot.nomnomz.dashboard.core.network.IntegrationStatus
import bot.nomnomz.dashboard.core.network.IntegrationsApi
import bot.nomnomz.dashboard.core.network.ModerationApi
import bot.nomnomz.dashboard.core.network.ModerationQueueItem
import bot.nomnomz.dashboard.core.network.NotificationsApi
import bot.nomnomz.dashboard.core.network.ResolvedAutomodQueueItem
import bot.nomnomz.dashboard.core.network.PipelineSummary
import bot.nomnomz.dashboard.core.network.PipelinesApi
import bot.nomnomz.dashboard.core.network.ReplayResult
import bot.nomnomz.dashboard.core.network.StreamApi
import bot.nomnomz.dashboard.core.network.StreamInfo
import bot.nomnomz.dashboard.core.network.StreamInfoUpdate
import bot.nomnomz.dashboard.core.network.ViewerOption
import bot.nomnomz.dashboard.core.network.UpdateCommandBody
import bot.nomnomz.dashboard.core.realtime.HubEvent
import bot.nomnomz.dashboard.core.realtime.HubRewardRedeemed
import bot.nomnomz.dashboard.core.realtime.HubStreamInfoChanged
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.UnconfinedTestDispatcher
import kotlinx.coroutines.test.runTest

// Proves the Home page state machine the screen renders: resolve the active channel, then surface the live
// snapshot — or an error if either step fails. The screen is a pure projection of this, so testing it proves
// the page shows real data (no fabricated counts) and degrades cleanly.
@OptIn(ExperimentalCoroutinesApi::class)
class HomeControllerTest {

    @Test
    fun load_surfaces_the_live_channel_snapshot_on_success() = runTest {
        val controller =
            HomeController(
                channelsApi = FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                dashboardApi = FakeDashboardApi(
                    ApiResult.Ok(
                        DashboardStats(
                            isLive = true,
                            streamTitle = "Live now",
                            viewerCount = 42,
                            followerCount = 1000,
                            uptime = 3720,
                        )
                    )
                ),
                streamApi = FakeStreamApi(),
                commandsApi = FakeCommandsApi(),
                communityApi = FakeCommunityApi(),
                notificationsApi = FakeNotificationsApi(),
                pipelinesApi = FakePipelinesApi(),
                moderationApi = FakeModerationApi(),
                integrationsApi = FakeIntegrationsApi(),
            )

        controller.load()

        val state: HomeState = controller.state.value
        assertTrue(state is HomeState.Ready)
        val stats: DashboardStats = (state as HomeState.Ready).stats
        assertEquals(true, stats.isLive)
        assertEquals(42, stats.viewerCount)
        assertEquals(1000, stats.followerCount)
        assertEquals("Live now", stats.streamTitle)
        assertEquals(3720, stats.uptime)
    }

    @Test
    fun load_errors_when_no_channel_resolves() = runTest {
        val controller =
            HomeController(
                channelsApi = FakeChannelsApi(ApiResult.Failure(ApiError(404, "NO_CHANNEL", "none onboarded"))),
                dashboardApi = FakeDashboardApi(ApiResult.Ok(DashboardStats())),
                streamApi = FakeStreamApi(),
                commandsApi = FakeCommandsApi(),
                communityApi = FakeCommunityApi(),
                notificationsApi = FakeNotificationsApi(),
                pipelinesApi = FakePipelinesApi(),
                moderationApi = FakeModerationApi(),
                integrationsApi = FakeIntegrationsApi(),
            )

        controller.load()

        assertTrue(controller.state.value is HomeState.Error)
    }

    @Test
    fun load_errors_when_the_stats_call_fails() = runTest {
        val controller =
            HomeController(
                channelsApi = FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                dashboardApi = FakeDashboardApi(ApiResult.Failure(ApiError(500, "ERR", "boom"))),
                streamApi = FakeStreamApi(),
                commandsApi = FakeCommandsApi(),
                communityApi = FakeCommunityApi(),
                notificationsApi = FakeNotificationsApi(),
                pipelinesApi = FakePipelinesApi(),
                moderationApi = FakeModerationApi(),
                integrationsApi = FakeIntegrationsApi(),
            )

        controller.load()

        assertTrue(controller.state.value is HomeState.Error)
    }

    @Test
    fun load_surfaces_top_5_commands_sorted_by_use_count() = runTest {
        val commands: List<CommandSummary> = listOf(
            CommandSummary(name = "!c", useCount = 1),
            CommandSummary(name = "!a", useCount = 50),
            CommandSummary(name = "!b", useCount = 30),
            CommandSummary(name = "!d", useCount = 5),
            CommandSummary(name = "!e", useCount = 20),
            CommandSummary(name = "!f", useCount = 100),
        )
        val controller =
            HomeController(
                channelsApi = FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                dashboardApi = FakeDashboardApi(ApiResult.Ok(DashboardStats())),
                streamApi = FakeStreamApi(),
                commandsApi = FakeCommandsApi(ApiResult.Ok(commands)),
                communityApi = FakeCommunityApi(),
                notificationsApi = FakeNotificationsApi(),
                pipelinesApi = FakePipelinesApi(),
                moderationApi = FakeModerationApi(),
                integrationsApi = FakeIntegrationsApi(),
            )

        controller.load()

        val state: HomeState = controller.state.value
        assertTrue(state is HomeState.Ready)
        val top: List<CommandSummary> = (state as HomeState.Ready).topCommands
        assertEquals(5, top.size)
        assertEquals("!f", top[0].name)
        assertEquals("!a", top[1].name)
        assertEquals("!b", top[2].name)
        assertEquals("!e", top[3].name)
        assertEquals("!d", top[4].name)
    }

    @Test
    fun updateStreamInfo_merges_the_saved_title_into_the_banner_stats_and_stream_info() = runTest {
        // The regression: the PUT echoed the saved title, but only streamInfo was merged — the live banner
        // renders stats.streamTitle, so the old title stayed on screen until a full page reload.
        val controller =
            HomeController(
                channelsApi = FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                dashboardApi = FakeDashboardApi(
                    ApiResult.Ok(DashboardStats(streamTitle = "Old title", gameName = "Old game"))
                ),
                streamApi = FakeStreamApi(
                    updateResult = ApiResult.Ok(StreamInfo(title = "New title", gameName = "New game"))
                ),
                commandsApi = FakeCommandsApi(),
                communityApi = FakeCommunityApi(),
                notificationsApi = FakeNotificationsApi(),
                pipelinesApi = FakePipelinesApi(),
                moderationApi = FakeModerationApi(),
                integrationsApi = FakeIntegrationsApi(),
            )
        controller.load()

        controller.updateStreamInfo(title = "New title", gameName = "New game", tags = null)

        val state: HomeState = controller.state.value
        assertTrue(state is HomeState.Ready)
        val ready: HomeState.Ready = state as HomeState.Ready
        assertEquals("New title", ready.stats.streamTitle)
        assertEquals("New game", ready.stats.gameName)
        assertEquals("New title", ready.streamInfo?.title)
        assertNull(ready.streamError)
    }

    @Test
    fun hub_stream_info_changed_updates_the_banner_without_a_reload() = runTest {
        // The regression's second half: the hub pushed StreamInfoChanged on channel.update, but the client
        // dropped it (unmodelled target) — an edit by another operator or on Twitch itself never showed live.
        val controller =
            HomeController(
                channelsApi = FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                dashboardApi = FakeDashboardApi(
                    ApiResult.Ok(DashboardStats(streamTitle = "Old title", gameName = "Old game"))
                ),
                streamApi = FakeStreamApi(),
                commandsApi = FakeCommandsApi(),
                communityApi = FakeCommunityApi(),
                notificationsApi = FakeNotificationsApi(),
                pipelinesApi = FakePipelinesApi(),
                moderationApi = FakeModerationApi(),
                integrationsApi = FakeIntegrationsApi(),
            )
        controller.load()

        // Collect on an unconfined test dispatcher so the subscription is live immediately (see ChatControllerTest).
        val events = MutableSharedFlow<HubEvent>(extraBufferCapacity = 16)
        backgroundScope.launch(UnconfinedTestDispatcher(testScheduler)) { controller.subscribeToHub(events) }

        events.emit(
            HubEvent.StreamInfoChanged(
                HubStreamInfoChanged(
                    broadcasterId = "ch1",
                    broadcasterDisplayName = "Streamer",
                    title = "Pushed title",
                    gameName = "Pushed game",
                )
            )
        )

        val state: HomeState = controller.state.value
        assertTrue(state is HomeState.Ready)
        val ready: HomeState.Ready = state as HomeState.Ready
        assertEquals("Pushed title", ready.stats.streamTitle)
        assertEquals("Pushed game", ready.stats.gameName)
        assertEquals("Pushed title", ready.streamInfo?.title)
    }

    @Test
    fun a_redemption_push_appears_live_at_the_top_of_the_activity_feed() = runTest {
        // The reported bug: redeeming a channel-point reward did nothing until a manual reload. A redemption is
        // pushed as its OWN hub event (not a generic ChannelEvent), so it must be handled and prepended live.
        val controller =
            HomeController(
                channelsApi = FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                dashboardApi = FakeDashboardApi(ApiResult.Ok(DashboardStats())),
                streamApi = FakeStreamApi(),
                commandsApi = FakeCommandsApi(),
                communityApi = FakeCommunityApi(),
                notificationsApi = FakeNotificationsApi(),
                pipelinesApi = FakePipelinesApi(),
                moderationApi = FakeModerationApi(),
                integrationsApi = FakeIntegrationsApi(),
            )
        controller.load()

        val events = MutableSharedFlow<HubEvent>(extraBufferCapacity = 16)
        backgroundScope.launch(UnconfinedTestDispatcher(testScheduler)) { controller.subscribeToHub(events) }

        events.emit(
            HubEvent.RewardRedeemed(
                HubRewardRedeemed(
                    redemptionId = "r1",
                    rewardTitle = "Baguette",
                    userId = "u1",
                    userDisplayName = "Stoney_Eagle",
                    timestamp = "2026-07-20T15:18:00Z",
                )
            )
        )

        val ready: HomeState.Ready = controller.state.value as HomeState.Ready
        val first: ActivityEvent = ready.activity.first()
        assertEquals("r1", first.id)
        assertEquals("channel.channel_points_custom_reward_redemption.add", first.type)
        assertEquals("Stoney_Eagle", first.username)
    }

    @Test
    fun replay_calls_the_api_with_the_clicked_rows_exact_event_id_and_marks_it_replayed() = runTest {
        val dashboardApi = FakeDashboardApi(
            result = ApiResult.Ok(DashboardStats()),
            replayResult = ApiResult.Ok(ReplayResult(widgetsNotified = 3)),
        )
        val controller = HomeController(
            channelsApi = FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
            dashboardApi = dashboardApi,
            streamApi = FakeStreamApi(),
            commandsApi = FakeCommandsApi(),
            communityApi = FakeCommunityApi(),
            notificationsApi = FakeNotificationsApi(),
            pipelinesApi = FakePipelinesApi(),
            moderationApi = FakeModerationApi(),
            integrationsApi = FakeIntegrationsApi(),
        )
        controller.load()

        controller.replay("evt-target")

        assertEquals("ch1" to "evt-target", dashboardApi.lastReplayCall)
        val ready: HomeState.Ready = controller.state.value as HomeState.Ready
        val status: ReplayStatus? = ready.replayStatus["evt-target"]
        assertTrue(status is ReplayStatus.Replayed)
        assertEquals(3, (status as ReplayStatus.Replayed).widgetsNotified)
    }

    @Test
    fun replay_on_a_404_marks_the_row_nothing_to_replay_not_a_generic_failure() = runTest {
        val dashboardApi = FakeDashboardApi(
            result = ApiResult.Ok(DashboardStats()),
            replayResult = ApiResult.Failure(ApiError(404, "NOT_FOUND", "nothing captured")),
        )
        val controller = HomeController(
            channelsApi = FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
            dashboardApi = dashboardApi,
            streamApi = FakeStreamApi(),
            commandsApi = FakeCommandsApi(),
            communityApi = FakeCommunityApi(),
            notificationsApi = FakeNotificationsApi(),
            pipelinesApi = FakePipelinesApi(),
            moderationApi = FakeModerationApi(),
            integrationsApi = FakeIntegrationsApi(),
        )
        controller.load()

        controller.replay("evt-uncaptured")

        val ready: HomeState.Ready = controller.state.value as HomeState.Ready
        assertEquals(ReplayStatus.NothingToReplay, ready.replayStatus["evt-uncaptured"])
    }

    @Test
    fun replay_on_a_non_404_failure_marks_the_row_failed_distinctly_from_nothing_to_replay() = runTest {
        val dashboardApi = FakeDashboardApi(
            result = ApiResult.Ok(DashboardStats()),
            replayResult = ApiResult.Failure(ApiError(500, "ERR", "widget notifier unreachable")),
        )
        val controller = HomeController(
            channelsApi = FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
            dashboardApi = dashboardApi,
            streamApi = FakeStreamApi(),
            commandsApi = FakeCommandsApi(),
            communityApi = FakeCommunityApi(),
            notificationsApi = FakeNotificationsApi(),
            pipelinesApi = FakePipelinesApi(),
            moderationApi = FakeModerationApi(),
            integrationsApi = FakeIntegrationsApi(),
        )
        controller.load()

        controller.replay("evt-broken")

        val ready: HomeState.Ready = controller.state.value as HomeState.Ready
        val status: ReplayStatus? = ready.replayStatus["evt-broken"]
        assertTrue(status is ReplayStatus.Failed)
        assertEquals("widget notifier unreachable", (status as ReplayStatus.Failed).message)
    }

    @Test
    fun replay_disables_only_the_clicked_row_while_in_flight_leaving_other_rows_untouched() = runTest {
        // Prove per-event isolation: mark one row's prior outcome, then start a NEW in-flight replay on a
        // different row and confirm the first row's earlier outcome survives untouched (not wiped/overwritten).
        val dashboardApi = FakeDashboardApi(
            result = ApiResult.Ok(DashboardStats()),
            replayResult = ApiResult.Ok(ReplayResult(widgetsNotified = 1)),
        )
        val controller = HomeController(
            channelsApi = FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
            dashboardApi = dashboardApi,
            streamApi = FakeStreamApi(),
            commandsApi = FakeCommandsApi(),
            communityApi = FakeCommunityApi(),
            notificationsApi = FakeNotificationsApi(),
            pipelinesApi = FakePipelinesApi(),
            moderationApi = FakeModerationApi(),
            integrationsApi = FakeIntegrationsApi(),
        )
        controller.load()

        controller.replay("evt-first")
        val afterFirst: HomeState.Ready = controller.state.value as HomeState.Ready
        assertTrue(afterFirst.replayStatus["evt-first"] is ReplayStatus.Replayed)
        assertNull(afterFirst.replayStatus["evt-second"])

        controller.replay("evt-second")
        val afterSecond: HomeState.Ready = controller.state.value as HomeState.Ready
        // evt-first's outcome from its own completed call is unaffected by evt-second's run.
        assertTrue(afterSecond.replayStatus["evt-first"] is ReplayStatus.Replayed)
        assertTrue(afterSecond.replayStatus["evt-second"] is ReplayStatus.Replayed)
    }

    @Test
    fun load_survives_commands_api_failure_and_shows_empty_top_commands() = runTest {
        val controller =
            HomeController(
                channelsApi = FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                dashboardApi = FakeDashboardApi(ApiResult.Ok(DashboardStats())),
                streamApi = FakeStreamApi(),
                commandsApi = FakeCommandsApi(ApiResult.Failure(ApiError(500, "ERR", "commands unavailable"))),
                communityApi = FakeCommunityApi(),
                notificationsApi = FakeNotificationsApi(),
                pipelinesApi = FakePipelinesApi(),
                moderationApi = FakeModerationApi(),
                integrationsApi = FakeIntegrationsApi(),
            )

        controller.load()

        val state: HomeState = controller.state.value
        assertTrue(state is HomeState.Ready)
        assertTrue((state as HomeState.Ready).topCommands.isEmpty())
    }

    @Test
    fun load_surfaces_an_action_required_item_for_the_home_tile_to_render() = runTest {
        val item = ActionRequiredItem(
            kind = "integration_token_dead",
            severity = "critical",
            title = "Spotify token expired",
            message = "Reconnect Spotify to keep song requests working.",
            detectedAt = "2026-09-01T12:00:00Z",
            deepLinkRoute = "Integrations",
        )
        val controller =
            HomeController(
                channelsApi = FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                dashboardApi = FakeDashboardApi(ApiResult.Ok(DashboardStats())),
                streamApi = FakeStreamApi(),
                commandsApi = FakeCommandsApi(),
                communityApi = FakeCommunityApi(),
                notificationsApi = FakeNotificationsApi(ApiResult.Ok(listOf(item))),
                pipelinesApi = FakePipelinesApi(),
                moderationApi = FakeModerationApi(),
                integrationsApi = FakeIntegrationsApi(),
            )

        controller.load()

        val state: HomeState = controller.state.value
        assertTrue(state is HomeState.Ready)
        val actionRequired: List<ActionRequiredItem> = (state as HomeState.Ready).actionRequired
        assertEquals(1, actionRequired.size)
        assertEquals("Spotify token expired", actionRequired.first().title)
        assertEquals("critical", actionRequired.first().severity)
        assertEquals("Integrations", actionRequired.first().deepLinkRoute)
    }

    @Test
    fun load_surfaces_no_action_required_items_when_nothing_needs_attention() = runTest {
        val controller =
            HomeController(
                channelsApi = FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                dashboardApi = FakeDashboardApi(ApiResult.Ok(DashboardStats())),
                streamApi = FakeStreamApi(),
                commandsApi = FakeCommandsApi(),
                communityApi = FakeCommunityApi(),
                notificationsApi = FakeNotificationsApi(ApiResult.Ok(emptyList())),
                pipelinesApi = FakePipelinesApi(),
                moderationApi = FakeModerationApi(),
                integrationsApi = FakeIntegrationsApi(),
            )

        controller.load()

        val state: HomeState = controller.state.value
        assertTrue(state is HomeState.Ready)
        assertTrue((state as HomeState.Ready).actionRequired.isEmpty())
    }

    @Test
    fun load_survives_action_required_api_failure_and_shows_no_items() = runTest {
        val controller =
            HomeController(
                channelsApi = FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                dashboardApi = FakeDashboardApi(ApiResult.Ok(DashboardStats())),
                streamApi = FakeStreamApi(),
                commandsApi = FakeCommandsApi(),
                communityApi = FakeCommunityApi(),
                notificationsApi = FakeNotificationsApi(
                    ApiResult.Failure(ApiError(500, "ERR", "notifications unavailable"))
                ),
                pipelinesApi = FakePipelinesApi(),
                moderationApi = FakeModerationApi(),
                integrationsApi = FakeIntegrationsApi(),
            )

        controller.load()

        val state: HomeState = controller.state.value
        assertTrue(state is HomeState.Ready)
        assertTrue((state as HomeState.Ready).actionRequired.isEmpty())
    }

    @Test
    fun load_surfaces_real_recent_activity_content_in_the_feed_state() = runTest {
        // Proves actual event content reaches HomeState, not merely "no exception" — a follow AND a raid,
        // with their real fields, must both come through intact.
        val events: List<ActivityEvent> = listOf(
            ActivityEvent(id = "e1", type = "channel.follow", username = "QTkittE", timestamp = "2026-09-01T10:00:00Z"),
            ActivityEvent(id = "e2", type = "channel.raid", username = "BigStreamer", timestamp = "2026-09-01T09:00:00Z"),
        )
        val controller =
            HomeController(
                channelsApi = FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                dashboardApi = FakeDashboardApi(
                    result = ApiResult.Ok(DashboardStats()),
                    activityResult = ApiResult.Ok(events),
                ),
                streamApi = FakeStreamApi(),
                commandsApi = FakeCommandsApi(),
                communityApi = FakeCommunityApi(),
                notificationsApi = FakeNotificationsApi(),
                pipelinesApi = FakePipelinesApi(),
                moderationApi = FakeModerationApi(),
                integrationsApi = FakeIntegrationsApi(),
            )

        controller.load()

        val ready: HomeState.Ready = controller.state.value as HomeState.Ready
        assertEquals(2, ready.activity.size)
        assertEquals("channel.follow", ready.activity[0].type)
        assertEquals("QTkittE", ready.activity[0].username)
        assertEquals("channel.raid", ready.activity[1].type)
        assertEquals("BigStreamer", ready.activity[1].username)
    }

    @Test
    fun load_surfaces_the_first_run_checklist_when_the_channel_has_no_commands_pipelines_or_integrations() = runTest {
        val controller =
            HomeController(
                channelsApi = FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                dashboardApi = FakeDashboardApi(ApiResult.Ok(DashboardStats())),
                streamApi = FakeStreamApi(),
                commandsApi = FakeCommandsApi(ApiResult.Ok(emptyList())),
                communityApi = FakeCommunityApi(),
                notificationsApi = FakeNotificationsApi(),
                pipelinesApi = FakePipelinesApi(ApiResult.Ok(emptyList())),
                moderationApi = FakeModerationApi(),
                integrationsApi = FakeIntegrationsApi(ApiResult.Ok(emptyList())),
            )

        controller.load()

        val ready: HomeState.Ready = controller.state.value as HomeState.Ready
        assertEquals(3, ready.firstRunSteps.size)
        assertEquals(FirstRunStepKind.ConnectIntegration, ready.firstRunSteps[0].kind)
        assertEquals(FirstRunStepKind.CreateCommand, ready.firstRunSteps[1].kind)
        assertEquals(FirstRunStepKind.CreatePipeline, ready.firstRunSteps[2].kind)
    }

    @Test
    fun load_hides_the_first_run_checklist_once_the_channel_has_real_commands() = runTest {
        val controller =
            HomeController(
                channelsApi = FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                dashboardApi = FakeDashboardApi(ApiResult.Ok(DashboardStats())),
                streamApi = FakeStreamApi(),
                commandsApi = FakeCommandsApi(ApiResult.Ok(listOf(CommandSummary(name = "!hello")))),
                communityApi = FakeCommunityApi(),
                notificationsApi = FakeNotificationsApi(),
                pipelinesApi = FakePipelinesApi(ApiResult.Ok(emptyList())),
                moderationApi = FakeModerationApi(),
                integrationsApi = FakeIntegrationsApi(ApiResult.Ok(emptyList())),
            )

        controller.load()

        val ready: HomeState.Ready = controller.state.value as HomeState.Ready
        assertTrue(ready.firstRunSteps.isEmpty())
    }

    @Test
    fun load_hides_the_first_run_checklist_once_an_integration_is_connected() = runTest {
        val controller =
            HomeController(
                channelsApi = FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                dashboardApi = FakeDashboardApi(ApiResult.Ok(DashboardStats())),
                streamApi = FakeStreamApi(),
                commandsApi = FakeCommandsApi(ApiResult.Ok(emptyList())),
                communityApi = FakeCommunityApi(),
                notificationsApi = FakeNotificationsApi(),
                pipelinesApi = FakePipelinesApi(ApiResult.Ok(emptyList())),
                moderationApi = FakeModerationApi(),
                integrationsApi = FakeIntegrationsApi(
                    ApiResult.Ok(listOf(IntegrationStatus(provider = "spotify", connected = true)))
                ),
            )

        controller.load()

        val ready: HomeState.Ready = controller.state.value as HomeState.Ready
        assertTrue(ready.firstRunSteps.isEmpty())
    }

    // ─── Attention inbox (S-OWN22 Task 4) ─────────────────────────────────────

    @Test
    fun openHeldReview_loads_the_pending_queue_rows_for_exactly_the_items_queue_ids() = runTest {
        val moderationApi = FakeModerationApi()
        moderationApi.automodQueueResult = ApiResult.Ok(
            listOf(
                ModerationQueueItem(
                    id = "q1",
                    status = "pending",
                    messageContentSnapshot = "buy followers at spam.example",
                    autoModCategory = "spam",
                    createdAt = "2026-09-02T10:00:00Z",
                ),
                ModerationQueueItem(id = "q2", status = "pending", messageContentSnapshot = "second message"),
                ModerationQueueItem(id = "q-other", status = "pending", messageContentSnapshot = "someone else"),
            )
        )
        val controller = attentionController(moderationApi = moderationApi)
        controller.load()

        controller.openHeldReview(heldItem(queueItemIds = listOf("q1", "q2")))

        val review: HeldReviewState.Ready = controller.heldReview.value as HeldReviewState.Ready
        assertEquals(2, review.messages.size)
        assertEquals("q1", review.messages[0].id)
        assertEquals("buy followers at spam.example", review.messages[0].messageContentSnapshot)
        assertEquals("spam", review.messages[0].autoModCategory)
        assertEquals("q2", review.messages[1].id)
    }

    @Test
    fun resolveHeldMessage_allow_sends_approve_and_removes_the_single_message_item() = runTest {
        val moderationApi = FakeModerationApi()
        moderationApi.automodQueueResult =
            ApiResult.Ok(listOf(ModerationQueueItem(id = "q1", status = "pending")))
        val notificationsApi = FakeNotificationsApi(ApiResult.Ok(listOf(heldItem(queueItemIds = listOf("q1")))))
        val controller = attentionController(notificationsApi = notificationsApi, moderationApi = moderationApi)
        controller.load()
        controller.openHeldReview(heldItem(queueItemIds = listOf("q1")))

        controller.resolveHeldMessage("q1", "approve")

        assertEquals(
            listOf(ResolveCall("q1", "approve", null, null, null)),
            moderationApi.resolveCalls,
        )
        assertTrue((controller.state.value as HomeState.Ready).actionRequired.isEmpty())
        assertNull(controller.heldReview.value)
    }

    @Test
    fun resolveHeldMessage_block_sends_deny() = runTest {
        val moderationApi = FakeModerationApi()
        moderationApi.automodQueueResult =
            ApiResult.Ok(listOf(ModerationQueueItem(id = "q1", status = "pending")))
        val controller = attentionController(moderationApi = moderationApi)
        controller.load()
        controller.openHeldReview(heldItem(queueItemIds = listOf("q1")))

        controller.resolveHeldMessage("q1", "deny")

        assertEquals(listOf(ResolveCall("q1", "deny", null, null, null)), moderationApi.resolveCalls)
    }

    @Test
    fun resolveHeldMessage_timeout_sends_deny_with_the_timeout_follow_up_and_seconds() = runTest {
        val moderationApi = FakeModerationApi()
        moderationApi.automodQueueResult =
            ApiResult.Ok(listOf(ModerationQueueItem(id = "q1", status = "pending")))
        val controller = attentionController(moderationApi = moderationApi)
        controller.load()
        controller.openHeldReview(heldItem(queueItemIds = listOf("q1")))

        controller.resolveHeldMessage("q1", "deny", followUp = "timeout", timeoutSeconds = 600)

        assertEquals(listOf(ResolveCall("q1", "deny", "timeout", 600, null)), moderationApi.resolveCalls)
    }

    @Test
    fun resolveHeldMessage_ban_sends_deny_with_the_ban_follow_up_and_reason() = runTest {
        val moderationApi = FakeModerationApi()
        moderationApi.automodQueueResult =
            ApiResult.Ok(listOf(ModerationQueueItem(id = "q1", status = "pending")))
        val controller = attentionController(moderationApi = moderationApi)
        controller.load()
        controller.openHeldReview(heldItem(queueItemIds = listOf("q1")))

        controller.resolveHeldMessage("q1", "deny", followUp = "ban", reason = "spam bot")

        assertEquals(listOf(ResolveCall("q1", "deny", "ban", null, "spam bot")), moderationApi.resolveCalls)
    }

    @Test
    fun resolveHeldMessage_follow_up_failure_keeps_the_item_visible_with_the_verbatim_error() = runTest {
        val moderationApi = FakeModerationApi()
        moderationApi.automodQueueResult =
            ApiResult.Ok(listOf(ModerationQueueItem(id = "q1", status = "pending")))
        moderationApi.resolveResult = ApiResult.Ok(
            ResolvedAutomodQueueItem(
                item = ModerationQueueItem(id = "q1", status = "resolved"),
                followUpError = "message blocked, but the ban failed: missing scope",
            )
        )
        val notificationsApi = FakeNotificationsApi(ApiResult.Ok(listOf(heldItem(queueItemIds = listOf("q1")))))
        val controller = attentionController(notificationsApi = notificationsApi, moderationApi = moderationApi)
        controller.load()
        controller.openHeldReview(heldItem(queueItemIds = listOf("q1")))

        controller.resolveHeldMessage("q1", "deny", followUp = "ban")

        // The partial outcome is NOT a clean success: the item stays visible and the error shows verbatim.
        assertEquals(1, (controller.state.value as HomeState.Ready).actionRequired.size)
        val review: HeldReviewState.Ready = controller.heldReview.value as HeldReviewState.Ready
        assertEquals("message blocked, but the ban failed: missing scope", review.actionError)
        assertEquals(1, review.messages.size)
    }

    @Test
    fun resolveHeldMessage_failure_keeps_everything_and_surfaces_the_backend_error_verbatim() = runTest {
        val moderationApi = FakeModerationApi()
        moderationApi.automodQueueResult =
            ApiResult.Ok(listOf(ModerationQueueItem(id = "q1", status = "pending")))
        moderationApi.resolveResult = ApiResult.Failure(ApiError(409, "CONFLICT", "already resolved by ModBot"))
        val notificationsApi = FakeNotificationsApi(ApiResult.Ok(listOf(heldItem(queueItemIds = listOf("q1")))))
        val controller = attentionController(notificationsApi = notificationsApi, moderationApi = moderationApi)
        controller.load()
        controller.openHeldReview(heldItem(queueItemIds = listOf("q1")))

        controller.resolveHeldMessage("q1", "approve")

        assertEquals(1, (controller.state.value as HomeState.Ready).actionRequired.size)
        val review: HeldReviewState.Ready = controller.heldReview.value as HeldReviewState.Ready
        assertEquals("already resolved by ModBot", review.actionError)
    }

    @Test
    fun resolveAllHeldMessages_resolves_every_message_and_removes_the_group() = runTest {
        val moderationApi = FakeModerationApi()
        moderationApi.automodQueueResult = ApiResult.Ok(
            listOf(
                ModerationQueueItem(id = "q1", status = "pending"),
                ModerationQueueItem(id = "q2", status = "pending"),
            )
        )
        val group: ActionRequiredItem = heldItem(id = "held-user:u9", queueItemIds = listOf("q1", "q2"), count = 2)
        val notificationsApi = FakeNotificationsApi(ApiResult.Ok(listOf(group)))
        val controller = attentionController(notificationsApi = notificationsApi, moderationApi = moderationApi)
        controller.load()
        controller.openHeldReview(group)

        controller.resolveAllHeldMessages("deny", followUp = "ban", reason = "spam wave")

        assertEquals(
            listOf(
                ResolveCall("q1", "deny", "ban", null, "spam wave"),
                ResolveCall("q2", "deny", "ban", null, "spam wave"),
            ),
            moderationApi.resolveCalls,
        )
        assertTrue((controller.state.value as HomeState.Ready).actionRequired.isEmpty())
        assertNull(controller.heldReview.value)
    }

    @Test
    fun blockTermFromHeldMessage_sends_the_message_text_to_the_blocked_terms_endpoint() = runTest {
        val moderationApi = FakeModerationApi()
        moderationApi.automodQueueResult =
            ApiResult.Ok(listOf(ModerationQueueItem(id = "q1", status = "pending")))
        val controller = attentionController(moderationApi = moderationApi)
        controller.load()
        controller.openHeldReview(heldItem(queueItemIds = listOf("q1")))

        controller.blockTermFromHeldMessage("buy followers at spam.example")

        assertEquals(listOf("ch1" to "buy followers at spam.example"), moderationApi.blockedTerms)
        val review: HeldReviewState.Ready = controller.heldReview.value as HeldReviewState.Ready
        assertEquals("buy followers at spam.example", review.blockedTerm)
    }

    @Test
    fun dismissAttentionItem_removes_the_item_and_it_stays_gone_after_a_reload() = runTest {
        val item: ActionRequiredItem = tokenItem(id = "token:conn1:123")
        val notificationsApi = FakeNotificationsApi(ApiResult.Ok(listOf(item)))
        val controller = attentionController(notificationsApi = notificationsApi)
        controller.load()
        assertEquals(1, (controller.state.value as HomeState.Ready).actionRequired.size)

        controller.dismissAttentionItem(item)

        assertEquals(listOf(listOf("token:conn1:123")), notificationsApi.dismissedIds)
        assertTrue((controller.state.value as HomeState.Ready).actionRequired.isEmpty())

        // A reload re-fetches — the backend excludes the dismissed key, so the item stays gone.
        notificationsApi.result = ApiResult.Ok(emptyList())
        controller.load()
        assertTrue((controller.state.value as HomeState.Ready).actionRequired.isEmpty())
    }

    @Test
    fun dismissAttentionItem_failure_keeps_the_item_with_the_backend_error() = runTest {
        val item: ActionRequiredItem = tokenItem(id = "token:conn1:123")
        val notificationsApi = FakeNotificationsApi(
            result = ApiResult.Ok(listOf(item)),
            dismissResult = ApiResult.Failure(ApiError(403, "FORBIDDEN", "below the dismiss floor")),
        )
        val controller = attentionController(notificationsApi = notificationsApi)
        controller.load()

        controller.dismissAttentionItem(item)

        val ready: HomeState.Ready = controller.state.value as HomeState.Ready
        assertEquals(1, ready.actionRequired.size)
        assertEquals("below the dismiss floor", ready.attentionError)
    }

    @Test
    fun attention_severity_maps_three_ways_not_binarised() {
        assertEquals(AttentionSeverity.Critical, attentionSeverityFor("critical"))
        assertEquals(AttentionSeverity.Warning, attentionSeverityFor("warning"))
        assertEquals(AttentionSeverity.Info, attentionSeverityFor("info"))
    }

    @Test
    fun attention_kind_maps_to_the_shell_route_names() {
        assertEquals("Moderation", attentionRouteFor("held_chat_message"))
        assertEquals("Integrations", attentionRouteFor("integration_token_dead"))
        assertNull(attentionRouteFor("some_future_kind"))
    }

    // ─── Attention-inbox test helpers ─────────────────────────────────────────

    private fun attentionController(
        notificationsApi: FakeNotificationsApi = FakeNotificationsApi(),
        moderationApi: FakeModerationApi = FakeModerationApi(),
    ): HomeController =
        HomeController(
            channelsApi = FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
            dashboardApi = FakeDashboardApi(ApiResult.Ok(DashboardStats())),
            streamApi = FakeStreamApi(),
            commandsApi = FakeCommandsApi(),
            communityApi = FakeCommunityApi(),
            notificationsApi = notificationsApi,
            pipelinesApi = FakePipelinesApi(),
            moderationApi = moderationApi,
            integrationsApi = FakeIntegrationsApi(),
        )

    private fun heldItem(
        id: String = "held:q1",
        queueItemIds: List<String>,
        count: Int = queueItemIds.size,
    ): ActionRequiredItem =
        ActionRequiredItem(
            kind = "held_chat_message",
            severity = "warning",
            title = "$count messages from spammy held for review",
            message = "AutoMod is holding messages from spammy.",
            detectedAt = "2026-09-02T10:00:00Z",
            deepLinkRoute = "/moderation/queue",
            id = id,
            sourceUserId = "u9",
            sourceUserName = "spammy",
            count = count,
            queueItemIds = queueItemIds,
        )

    private fun tokenItem(id: String): ActionRequiredItem =
        ActionRequiredItem(
            kind = "integration_token_dead",
            severity = "critical",
            title = "Spotify token expired",
            message = "Reconnect Spotify to keep song requests working.",
            detectedAt = "2026-09-01T12:00:00Z",
            deepLinkRoute = "/settings/integrations",
            id = id,
        )
}

private class FakeChannelsApi(private val result: ApiResult<ChannelSummary>) : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = result

    override suspend fun list(): ApiResult<List<ChannelSummary>> = ApiResult.Ok(emptyList())

    override suspend fun join(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun leave(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun reset(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun deleteChannel(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun channelScopes(channelId: String) = error("stub")
    override suspend fun startChannelBotConnect(channelId: String) = error("stub")
    override suspend fun channelBotStatus(channelId: String) = error("stub")
    override suspend fun disconnectChannelBot(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun moderatedChannels(): ApiResult<List<ModeratedChannel>> = ApiResult.Ok(emptyList())
}

private class FakeDashboardApi(
    private val result: ApiResult<DashboardStats>,
    private val replayResult: ApiResult<ReplayResult> = ApiResult.Ok(ReplayResult(widgetsNotified = 1)),
    private val activityResult: ApiResult<List<ActivityEvent>> = ApiResult.Ok(emptyList()),
) : DashboardApi {
    /** The (channelId, eventId) pair of the last [replay] call — asserted against to prove the right row fired. */
    var lastReplayCall: Pair<String, String>? = null
        private set

    override suspend fun stats(channelId: String): ApiResult<DashboardStats> = result
    override suspend fun activity(channelId: String): ApiResult<List<ActivityEvent>> = activityResult

    override suspend fun replay(channelId: String, eventId: String): ApiResult<ReplayResult> {
        lastReplayCall = channelId to eventId
        return replayResult
    }
}

private class FakePipelinesApi(
    private val result: ApiResult<List<PipelineSummary>> = ApiResult.Ok(emptyList()),
) : PipelinesApi {
    override suspend fun list(channelId: String): ApiResult<List<PipelineSummary>> = result
    override suspend fun catalogue(channelId: String) = error("stub")
    override suspend fun get(channelId: String, id: String) = error("stub")
    override suspend fun create(channelId: String, body: bot.nomnomz.dashboard.core.network.CreatePipelineBody) =
        error("stub")
    override suspend fun createReturning(
        channelId: String,
        body: bot.nomnomz.dashboard.core.network.CreatePipelineBody,
    ) = error("stub")
    override suspend fun update(
        channelId: String,
        id: String,
        body: bot.nomnomz.dashboard.core.network.UpdatePipelineBody,
    ) = error("stub")
    override suspend fun delete(channelId: String, id: String) = error("stub")
    override suspend fun blastRadius(channelId: String, id: String) = error("stub")
    override suspend fun testRun(
        channelId: String,
        id: String,
        body: bot.nomnomz.dashboard.core.network.PipelineTestRunBody,
    ) = error("stub")
}

private class FakeIntegrationsApi(
    private val result: ApiResult<List<IntegrationStatus>> = ApiResult.Ok(emptyList()),
) : IntegrationsApi {
    override suspend fun status(channelId: String): ApiResult<List<IntegrationStatus>> = result
    override suspend fun startGenericConnect(
        channelId: String,
        provider: String,
        scopeSetKey: String,
        returnUrl: String?,
    ) = error("stub")
    override fun discordStartUrl(baseUrl: String, channelId: String): String = error("stub")
    override suspend fun disconnectGeneric(channelId: String, provider: String) = error("stub")
    override suspend fun disconnectBlastRadius(channelId: String, provider: String) = error("stub")
    override suspend fun disconnectDiscord(channelId: String) = error("stub")
    override suspend fun spotifyCredentials(channelId: String) = error("stub")
    override suspend fun saveSpotifyCredentials(channelId: String, clientId: String, clientSecret: String) =
        error("stub")
    override suspend fun clearSpotifyCredentials(channelId: String) = error("stub")
}

private class FakeStreamApi(
    private val infoResult: ApiResult<StreamInfo> = ApiResult.Ok(StreamInfo()),
    private val updateResult: ApiResult<StreamInfo> = ApiResult.Ok(StreamInfo()),
) : StreamApi {
    override suspend fun info(channelId: String): ApiResult<StreamInfo> = infoResult
    override suspend fun update(channelId: String, update: StreamInfoUpdate): ApiResult<StreamInfo> =
        updateResult
    override suspend fun searchCategories(channelId: String, query: String): ApiResult<List<Category>> =
        ApiResult.Ok(emptyList())

    override suspend fun searchChannels(channelId: String, query: String): ApiResult<List<ChannelSearchResult>> =
        ApiResult.Ok(emptyList())
}

private class FakeCommunityApi : CommunityApi {
    override suspend fun searchViewers(
        channelId: String,
        query: String,
        limit: Int,
    ): ApiResult<List<ViewerOption>> = ApiResult.Ok(emptyList())

    override suspend fun members(channelId: String) = error("stub")
    override suspend fun membersPage(
        channelId: String,
        role: String?,
        page: Int,
        pageSize: Int,
        cursor: String?,
    ) = error("stub")
    override suspend fun topChatters(channelId: String) = error("stub")
    override suspend fun setTrust(channelId: String, userId: String, level: String) = error("stub")
    override suspend fun ban(channelId: String, userId: String, reason: String) = error("stub")
    override suspend fun unban(channelId: String, userId: String) = error("stub")
    override suspend fun addVip(channelId: String, userId: String) = error("stub")
    override suspend fun removeVip(channelId: String, userId: String) = error("stub")
    override suspend fun shoutout(channelId: String, targetTwitchUserId: String) = error("stub")
    override suspend fun stats(channelId: String) = ApiResult.Ok(CommunityStats())

    override suspend fun member(channelId: String, userId: String) =
        ApiResult.Ok(CommunityMember(id = userId))
}

private class FakeNotificationsApi(
    /** Mutable so a test can change what the NEXT load returns (a reload after a dismissal, where the
     * backend excludes the dismissed key). */
    var result: ApiResult<List<ActionRequiredItem>> = ApiResult.Ok(emptyList()),
    var dismissResult: ApiResult<Unit> = ApiResult.Ok(Unit),
) : NotificationsApi {
    /** Every dismiss call's id list — asserted against to prove the right item keys were sent. */
    val dismissedIds: MutableList<List<String>> = mutableListOf()

    override suspend fun actionRequired(channelId: String): ApiResult<List<ActionRequiredItem>> = result

    override suspend fun dismissActionRequired(channelId: String, ids: List<String>): ApiResult<Unit> {
        dismissedIds.add(ids)
        return dismissResult
    }
}

private class FakeCommandsApi(
    private val result: ApiResult<List<CommandSummary>> = ApiResult.Ok(emptyList()),
) : CommandsApi {
    override suspend fun list(channelId: String): ApiResult<List<CommandSummary>> = result
    override suspend fun create(channelId: String, body: CreateCommandBody): ApiResult<Unit> =
        ApiResult.Ok(Unit)
    override suspend fun update(
        channelId: String,
        commandName: String,
        body: UpdateCommandBody,
    ): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun delete(channelId: String, commandName: String): ApiResult<Unit> =
        ApiResult.Ok(Unit)
}

/** One recorded resolve call — asserted against to prove the exact wire payload each action sends. */
private data class ResolveCall(
    val queueItemId: String,
    val action: String,
    val followUp: String?,
    val timeoutSeconds: Int?,
    val reason: String?,
)

// Only the surface the Home attention inbox touches is real (queue read, resolve, blocked terms, user
// context, automod config); everything else stubs out like the other fakes in this file.
private class FakeModerationApi : ModerationApi {
    var automodQueueResult: ApiResult<List<ModerationQueueItem>> = ApiResult.Ok(emptyList())
    var resolveResult: ApiResult<ResolvedAutomodQueueItem> =
        ApiResult.Ok(ResolvedAutomodQueueItem(item = ModerationQueueItem()))
    var addBlockedTermResult: ApiResult<Unit> = ApiResult.Ok(Unit)

    /** Every resolve call, in order — the exact wire payload each action sent. */
    val resolveCalls: MutableList<ResolveCall> = mutableListOf()

    /** Every (channelId, term) pair sent to the blocked-terms endpoint. */
    val blockedTerms: MutableList<Pair<String, String>> = mutableListOf()

    override suspend fun automodQueue(
        channelId: String,
        status: String?,
    ): ApiResult<List<ModerationQueueItem>> = automodQueueResult

    override suspend fun resolveAutomodQueueItem(
        channelId: String,
        queueItemId: String,
        action: String,
        followUp: String?,
        timeoutSeconds: Int?,
        reason: String?,
    ): ApiResult<ResolvedAutomodQueueItem> {
        resolveCalls.add(ResolveCall(queueItemId, action, followUp, timeoutSeconds, reason))
        return resolveResult
    }

    override suspend fun addBlockedTerm(channelId: String, term: String): ApiResult<Unit> {
        blockedTerms.add(channelId to term)
        return addBlockedTermResult
    }

    // Enrichment reads the dialog folds to null/default on failure — failing here proves that fold.
    override suspend fun userContext(channelId: String, userId: String) =
        ApiResult.Failure(ApiError(404, "NOT_FOUND", "no context"))

    override suspend fun automod(channelId: String) =
        ApiResult.Failure(ApiError(403, "FORBIDDEN", "no automod read"))

    override suspend fun bans(channelId: String) = error("stub")
    override suspend fun unban(channelId: String, userId: String) = error("stub")
    override suspend fun modLog(channelId: String) = error("stub")
    override suspend fun shieldMode(channelId: String) = error("stub")
    override suspend fun setShieldMode(channelId: String, enabled: Boolean) = error("stub")
    override suspend fun blockedTerms(channelId: String) = error("stub")
    override suspend fun removeBlockedTerm(channelId: String, term: String) = error("stub")
    override suspend fun saveAutomod(
        channelId: String,
        config: bot.nomnomz.dashboard.core.network.AutomodConfig,
    ) = error("stub")
    override suspend fun rules(channelId: String) = error("stub")
    override suspend fun createRule(
        channelId: String,
        body: bot.nomnomz.dashboard.core.network.CreateModerationRuleBody,
    ) = error("stub")
    override suspend fun setRuleEnabled(channelId: String, ruleId: Int, enabled: Boolean) = error("stub")
    override suspend fun deleteRule(channelId: String, ruleId: Int) = error("stub")
    override suspend fun performAction(
        channelId: String,
        action: String,
        targetUserId: String,
        durationSeconds: Int?,
        reason: String?,
    ) = error("stub")
    override suspend fun stats(channelId: String) = error("stub")
    override suspend fun shoutoutTemplate(channelId: String) = error("stub")
    override suspend fun setShoutoutTemplate(channelId: String, template: String?) = error("stub")
    override suspend fun shoutoutOverrides(channelId: String) = error("stub")
    override suspend fun setShoutoutOverride(
        channelId: String,
        targetTwitchUserId: String,
        targetDisplayName: String,
        messageTemplate: String,
    ) = error("stub")
    override suspend fun deleteShoutoutOverride(channelId: String, targetTwitchUserId: String) = error("stub")
    override suspend fun notesFor(channelId: String, userId: String) = error("stub")
    override suspend fun createNote(
        channelId: String,
        userId: String,
        content: String,
        pinned: Boolean,
    ) = error("stub")
    override suspend fun updateNote(
        channelId: String,
        noteId: String,
        content: String?,
        pinned: Boolean?,
    ) = error("stub")
    override suspend fun deleteNote(channelId: String, noteId: String) = error("stub")
    override suspend fun announce(channelId: String, message: String, color: String?) = error("stub")
    override suspend fun warn(channelId: String, userId: String, reason: String) = error("stub")
    override suspend fun setSuspicious(channelId: String, userId: String, status: String) = error("stub")
    override suspend fun clearSuspicious(channelId: String, userId: String) = error("stub")
    override suspend fun unbanRequests(channelId: String) = error("stub")
    override suspend fun resolveUnbanRequest(
        channelId: String,
        requestId: String,
        approve: Boolean,
        note: String?,
    ) = error("stub")
    override suspend fun networkUnban(
        channelId: String,
        targetTwitchUserId: String,
        scope: String,
    ) = error("stub")
    override suspend fun reports(channelId: String) = error("stub")
    override suspend fun resolveReport(channelId: String, reportId: String, action: String) = error("stub")
    override suspend fun escalationPolicy(channelId: String) = error("stub")
    override suspend fun saveEscalationPolicy(
        channelId: String,
        body: bot.nomnomz.dashboard.core.network.UpsertEscalationPolicyBody,
    ) = error("stub")
    override suspend fun resetEscalation(channelId: String, userId: String) = error("stub")
    override suspend fun nukeBatches(channelId: String) = error("stub")
    override suspend fun networkNuke(
        channelId: String,
        body: bot.nomnomz.dashboard.core.network.NetworkNukeBody,
    ) = error("stub")
    override suspend fun revertNuke(channelId: String, batchId: String) = error("stub")
    override suspend fun sharedBanSettings(channelId: String) = error("stub")
    override suspend fun saveSharedBanSettings(
        channelId: String,
        body: bot.nomnomz.dashboard.core.network.SaveSharedBanSettingsBody,
    ) = error("stub")
    override suspend fun addTrustedChannel(channelId: String, trustedChannelId: String) = error("stub")
    override suspend fun removeTrustedChannel(channelId: String, trustedChannelId: String) = error("stub")
    override suspend fun setStanding(
        channelId: String,
        userId: String,
        body: bot.nomnomz.dashboard.core.network.SetModerationStandingBody,
    ) = error("stub")
    override suspend fun clearStanding(channelId: String, userId: String, provider: String) = error("stub")
    override suspend fun chatFilters(channelId: String) = error("stub")
    override suspend fun createChatFilter(
        channelId: String,
        body: bot.nomnomz.dashboard.core.network.CreateChatFilterBody,
    ) = error("stub")
    override suspend fun updateChatFilter(
        channelId: String,
        filterId: String,
        body: bot.nomnomz.dashboard.core.network.UpdateChatFilterBody,
    ) = error("stub")
    override suspend fun deleteChatFilter(channelId: String, filterId: String) = error("stub")
    override suspend fun moderators(channelId: String) = error("stub")
    override suspend fun addModerator(channelId: String, targetTwitchUserId: String) = error("stub")
    override suspend fun removeModerator(channelId: String, userId: String) = error("stub")
    override suspend fun clearChat(channelId: String) = error("stub")
}
