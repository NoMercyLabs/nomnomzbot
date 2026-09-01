// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.home.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.layout.Spacer
import bot.nomnomz.dashboard.core.designsystem.component.ActionErrorBanner
import bot.nomnomz.dashboard.core.designsystem.resolveRowLabel
import bot.nomnomz.dashboard.core.designsystem.component.AlertDialog
import bot.nomnomz.dashboard.core.designsystem.component.Badge
import bot.nomnomz.dashboard.core.designsystem.component.BadgeVariant
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.GlyphButton
import bot.nomnomz.dashboard.core.designsystem.component.Slider
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import bot.nomnomz.dashboard.core.media.EmojiText
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import org.jetbrains.compose.resources.DrawableResource
import org.jetbrains.compose.resources.painterResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.component.PickerOption
import bot.nomnomz.dashboard.core.designsystem.component.PickerRef
import bot.nomnomz.dashboard.core.designsystem.component.SearchPickerField
import bot.nomnomz.dashboard.core.designsystem.component.Tooltip
import bot.nomnomz.dashboard.core.designsystem.component.TabsList
import bot.nomnomz.dashboard.core.designsystem.component.TabsTrigger
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecisionForAction
import bot.nomnomz.dashboard.core.designsystem.icon.AddGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.AppIcons
import bot.nomnomz.dashboard.core.designsystem.icon.CheckCircleGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.EditGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.RefreshGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.RemoveGlyph
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.ActionRequiredItem
import bot.nomnomz.dashboard.core.network.ActivityEvent
import bot.nomnomz.dashboard.feature.home.state.FirstRunStep
import bot.nomnomz.dashboard.feature.home.state.FirstRunStepKind
import bot.nomnomz.dashboard.core.network.CommandSummary
import bot.nomnomz.dashboard.core.network.DashboardStats
import bot.nomnomz.dashboard.core.network.LiveOpsClipStub
import bot.nomnomz.dashboard.core.network.LiveOpsMarker
import bot.nomnomz.dashboard.core.network.LiveOpsPoll
import bot.nomnomz.dashboard.core.network.LiveOpsPrediction
import bot.nomnomz.dashboard.core.network.StreamInfo
import bot.nomnomz.dashboard.core.realtime.HubEvent
import bot.nomnomz.dashboard.feature.home.state.HomeController
import bot.nomnomz.dashboard.feature.home.state.HomeState
import bot.nomnomz.dashboard.feature.home.state.ReplayStatus
import bot.nomnomz.dashboard.feature.chatpolls.state.ChatPollsController
import bot.nomnomz.dashboard.feature.chatpolls.ui.ChatPollsCard
import bot.nomnomz.dashboard.feature.liveops.state.LiveOpsController
import bot.nomnomz.dashboard.feature.liveops.state.LiveOpsState
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.category_picker_empty
import nomnomzbot.composeapp.generated.resources.category_picker_label
import nomnomzbot.composeapp.generated.resources.category_picker_placeholder
import nomnomzbot.composeapp.generated.resources.channel_picker_empty
import nomnomzbot.composeapp.generated.resources.channel_picker_label
import nomnomzbot.composeapp.generated.resources.channel_picker_placeholder
import nomnomzbot.composeapp.generated.resources.home_action_required_section
import nomnomzbot.composeapp.generated.resources.home_action_required_severity_critical
import nomnomzbot.composeapp.generated.resources.home_action_required_severity_warning
import nomnomzbot.composeapp.generated.resources.home_activity_ban
import nomnomzbot.composeapp.generated.resources.home_activity_cheer
import nomnomzbot.composeapp.generated.resources.home_activity_empty
import nomnomzbot.composeapp.generated.resources.home_activity_event
import nomnomzbot.composeapp.generated.resources.home_activity_follow
import nomnomzbot.composeapp.generated.resources.home_activity_mod_add
import nomnomzbot.composeapp.generated.resources.home_activity_mod_remove
import nomnomzbot.composeapp.generated.resources.home_activity_cheer_with_bits
import nomnomzbot.composeapp.generated.resources.home_activity_duration_days
import nomnomzbot.composeapp.generated.resources.home_activity_duration_hours
import nomnomzbot.composeapp.generated.resources.home_activity_duration_minutes
import nomnomzbot.composeapp.generated.resources.home_activity_duration_seconds
import nomnomzbot.composeapp.generated.resources.home_activity_gift_anonymous
import nomnomzbot.composeapp.generated.resources.home_activity_gift_count
import nomnomzbot.composeapp.generated.resources.home_activity_redemption_named_cost
import nomnomzbot.composeapp.generated.resources.home_activity_resub_months
import nomnomzbot.composeapp.generated.resources.home_activity_resub_months_streak
import nomnomzbot.composeapp.generated.resources.home_activity_subscribe_tier
import nomnomzbot.composeapp.generated.resources.home_activity_timeout_duration
import nomnomzbot.composeapp.generated.resources.home_activity_raid
import nomnomzbot.composeapp.generated.resources.home_activity_raid_with_viewers
import nomnomzbot.composeapp.generated.resources.home_activity_redemption
import nomnomzbot.composeapp.generated.resources.home_activity_redemption_named
import nomnomzbot.composeapp.generated.resources.home_activity_replay_button
import nomnomzbot.composeapp.generated.resources.home_activity_replay_failed
import nomnomzbot.composeapp.generated.resources.home_activity_replay_in_progress
import nomnomzbot.composeapp.generated.resources.home_activity_replay_nothing
import nomnomzbot.composeapp.generated.resources.home_activity_replay_success
import nomnomzbot.composeapp.generated.resources.home_activity_resub
import nomnomzbot.composeapp.generated.resources.home_activity_section
import nomnomzbot.composeapp.generated.resources.home_activity_subscribe
import nomnomzbot.composeapp.generated.resources.home_activity_subscription_gift
import nomnomzbot.composeapp.generated.resources.home_activity_timeout
import nomnomzbot.composeapp.generated.resources.home_activity_view_all
import nomnomzbot.composeapp.generated.resources.home_first_run_section
import nomnomzbot.composeapp.generated.resources.home_first_run_connect_integration
import nomnomzbot.composeapp.generated.resources.home_first_run_create_command
import nomnomzbot.composeapp.generated.resources.home_first_run_create_pipeline
import nomnomzbot.composeapp.generated.resources.home_change_title
import nomnomzbot.composeapp.generated.resources.home_error
import nomnomzbot.composeapp.generated.resources.home_game_label
import nomnomzbot.composeapp.generated.resources.home_live_ops_active_poll
import nomnomzbot.composeapp.generated.resources.home_live_ops_active_prediction
import nomnomzbot.composeapp.generated.resources.home_live_ops_cancel
import nomnomzbot.composeapp.generated.resources.home_live_ops_cancel_prediction
import nomnomzbot.composeapp.generated.resources.home_live_ops_cancel_raid
import nomnomzbot.composeapp.generated.resources.home_live_ops_commercial_confirm
import nomnomzbot.composeapp.generated.resources.home_live_ops_commercial_length_label
import nomnomzbot.composeapp.generated.resources.home_live_ops_clip_done
import nomnomzbot.composeapp.generated.resources.home_live_ops_create_clip
import nomnomzbot.composeapp.generated.resources.home_live_ops_create_poll
import nomnomzbot.composeapp.generated.resources.home_live_ops_create_prediction
import nomnomzbot.composeapp.generated.resources.home_live_ops_end_poll
import nomnomzbot.composeapp.generated.resources.home_live_ops_mark_moment
import nomnomzbot.composeapp.generated.resources.home_live_ops_mark_moment_done
import nomnomzbot.composeapp.generated.resources.home_live_ops_mark_moment_failed
import nomnomzbot.composeapp.generated.resources.home_live_ops_outcome_pick
import nomnomzbot.composeapp.generated.resources.chat_poll_add_option
import nomnomzbot.composeapp.generated.resources.chat_poll_announce
import nomnomzbot.composeapp.generated.resources.chat_poll_duration_label
import nomnomzbot.composeapp.generated.resources.chat_poll_option_label
import nomnomzbot.composeapp.generated.resources.chat_poll_subtitle
import nomnomzbot.composeapp.generated.resources.home_poll_target_chat
import nomnomzbot.composeapp.generated.resources.home_poll_target_label
import nomnomzbot.composeapp.generated.resources.home_poll_target_twitch
import nomnomzbot.composeapp.generated.resources.home_poll_twitch_hint
import nomnomzbot.composeapp.generated.resources.home_live_ops_poll_confirm
import nomnomzbot.composeapp.generated.resources.home_live_ops_poll_duration_label
import nomnomzbot.composeapp.generated.resources.home_live_ops_poll_title_label
import nomnomzbot.composeapp.generated.resources.home_live_ops_prediction_confirm
import nomnomzbot.composeapp.generated.resources.home_live_ops_prediction_title_label
import nomnomzbot.composeapp.generated.resources.home_live_ops_prediction_window_label
import nomnomzbot.composeapp.generated.resources.home_live_ops_raid_confirm
import nomnomzbot.composeapp.generated.resources.home_live_ops_resolve_prediction
import nomnomzbot.composeapp.generated.resources.home_live_ops_snooze_ad
import nomnomzbot.composeapp.generated.resources.home_live_ops_start_commercial
import nomnomzbot.composeapp.generated.resources.home_live_ops_start_raid
import nomnomzbot.composeapp.generated.resources.home_live_ops_title
import nomnomzbot.composeapp.generated.resources.home_loading
import nomnomzbot.composeapp.generated.resources.home_no_title
import nomnomzbot.composeapp.generated.resources.home_retry
import nomnomzbot.composeapp.generated.resources.home_stat_chatters
import nomnomzbot.composeapp.generated.resources.home_stat_commands
import nomnomzbot.composeapp.generated.resources.home_stat_donations
import nomnomzbot.composeapp.generated.resources.home_stat_followers
import nomnomzbot.composeapp.generated.resources.home_stat_messages
import nomnomzbot.composeapp.generated.resources.home_stat_subscribers
import nomnomzbot.composeapp.generated.resources.home_stat_uptime
import nomnomzbot.composeapp.generated.resources.home_stat_viewers
import nomnomzbot.composeapp.generated.resources.home_platforms_label
import nomnomzbot.composeapp.generated.resources.home_platforms_offline
import nomnomzbot.composeapp.generated.resources.home_status_live
import nomnomzbot.composeapp.generated.resources.home_status_offline
import nomnomzbot.composeapp.generated.resources.home_stream_error
import nomnomzbot.composeapp.generated.resources.home_stream_save
import nomnomzbot.composeapp.generated.resources.home_stream_section
import nomnomzbot.composeapp.generated.resources.home_stream_tags_label
import nomnomzbot.composeapp.generated.resources.home_stream_title_label
import nomnomzbot.composeapp.generated.resources.home_subtitle
import nomnomzbot.composeapp.generated.resources.home_top_commands
import nomnomzbot.composeapp.generated.resources.home_top_commands_empty
import nomnomzbot.composeapp.generated.resources.home_top_commands_uses
import nomnomzbot.composeapp.generated.resources.home_uptime_format
import nomnomzbot.composeapp.generated.resources.home_uptime_offline
import nomnomzbot.composeapp.generated.resources.shell_nav_dashboard
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import org.jetbrains.compose.resources.stringResource

// The Home page (frontend-ia.md §3): the live channel landing — current stream state, headline counters,
// recent activity feed, and quick-action panel. Pure projection of [HomeController] state.
@Composable
fun HomeScreen(
    controller: HomeController,
    liveOpsController: LiveOpsController,
    chatPollsController: ChatPollsController,
    heldActionKeys: Set<String> = emptySet(),
    hubEvents: SharedFlow<HubEvent>? = null,
    /** Navigates to a [bot.nomnomz.dashboard.feature.shell.nav.ShellRoute] name — fired when the streamer taps
     * an action-required row. No-op by default; the shell wires the real navigation. */
    onNavigate: (String) -> Unit = {},
) {
    val state: HomeState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    LaunchedEffect(Unit) {
        controller.load()
        liveOpsController.load()
    }

    if (hubEvents != null) {
        LaunchedEffect(hubEvents) { controller.subscribeToHub(hubEvents) }
    }

    Box(modifier = Modifier.fillMaxSize()) {
        when (val current: HomeState = state) {
            is HomeState.Loading -> CenteredMessage(stringResource(Res.string.home_loading))
            is HomeState.Error ->
                ErrorContent(detail = current.detail, onRetry = {
                    scope.launch {
                        controller.load()
                        liveOpsController.load()
                    }
                })
            is HomeState.Ready ->
                ReadyContent(
                    stats = current.stats,
                    streamInfo = current.streamInfo,
                    activity = current.activity,
                    topCommands = current.topCommands,
                    actionRequired = current.actionRequired,
                    firstRunSteps = current.firstRunSteps,
                    streamError = current.streamError,
                    replayStatus = current.replayStatus,
                    liveOpsController = liveOpsController,
                    chatPollsController = chatPollsController,
                    heldActionKeys = heldActionKeys,
                    onUpdateStream = { title, game, tags ->
                        scope.launch { controller.updateStreamInfo(title, game, tags) }
                    },
                    onSearchCategories = controller::searchCategories,
                    onSearchRaidTargets = controller::searchRaidTargets,
                    onReplay = { eventId -> scope.launch { controller.replay(eventId) } },
                    onNavigate = onNavigate,
                )
        }
    }
}

// ─── Ready content ────────────────────────────────────────────────────────────

@Composable
private fun ReadyContent(
    stats: DashboardStats,
    streamInfo: StreamInfo?,
    activity: List<ActivityEvent>,
    topCommands: List<CommandSummary>,
    actionRequired: List<ActionRequiredItem>,
    firstRunSteps: List<FirstRunStep>,
    streamError: String?,
    replayStatus: Map<String, ReplayStatus>,
    liveOpsController: LiveOpsController,
    chatPollsController: ChatPollsController,
    heldActionKeys: Set<String>,
    onUpdateStream: (title: String?, game: String?, tags: List<String>?) -> Unit,
    onSearchCategories: suspend (String) -> List<PickerOption>,
    onSearchRaidTargets: suspend (String) -> List<PickerOption>,
    onReplay: (eventId: String) -> Unit,
    onNavigate: (String) -> Unit,
) {
    val spacing = LocalSpacing.current
    val scope = rememberCoroutineScope()
    val liveOpsState: LiveOpsState by liveOpsController.state.collectAsStateWithLifecycle()
    val ready: LiveOpsState.Ready? = liveOpsState as? LiveOpsState.Ready

    // The live-ops quick actions each gate on their OWN backend action key against the caller's resolved
    // heldActionKeys (which folds in each action's channel-effective floor, per-channel overrides, AND per-user
    // permits) — NOT a blanket Editor floor. So a moderator holding a Mod-floored action (clip/marker), a
    // per-channel-lowered floor (polls), or a per-user title permit sees exactly those enabled, the rest disabled
    // with a reason. QuickActionsCard resolves the per-action decision from [heldActionKeys].

    // A raid has a short pending window before it goes live during which it can be cancelled. The backend does not
    // surface a "raid pending" flag, so the panel tracks it locally: set when a start returns a raid, cleared on a
    // cancel — the Cancel-raid action shows only while a raid this session is still in that window.
    var raidPending: Boolean by remember { mutableStateOf(false) }

    var showChangeTitleDialog: Boolean by remember { mutableStateOf(false) }
    var showPollDialog: Boolean by remember { mutableStateOf(false) }
    var showPredictionDialog: Boolean by remember { mutableStateOf(false) }
    var showRaidDialog: Boolean by remember { mutableStateOf(false) }
    var showCommercialDialog: Boolean by remember { mutableStateOf(false) }
    var showResolvePredictionDialog: Boolean by remember { mutableStateOf(false) }
    // A transient result line after "Mark moment" — the success confirmation, or the backend's Twitch error.
    var markerNotice: String? by remember { mutableStateOf(null) }
    // Resolved at composition (stringResource is @Composable) so the mark-moment coroutine can set the notice.
    val markSuccessMsg: String = stringResource(Res.string.home_live_ops_mark_moment_done)
    val markFailMsg: String = stringResource(Res.string.home_live_ops_mark_moment_failed)
    val clipDonePrefix: String = stringResource(Res.string.home_live_ops_clip_done)

    Column(
        // Padding sits INSIDE the scroll (content padding), not on an outer Box — otherwise the scroll
        // viewport clips flush against the first line and the page title's ascenders get shaved off. With
        // it here the clip is at the true container edge and the s6 inset scrolls with the content.
        modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(spacing.s6),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        PageHeader(
            title = stringResource(Res.string.shell_nav_dashboard),
            subtitle = stringResource(Res.string.home_subtitle),
        )

        // Real, already-detected conditions needing the streamer's attention — absent (not a fake "all good"
        // banner) when nothing is wrong, per house rule: never show unenforced/fabricated positive state.
        if (actionRequired.isNotEmpty()) {
            ActionRequiredCard(items = actionRequired, onNavigate = onNavigate)
        }

        // Suggested next steps for a channel with no commands, no pipelines, and no connected integration yet —
        // absent (not a stale "still onboarding" banner) the moment any of those becomes real, per house rule:
        // never show unenforced/fabricated state. Mirrors ActionRequiredCard's truthful-emptiness pattern.
        if (firstRunSteps.isNotEmpty()) {
            FirstRunChecklistCard(steps = firstRunSteps, onNavigate = onNavigate)
        }

        LiveBanner(stats = stats)
        StatTilesRow(stats = stats)
        PlatformsRow(platforms = stats.platformsLive)

        // Two-column lower section: activity feed (wider) + right sidebar (actions + top commands).
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(spacing.s4),
            verticalAlignment = Alignment.Top,
        ) {
            ActivityFeedCard(
                events = activity,
                replayStatus = replayStatus,
                heldActionKeys = heldActionKeys,
                onReplay = onReplay,
                onViewAll = { onNavigate("Analytics") },
                modifier = Modifier.weight(1.6f),
            )

            Column(
                modifier = Modifier.weight(1f),
                verticalArrangement = Arrangement.spacedBy(spacing.s4),
            ) {
                QuickActionsCard(
                    ready = ready,
                    isLive = stats.isLive,
                    heldActionKeys = heldActionKeys,
                    raidPending = raidPending,
                    onChangeTitle = { showChangeTitleDialog = true },
                    onCreateClip = {
                        scope.launch {
                            val clip: LiveOpsClipStub? = liveOpsController.createClip()
                            if (clip != null) markerNotice = "$clipDonePrefix ${clip.editUrl}"
                        }
                    },
                    onMarkMoment = {
                        scope.launch {
                            val marker: LiveOpsMarker? = liveOpsController.createMarker(null)
                            markerNotice =
                                if (marker != null) {
                                    markSuccessMsg
                                } else {
                                    (liveOpsController.state.value as? LiveOpsState.Ready)?.actionError
                                        ?: markFailMsg
                                }
                        }
                    },
                    onStartPoll = { showPollDialog = true },
                    onEndPoll = { scope.launch { liveOpsController.endPoll("TERMINATED") } },
                    onStartPrediction = { showPredictionDialog = true },
                    onResolvePrediction = { showResolvePredictionDialog = true },
                    onCancelPrediction = { scope.launch { liveOpsController.cancelPrediction() } },
                    onStartRaid = { showRaidDialog = true },
                    onCancelRaid = {
                        scope.launch {
                            // Only drop the Cancel affordance if the cancel actually succeeded — a failed cancel
                            // surfaces on actionError below and keeps the button up (the raid is still pending).
                            if (liveOpsController.cancelRaid()) raidPending = false
                        }
                    },
                    onStartCommercial = { showCommercialDialog = true },
                    onSnoozeAd = { scope.launch { liveOpsController.snoozeNextAd() } },
                )

                // Every live-ops quick action records a failure on ready.actionError (non-affiliate 403, channel
                // not live, etc.); render it here so a failed poll/prediction/raid/commercial/clip is never a
                // silent no-op that reads as a dead button.
                ready?.actionError?.let { error ->
                    ActionErrorBanner(message = error)
                }

                markerNotice?.let { notice ->
                    Text(
                        text = notice,
                        style = LocalTypography.current.xs,
                        color = LocalTokens.current.mutedForeground,
                    )
                }

                if (topCommands.isNotEmpty()) {
                    TopCommandsCard(commands = topCommands)
                }

                // Bot-run chat poll (item: chat polls) — sits beside the Twitch-native live-ops poll above,
                // labeled "Chat poll" so the two voting mechanisms read as distinct.
                ChatPollsCard(controller = chatPollsController)
            }
        }
    }

    // ─── Dialogs ──────────────────────────────────────────────────────────────

    if (showChangeTitleDialog) {
        ChangeTitleDialog(
            streamInfo = streamInfo,
            error = streamError,
            onSearchCategories = onSearchCategories,
            onSave = { title, game, tags ->
                showChangeTitleDialog = false
                onUpdateStream(title, game, tags)
            },
            onDismiss = { showChangeTitleDialog = false },
        )
    }

    if (showPollDialog) {
        // One "Start poll" entry point. The pretty modal picks the target: a bot chat poll (viewers type a
        // number, any platform) or Twitch's native poll. Both mechanisms are kept — the dialog closes itself
        // only on a successful start, so a failed start keeps the operator's typed question/options.
        StartPollDialog(
            onStartChatPoll = { question, options, duration, announce ->
                chatPollsController.open(question, options, duration, announce)
            },
            onStartTwitchPoll = { title, choices, duration ->
                liveOpsController.createPoll(title, choices, duration)
            },
            onDismiss = { showPollDialog = false },
        )
    }

    if (showPredictionDialog) {
        PredictionDialog(
            onConfirm = { title, outcomes, window ->
                showPredictionDialog = false
                scope.launch { liveOpsController.createPrediction(title, outcomes, window) }
            },
            onDismiss = { showPredictionDialog = false },
        )
    }

    if (showRaidDialog) {
        RaidDialog(
            onSearchRaidTargets = onSearchRaidTargets,
            onConfirm = { target ->
                showRaidDialog = false
                scope.launch { raidPending = liveOpsController.startRaid(target) != null }
            },
            onDismiss = { showRaidDialog = false },
        )
    }

    if (showCommercialDialog) {
        CommercialDialog(
            onConfirm = { length ->
                showCommercialDialog = false
                scope.launch { liveOpsController.startCommercial(length) }
            },
            onDismiss = { showCommercialDialog = false },
        )
    }

    if (showResolvePredictionDialog) {
        ready?.activePrediction?.let { prediction: LiveOpsPrediction ->
            ResolvePredictionDialog(
                prediction = prediction,
                onConfirm = { winningId ->
                    showResolvePredictionDialog = false
                    scope.launch { liveOpsController.resolvePrediction(winningId) }
                },
                onDismiss = { showResolvePredictionDialog = false },
            )
        }
    }
}

// ─── Live banner ──────────────────────────────────────────────────────────────

@Composable
private fun LiveBanner(stats: DashboardStats) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Card(modifier = Modifier.fillMaxWidth()) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(spacing.s4),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(
            modifier = Modifier.weight(1f),
            verticalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            // Status row — LIVE pill + uptime, or simple offline indicator.
            Row(
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(spacing.s2),
            ) {
                if (stats.isLive) {
                    Box(
                        modifier = Modifier
                            .clip(RoundedCornerShape(tokens.radius.sm))
                            .background(tokens.destructive)
                            .padding(horizontal = spacing.s2, vertical = spacing.s0_5),
                    ) {
                        Text(
                            text = stringResource(Res.string.home_status_live).uppercase(),
                            style = typography.xs,
                            fontWeight = FontWeight.Bold,
                            color = tokens.destructiveForeground,
                        )
                    }
                    stats.uptime?.let { uptime ->
                        Text(
                            text = stringResource(Res.string.home_uptime_format, (uptime / 3600).toInt(), ((uptime % 3600) / 60).toInt()),
                            style = typography.sm,
                            color = tokens.mutedForeground,
                        )
                    }
                } else {
                    Box(
                        modifier = Modifier
                            .size(spacing.s2)
                            .clip(CircleShape)
                            .background(tokens.mutedForeground),
                    )
                    Text(
                        text = stringResource(Res.string.home_status_offline),
                        style = typography.sm,
                        color = tokens.mutedForeground,
                    )
                }
            }

            // Rendered through EmojiText so Unicode emoji in a stream title show as their real glyphs (inline
            // Twemoji images) instead of □ tofu on the web/Wasm build, which has no colour-emoji font.
            EmojiText(
                text = stats.streamTitle?.takeIf { it.isNotBlank() }
                    ?: stringResource(Res.string.home_no_title),
                style = typography.xl.copy(fontWeight = FontWeight.SemiBold),
                color = tokens.cardForeground,
                maxLines = 2,
                overflow = TextOverflow.Ellipsis,
            )
            stats.gameName?.takeIf { it.isNotBlank() }?.let { game ->
                Text(
                    text = stringResource(Res.string.home_game_label, game),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
            }
        }
    }
    }
}

// ─── Action-required hero tile ────────────────────────────────────────────────

// Real, already-detected conditions needing the streamer's attention (S071a backend / S071b tile). Every row
// traces to a real signal (dead integration token, held AutoMod message, …) — never a fabricated positive.
// Rendered only when [items] is non-empty; the caller skips the whole card on an empty list.
@Composable
private fun ActionRequiredCard(items: List<ActionRequiredItem>, onNavigate: (String) -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.padding(spacing.s4),
            verticalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            Text(
                text = stringResource(Res.string.home_action_required_section),
                style = typography.sm,
                color = tokens.mutedForeground,
            )
            items.forEach { item ->
                ActionRequiredRow(item = item, onClick = { onNavigate(item.deepLinkRoute) })
            }
        }
    }
}

@Composable
private fun ActionRequiredRow(item: ActionRequiredItem, onClick: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    // Only "critical" gets the destructive treatment (mirrors ActionErrorBanner's existing failure styling);
    // every other severity ("warning", and anything the backend adds later) uses the accent highlight — no
    // new colors invented, both already used elsewhere on this screen (destructive = write failures, accent =
    // the raid/attention dot in the activity feed).
    val isCritical: Boolean = item.severity == "critical"
    val badgeBackground = if (isCritical) tokens.destructive else tokens.accent
    val badgeForeground = if (isCritical) tokens.destructiveForeground else tokens.accentForeground
    val severityLabel: String =
        if (isCritical) {
            stringResource(Res.string.home_action_required_severity_critical)
        } else {
            stringResource(Res.string.home_action_required_severity_warning)
        }

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.md))
            .clickable(onClick = onClick)
            .padding(vertical = spacing.s2, horizontal = spacing.s1),
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Box(
            modifier = Modifier
                .clip(RoundedCornerShape(tokens.radius.sm))
                .background(badgeBackground)
                .padding(horizontal = spacing.s2, vertical = spacing.s0_5),
        ) {
            Text(
                text = severityLabel.uppercase(),
                style = typography.xs,
                fontWeight = FontWeight.Bold,
                color = badgeForeground,
            )
        }
        Column(modifier = Modifier.weight(1f)) {
            Text(
                text = resolveRowLabel(
                    primary = item.title,
                    secondary = item.message,
                    typeLabel = item.kind.ifBlank { "Notice" },
                    discriminatorSource = item.deepLinkRoute,
                ),
                style = typography.sm,
                fontWeight = FontWeight.SemiBold,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = item.message,
                style = typography.xs,
                color = tokens.mutedForeground,
                maxLines = 2,
                overflow = TextOverflow.Ellipsis,
            )
        }
    }
}

// ─── First-run checklist ──────────────────────────────────────────────────────

// Suggested next actions for a brand-new channel (S071c) — same card/row shape as ActionRequiredCard, but
// neutral (no severity badge): these are suggestions, not problems.
@Composable
private fun FirstRunChecklistCard(steps: List<FirstRunStep>, onNavigate: (String) -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.padding(spacing.s4),
            verticalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            Text(
                text = stringResource(Res.string.home_first_run_section),
                style = typography.sm,
                color = tokens.mutedForeground,
            )
            steps.forEach { step ->
                FirstRunStepRow(step = step, onClick = { onNavigate(step.deepLinkRoute) })
            }
        }
    }
}

@Composable
private fun FirstRunStepRow(step: FirstRunStep, onClick: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val label: String = when (step.kind) {
        FirstRunStepKind.ConnectIntegration -> stringResource(Res.string.home_first_run_connect_integration)
        FirstRunStepKind.CreateCommand -> stringResource(Res.string.home_first_run_create_command)
        FirstRunStepKind.CreatePipeline -> stringResource(Res.string.home_first_run_create_pipeline)
    }

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.md))
            .clickable(onClick = onClick)
            .padding(vertical = spacing.s2, horizontal = spacing.s1),
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Box(
            modifier = Modifier
                .size(spacing.s2)
                .clip(CircleShape)
                .background(tokens.accent),
        )
        Text(
            text = label,
            style = typography.sm,
            color = tokens.cardForeground,
            modifier = Modifier.weight(1f),
        )
    }
}

// ─── Stat tiles ───────────────────────────────────────────────────────────────

// A balanced stat-card grid (owner's home-screen ask): current viewers, followers, subscribers, chatters today,
// donations today, commands, and uptime — all real data from the backend. Tiles wrap into equal-width columns
// (padded on the last row) so the row balances itself rather than leaving a ragged trailing gap.
@Composable
private fun StatTilesRow(stats: DashboardStats) {
    val spacing = LocalSpacing.current
    val tiles: List<Pair<String, String>> =
        listOf(
            stringResource(Res.string.home_stat_viewers) to stats.viewerCount.toString(),
            stringResource(Res.string.home_stat_followers) to stats.followerCount.toString(),
            stringResource(Res.string.home_stat_subscribers) to stats.subscriberCount.toString(),
            stringResource(Res.string.home_stat_chatters) to stats.chattersToday.toString(),
            stringResource(Res.string.home_stat_donations) to donationsLabel(stats),
            stringResource(Res.string.home_stat_commands) to stats.commandsUsed.toString(),
            stringResource(Res.string.home_stat_messages) to stats.messagesCount.toString(),
            stringResource(Res.string.home_stat_uptime) to uptimeLabel(stats.uptime),
        )
    val columns = 4

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
        tiles.chunked(columns).forEach { rowTiles ->
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(spacing.s3),
            ) {
                rowTiles.forEach { (label, value) ->
                    StatTile(modifier = Modifier.weight(1f), label = label, value = value)
                }
                repeat(columns - rowTiles.size) { Spacer(modifier = Modifier.weight(1f)) }
            }
        }
    }
}

// "Streaming to" — the platforms the owner is live on right now, as platform badges (empty = Offline). Real
// presence tracked by the bot; never a fabricated badge.
@Composable
private fun PlatformsRow(platforms: List<String>) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Text(
            text = stringResource(Res.string.home_platforms_label),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
        if (platforms.isEmpty()) {
            Text(
                text = stringResource(Res.string.home_platforms_offline),
                style = typography.sm,
                color = tokens.mutedForeground,
            )
        } else {
            platforms.forEach { platform ->
                Badge(variant = BadgeVariant.Secondary) {
                    Text(text = platform.replaceFirstChar { it.uppercase() })
                }
            }
        }
    }
}

// The donations tile value: today's supporter total in MAJOR units when every amount-bearing event shares one
// currency (amount / 100 + code), else the bare event count — never a fabricated 0.00 on a mixed-currency day.
private fun donationsLabel(stats: DashboardStats): String {
    val minor: Long? = stats.supporterAmountMinorToday
    val currency: String? = stats.supporterCurrency
    return if (minor != null && currency != null) {
        val whole: Long = minor / 100
        val cents: Long = kotlin.math.abs(minor % 100)
        "$whole.${cents.toString().padStart(2, '0')} $currency"
    } else {
        stats.supporterEventsToday.toString()
    }
}

@Composable
private fun StatTile(modifier: Modifier, label: String, value: String) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Card(modifier = modifier.clearAndSetSemantics { contentDescription = "$label: $value" }) {
        Column(
            modifier = Modifier.padding(spacing.s4),
            verticalArrangement = Arrangement.spacedBy(spacing.s1),
        ) {
            Text(text = value, style = typography.xl2, color = tokens.cardForeground)
            Text(text = label, style = typography.sm, color = tokens.mutedForeground)
        }
    }
}

// ─── Activity feed ────────────────────────────────────────────────────────────

// Collapsed to a compact preview — a "View all" link opens the Analytics page for the full history, rather
// than every one of the backend's 20 most-recent rows crowding the Home layout.
private const val ACTIVITY_PREVIEW_COUNT: Int = 5

@Composable
private fun ActivityFeedCard(
    events: List<ActivityEvent>,
    replayStatus: Map<String, ReplayStatus>,
    heldActionKeys: Set<String>,
    onReplay: (eventId: String) -> Unit,
    onViewAll: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Card(modifier = modifier) {
    Column(
        modifier = Modifier.padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(
                text = stringResource(Res.string.home_activity_section),
                style = typography.sm,
                color = tokens.mutedForeground,
            )
            if (events.size > ACTIVITY_PREVIEW_COUNT) {
                TextButton(onClick = onViewAll) {
                    Text(text = stringResource(Res.string.home_activity_view_all))
                }
            }
        }
        if (events.isEmpty()) {
            Text(
                text = stringResource(Res.string.home_activity_empty),
                style = typography.sm,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
                modifier = Modifier.fillMaxWidth().padding(vertical = spacing.s4),
            )
        } else {
            events.take(ACTIVITY_PREVIEW_COUNT).forEach { event ->
                ActivityRow(
                    event = event,
                    replayStatus = replayStatus[event.id],
                    heldActionKeys = heldActionKeys,
                    onReplay = { onReplay(event.id) },
                )
            }
        }
    }
    }
}

// Pull the reward name out of a redemption event's {"rewardTitle":…} JSON data payload. Tolerant: any parse
// failure or absent field yields null and the row falls back to the generic "redeemed a reward" wording.
private fun rewardTitleFromData(data: String?): String? =
    data
        ?.takeIf { it.isNotBlank() }
        ?.let {
            runCatching { Json.parseToJsonElement(it).jsonObject["rewardTitle"]?.jsonPrimitive?.contentOrNull }
                .getOrNull()
        }
        ?.takeIf { it.isNotBlank() }

// Pull a numeric field out of an event's JSON data payload. Tolerant in the same way as
// [rewardTitleFromData]: an absent field, a non-number, or unparseable JSON yields null and the row falls
// back to its countless wording rather than rendering a wrong or zero figure.
private fun intFromData(data: String?, field: String): Int? =
    data
        ?.takeIf { it.isNotBlank() }
        ?.let {
            runCatching { Json.parseToJsonElement(it).jsonObject[field]?.jsonPrimitive?.contentOrNull?.toIntOrNull() }
                .getOrNull()
        }

// Payload readers, all tolerant in the same way: an absent field, a wrong type, or unparseable JSON yields
// null so the row falls back to its figureless wording rather than showing a wrong or zero value.
private fun stringFromData(data: String?, field: String): String? =
    data
        ?.takeIf { it.isNotBlank() }
        ?.let {
            runCatching { Json.parseToJsonElement(it).jsonObject[field]?.jsonPrimitive?.contentOrNull }
                .getOrNull()
        }
        ?.takeIf { it.isNotBlank() }

private fun boolFromData(data: String?, field: String): Boolean? =
    stringFromData(data, field)?.lowercase()?.let {
        when (it) {
            "true" -> true
            "false" -> false
            else -> null
        }
    }

// Twitch reports sub tiers as "1000"/"2000"/"3000". Showing the raw number would read as a price.
@Composable
private fun tierLabel(tier: String?): String =
    when (tier) {
        "1000" -> "1"
        "2000" -> "2"
        "3000" -> "3"
        null, "" -> "1"
        else -> tier
    }

// Compact, largest sensible unit — a 600-second timeout reads as "10m", not "600s".
@Composable
private fun formatDuration(seconds: Int): String =
    when {
        seconds >= 86_400 -> stringResource(Res.string.home_activity_duration_days, seconds / 86_400)
        seconds >= 3_600 -> stringResource(Res.string.home_activity_duration_hours, seconds / 3_600)
        seconds >= 60 -> stringResource(Res.string.home_activity_duration_minutes, seconds / 60)
        else -> stringResource(Res.string.home_activity_duration_seconds, seconds)
    }

@Composable
private fun ActivityRow(
    event: ActivityEvent,
    replayStatus: ReplayStatus?,
    heldActionKeys: Set<String>,
    onReplay: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val who: String = event.username ?: "—"
    // A redemption carries its reward name in the {"rewardTitle":…} data payload — show WHICH reward, not just
    // that one was redeemed. Absent/unparseable → the generic wording.
    val rewardTitle: String? = rewardTitleFromData(event.data)
    // Every figure the backend records on an event (TwitchChannelEventLogProjection) — the feed used to
    // render bare labels and discard all of it, so "QTkittE resubscribed" hid the month count that is the
    // entire reason anyone reads a resub line.
    val viewerCount: Int? = intFromData(event.data, "viewerCount")
    val bits: Int? = intFromData(event.data, "bits")
    val cumulativeMonths: Int? = intFromData(event.data, "cumulativeMonths")
    val streakMonths: Int? = intFromData(event.data, "streakMonths")
    val giftCount: Int? = intFromData(event.data, "giftCount")
    val cost: Int? = intFromData(event.data, "cost")
    val durationSeconds: Int? = intFromData(event.data, "durationSeconds")
    val isAnonymous: Boolean = boolFromData(event.data, "isAnonymous") == true
    val tier: String = tierLabel(stringFromData(event.data, "tier"))
    val duration: String? = durationSeconds?.let { formatDuration(it) }
    val label: String = when (event.type) {
        "channel.follow" -> stringResource(Res.string.home_activity_follow, who)
        "channel.subscribe" -> stringResource(Res.string.home_activity_subscribe_tier, who, tier)
        "channel.subscription.message" ->
            when {
                cumulativeMonths != null && streakMonths != null && streakMonths > 1 ->
                    stringResource(
                        Res.string.home_activity_resub_months_streak,
                        who,
                        cumulativeMonths,
                        streakMonths,
                        tier,
                    )
                cumulativeMonths != null ->
                    stringResource(Res.string.home_activity_resub_months, who, cumulativeMonths, tier)
                else -> stringResource(Res.string.home_activity_resub, who)
            }
        "channel.subscription.gift" ->
            when {
                isAnonymous && giftCount != null ->
                    stringResource(Res.string.home_activity_gift_anonymous, giftCount, tier)
                giftCount != null ->
                    stringResource(Res.string.home_activity_gift_count, who, giftCount, tier)
                else -> stringResource(Res.string.home_activity_subscription_gift, who)
            }
        "channel.cheer" ->
            if (bits != null) {
                stringResource(Res.string.home_activity_cheer_with_bits, who, bits)
            } else {
                stringResource(Res.string.home_activity_cheer, who)
            }
        "channel.raid" ->
            if (viewerCount != null) {
                stringResource(Res.string.home_activity_raid_with_viewers, who, viewerCount)
            } else {
                stringResource(Res.string.home_activity_raid, who)
            }
        "channel.channel_points_custom_reward_redemption.add" ->
            when {
                rewardTitle != null && cost != null ->
                    stringResource(
                        Res.string.home_activity_redemption_named_cost,
                        who,
                        rewardTitle,
                        cost,
                    )
                rewardTitle != null ->
                    stringResource(Res.string.home_activity_redemption_named, who, rewardTitle)
                else -> stringResource(Res.string.home_activity_redemption, who)
            }
        "channel.ban" -> stringResource(Res.string.home_activity_ban, who)
        "channel.timeout" ->
            if (duration != null) {
                stringResource(Res.string.home_activity_timeout_duration, who, duration)
            } else {
                stringResource(Res.string.home_activity_timeout, who)
            }
        "channel.moderator.add" -> stringResource(Res.string.home_activity_mod_add, who)
        "channel.moderator.remove" -> stringResource(Res.string.home_activity_mod_remove, who)
        else -> stringResource(Res.string.home_activity_event)
    }
    val dotColor = when (event.type) {
        "channel.follow" -> tokens.primary
        "channel.subscribe", "channel.subscription.message", "channel.subscription.gift" -> tokens.ring
        "channel.cheer" -> tokens.primary
        "channel.raid" -> tokens.accent
        "channel.channel_points_custom_reward_redemption.add" -> tokens.ring
        "channel.ban", "channel.timeout" -> tokens.destructive
        "channel.moderator.add", "channel.moderator.remove" -> tokens.mutedForeground
        else -> tokens.mutedForeground
    }

    // Mirrors the backend's `dashboard:replay` action key (Mod floor) — the SAME per-action authorization the
    // panel's other write actions gate on, not a client-guessed role. Disable (never hide) below the floor, with
    // the reason as a hover tooltip — the house rule for every gated action in this screen.
    val manage: ManageDecision = rememberManageDecisionForAction(heldActionKeys, "dashboard:replay")
    val replayReason: String? = manage.deniedReason?.takeIf { it.isNotBlank() }
    // Only THIS row disables while its own replay is in flight — other rows are unaffected, per-event by id.
    val inFlight: Boolean = replayStatus is ReplayStatus.InFlight
    val replayEnabled: Boolean = manage is ManageDecision.Allowed && !inFlight
    val replayLabel: String = stringResource(Res.string.home_activity_replay_button)
    val inFlightReason: String? = if (inFlight) stringResource(Res.string.home_activity_replay_in_progress) else null

    Row(
        modifier = Modifier.fillMaxWidth().padding(vertical = spacing.s1),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Row(
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(spacing.s2),
            modifier = Modifier.weight(1f),
        ) {
            Box(
                modifier = Modifier
                    .size(spacing.s2)
                    .clip(CircleShape)
                    .background(dotColor),
            )
            Column {
                Text(
                    text = label,
                    style = typography.sm,
                    color = tokens.cardForeground,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                // A distinct, truthful confirmation per outcome — never the same generic line for "replayed" and
                // "nothing to replay": the streamer needs to know whether the viewer actually saw it again.
                when (replayStatus) {
                    is ReplayStatus.Replayed ->
                        Text(
                            text = stringResource(Res.string.home_activity_replay_success),
                            style = typography.xs,
                            color = tokens.primary,
                        )
                    is ReplayStatus.NothingToReplay ->
                        Text(
                            text = stringResource(Res.string.home_activity_replay_nothing),
                            style = typography.xs,
                            color = tokens.mutedForeground,
                        )
                    is ReplayStatus.Failed ->
                        Text(
                            text = stringResource(Res.string.home_activity_replay_failed),
                            style = typography.xs,
                            color = tokens.destructive,
                        )
                    is ReplayStatus.InFlight, null -> Unit
                }
            }
        }
        Tooltip(text = replayReason ?: inFlightReason ?: replayLabel) {
            GlyphButton(
                icon = RefreshGlyph,
                label = replayLabel,
                onClick = onReplay,
                enabled = replayEnabled,
            )
        }
        Text(
            text = event.timestamp.take(10),
            style = typography.xs,
            color = tokens.mutedForeground,
            modifier = Modifier.padding(start = spacing.s3),
        )
    }
}

// ─── Quick actions ────────────────────────────────────────────────────────────

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun QuickActionsCard(
    ready: LiveOpsState.Ready?,
    isLive: Boolean,
    heldActionKeys: Set<String>,
    raidPending: Boolean,
    onChangeTitle: () -> Unit,
    onCreateClip: () -> Unit,
    onMarkMoment: () -> Unit,
    onStartPoll: () -> Unit,
    onEndPoll: () -> Unit,
    onStartPrediction: () -> Unit,
    onResolvePrediction: () -> Unit,
    onCancelPrediction: () -> Unit,
    onStartRaid: () -> Unit,
    onCancelRaid: () -> Unit,
    onStartCommercial: () -> Unit,
    onSnoozeAd: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val activePoll: LiveOpsPoll? = ready?.activePoll
    val activePrediction: LiveOpsPrediction? = ready?.activePrediction

    Card(modifier = Modifier.fillMaxWidth()) {
    Column(
        modifier = Modifier.padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Text(
            text = stringResource(Res.string.home_live_ops_title),
            style = typography.sm,
            color = tokens.mutedForeground,
        )

        activePoll?.let { poll ->
            EmojiText(
                text = stringResource(Res.string.home_live_ops_active_poll, poll.title),
                style = typography.xs,
                color = tokens.primary,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
        activePrediction?.let { prediction ->
            EmojiText(
                text = stringResource(Res.string.home_live_ops_active_prediction, prediction.title),
                style = typography.xs,
                color = tokens.ring,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }

        FlowRow(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(spacing.s2),
            verticalArrangement = Arrangement.spacedBy(spacing.s2),
            maxItemsInEachRow = 2,
        ) {
            GatedQuickAction(heldActionKeys = heldActionKeys, actionKey = "channel:title:write", modifier = Modifier.weight(1f)) { enabled, mod ->
                QuickActionButton(
                    icon = EditGlyph,
                    label = stringResource(Res.string.home_change_title),
                    onClick = onChangeTitle,
                    enabled = enabled,
                    modifier = mod,
                )
            }

            if (activePoll == null) {
                GatedQuickAction(heldActionKeys = heldActionKeys, actionKey = "live-ops:polls:write", modifier = Modifier.weight(1f)) { enabled, mod ->
                    QuickActionButton(
                        icon = AppIcons.CircleMessageQuestion,
                        label = stringResource(Res.string.home_live_ops_create_poll),
                        onClick = onStartPoll,
                        enabled = enabled,
                        modifier = mod,
                    )
                }
            } else {
                GatedQuickAction(heldActionKeys = heldActionKeys, actionKey = "live-ops:polls:write", modifier = Modifier.weight(1f)) { enabled, mod ->
                    QuickActionButton(
                        icon = CheckCircleGlyph,
                        label = stringResource(Res.string.home_live_ops_end_poll),
                        onClick = onEndPoll,
                        enabled = enabled,
                        modifier = mod,
                        destructive = true,
                    )
                }
            }

            if (activePrediction == null) {
                GatedQuickAction(heldActionKeys = heldActionKeys, actionKey = "live-ops:predictions:write", modifier = Modifier.weight(1f)) { enabled, mod ->
                    QuickActionButton(
                        icon = AppIcons.Vote2,
                        label = stringResource(Res.string.home_live_ops_create_prediction),
                        onClick = onStartPrediction,
                        enabled = enabled,
                        modifier = mod,
                    )
                }
            } else {
                GatedQuickAction(heldActionKeys = heldActionKeys, actionKey = "live-ops:predictions:write", modifier = Modifier.weight(1f)) { enabled, mod ->
                    QuickActionButton(
                        icon = CheckCircleGlyph,
                        label = stringResource(Res.string.home_live_ops_resolve_prediction),
                        onClick = onResolvePrediction,
                        enabled = enabled,
                        modifier = mod,
                    )
                }
                GatedQuickAction(heldActionKeys = heldActionKeys, actionKey = "live-ops:predictions:write", modifier = Modifier.weight(1f)) { enabled, mod ->
                    QuickActionButton(
                        icon = RemoveGlyph,
                        label = stringResource(Res.string.home_live_ops_cancel_prediction),
                        onClick = onCancelPrediction,
                        enabled = enabled,
                        modifier = mod,
                        destructive = true,
                    )
                }
            }

            GatedQuickAction(heldActionKeys = heldActionKeys, actionKey = "live-ops:clips:write", modifier = Modifier.weight(1f)) { enabled, mod ->
                QuickActionButton(
                    icon = AppIcons.Clips,
                    label = stringResource(Res.string.home_live_ops_create_clip),
                    onClick = onCreateClip,
                    enabled = enabled,
                    modifier = mod,
                )
            }

            // "Mark this moment" — a VOD bookmark. Twitch only accepts markers while LIVE, so the button is
            // shown only when the channel is live (rather than offering a tap that would always fail offline).
            if (isLive) {
                GatedQuickAction(heldActionKeys = heldActionKeys, actionKey = "live-ops:marker:create", modifier = Modifier.weight(1f)) { enabled, mod ->
                    QuickActionButton(
                        icon = AddGlyph,
                        label = stringResource(Res.string.home_live_ops_mark_moment),
                        onClick = onMarkMoment,
                        enabled = enabled,
                        modifier = mod,
                    )
                }
            }

            // A raid in its pending window can be cancelled before it sends; otherwise offer Start raid.
            if (raidPending) {
                GatedQuickAction(heldActionKeys = heldActionKeys, actionKey = "live-ops:raids:write", modifier = Modifier.weight(1f)) { enabled, mod ->
                    QuickActionButton(
                        icon = RemoveGlyph,
                        label = stringResource(Res.string.home_live_ops_cancel_raid),
                        onClick = onCancelRaid,
                        enabled = enabled,
                        modifier = mod,
                        destructive = true,
                    )
                }
            } else {
                GatedQuickAction(heldActionKeys = heldActionKeys, actionKey = "live-ops:raids:write", modifier = Modifier.weight(1f)) { enabled, mod ->
                    QuickActionButton(
                        icon = AppIcons.ArrowJumpRight,
                        label = stringResource(Res.string.home_live_ops_start_raid),
                        onClick = onStartRaid,
                        enabled = enabled,
                        modifier = mod,
                    )
                }
            }

            GatedQuickAction(heldActionKeys = heldActionKeys, actionKey = "live-ops:ads:write", modifier = Modifier.weight(1f)) { enabled, mod ->
                QuickActionButton(
                    icon = AppIcons.MoneyBagDollar,
                    label = stringResource(Res.string.home_live_ops_start_commercial),
                    onClick = onStartCommercial,
                    enabled = enabled,
                    modifier = mod,
                )
            }

            if (ready?.adSchedule != null && (ready.adSchedule?.snoozeCount ?: 0) > 0) {
                GatedQuickAction(heldActionKeys = heldActionKeys, actionKey = "live-ops:ads:write", modifier = Modifier.weight(1f)) { enabled, mod ->
                    QuickActionButton(
                        icon = RefreshGlyph,
                        label = stringResource(Res.string.home_live_ops_snooze_ad),
                        onClick = onSnoozeAd,
                        enabled = enabled,
                        modifier = mod,
                    )
                }
            }
        }
    }
    }
}

// Wrap one live-ops quick action in the write gate: below the manage floor the button renders disabled, with the
// localized reason announced to assistive tech (via [ManageGate]) and shown as a hover [Tooltip]. The gate carries
// the FlowRow [modifier] (the item weight); the button fills it. [button] receives the resolved enabled flag + the
// modifier to apply. One helper, every action — the disable-with-reason rule stays identical across the panel.
@Composable
private fun GatedQuickAction(
    heldActionKeys: Set<String>,
    actionKey: String,
    modifier: Modifier = Modifier,
    button: @Composable (enabled: Boolean, modifier: Modifier) -> Unit,
) {
    // Gate on the backend's authoritative per-action authorization (heldActionKeys), NOT a client role guess —
    // so a Mod-floored action (clip/marker), a per-channel-lowered floor (polls), and a per-user permit (title)
    // all light up correctly instead of a blanket "Requires Editor".
    val manage: ManageDecision = rememberManageDecisionForAction(heldActionKeys, actionKey)
    val reason: String? = manage.deniedReason?.takeIf { it.isNotBlank() }
    ManageGate(decision = manage, modifier = modifier) { enabled ->
        if (reason != null) {
            Tooltip(text = reason) { button(enabled, Modifier.fillMaxWidth()) }
        } else {
            button(enabled, Modifier.fillMaxWidth())
        }
    }
}

@Composable
private fun QuickActionButton(
    icon: DrawableResource,
    label: String,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    destructive: Boolean = false,
    enabled: Boolean = true,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    // Disabled below the manage floor: muted colours + no click wiring (the gate already announces the reason).
    val baseTint = if (destructive) tokens.destructive else tokens.mutedForeground
    val baseLabel = if (destructive) tokens.destructive else tokens.cardForeground
    val iconTint = if (enabled) baseTint else tokens.mutedForeground.copy(alpha = 0.5f)
    val labelColor = if (enabled) baseLabel else tokens.mutedForeground.copy(alpha = 0.5f)

    Column(
        modifier = modifier
            .clip(RoundedCornerShape(tokens.radius.md))
            .background(tokens.muted)
            .clickable(enabled = enabled, onClick = onClick)
            .padding(vertical = spacing.s3, horizontal = spacing.s2)
            .clearAndSetSemantics {
                contentDescription = label
                role = Role.Button
            },
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(spacing.s1),
    ) {
        Icon(
            painter = painterResource(icon),
            contentDescription = null,
            tint = iconTint,
            modifier = Modifier.size(spacing.s6),
        )
        Text(
            text = label,
            style = typography.xs,
            color = labelColor,
            textAlign = TextAlign.Center,
            maxLines = 2,
            overflow = TextOverflow.Ellipsis,
        )
    }
}

// ─── Top commands ─────────────────────────────────────────────────────────────

@Composable
private fun TopCommandsCard(commands: List<CommandSummary>) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Card(modifier = Modifier.fillMaxWidth()) {
    Column(
        modifier = Modifier.padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Text(
            text = stringResource(Res.string.home_top_commands),
            style = typography.sm,
            color = tokens.mutedForeground,
        )

        if (commands.isEmpty()) {
            Text(
                text = stringResource(Res.string.home_top_commands_empty),
                style = typography.sm,
                color = tokens.mutedForeground,
                modifier = Modifier.padding(vertical = spacing.s2),
            )
        } else {
            commands.forEachIndexed { index, cmd ->
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
                        modifier = Modifier.weight(1f),
                    ) {
                        Text(
                            text = "${index + 1}",
                            style = typography.xs,
                            color = tokens.mutedForeground,
                            modifier = Modifier.width(spacing.s4),
                        )
                        Text(
                            text = "!${cmd.name}",
                            style = typography.sm,
                            fontWeight = FontWeight.Medium,
                            color = tokens.cardForeground,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                        )
                    }
                    Text(
                        text = stringResource(Res.string.home_top_commands_uses, cmd.useCount.toInt()),
                        style = typography.xs,
                        color = tokens.mutedForeground,
                    )
                }
            }
        }
    }
    }
}

// ─── Dialogs ──────────────────────────────────────────────────────────────────

@Composable
private fun ChangeTitleDialog(
    streamInfo: StreamInfo?,
    error: String?,
    onSearchCategories: suspend (String) -> List<PickerOption>,
    onSave: (title: String?, game: String?, tags: List<String>?) -> Unit,
    onDismiss: () -> Unit,
) {
    var editTitle: String by remember(streamInfo?.title) { mutableStateOf(streamInfo?.title ?: "") }
    // The category picker owns a PickerRef selection; the stream update writes only the NAME, so the current
    // game is seeded as PickerRef(name, name) — the id is unused on this write. onClear reopens the search.
    var selectedGame: PickerRef? by remember(streamInfo?.gameName) {
        mutableStateOf(streamInfo?.gameName?.takeIf { it.isNotBlank() }?.let { PickerRef(it, it) })
    }
    var editTags: String by remember(streamInfo?.tags) {
        mutableStateOf(streamInfo?.tags?.joinToString(", ") ?: "")
    }
    val spacing = LocalSpacing.current

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(Res.string.home_stream_section)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                AppTextField(
                    value = editTitle,
                    onValueChange = { editTitle = it },
                    label = stringResource(Res.string.home_stream_title_label),
                    modifier = Modifier.fillMaxWidth(),
                )
                SearchPickerField(
                    search = onSearchCategories,
                    selected = selectedGame,
                    onSelect = { selectedGame = it },
                    onClear = { selectedGame = null },
                    label = stringResource(Res.string.category_picker_label),
                    placeholder = stringResource(Res.string.category_picker_placeholder),
                    emptyText = stringResource(Res.string.category_picker_empty),
                    modifier = Modifier.fillMaxWidth(),
                )
                AppTextField(
                    value = editTags,
                    onValueChange = { editTags = it },
                    label = stringResource(Res.string.home_stream_tags_label),
                    modifier = Modifier.fillMaxWidth(),
                )
                if (error != null) {
                    Text(
                        text = stringResource(Res.string.home_stream_error, error),
                        style = LocalTypography.current.sm,
                        color = LocalTokens.current.destructive,
                    )
                }
            }
        },
        confirmButton = {
            Button(
                onClick = {
                    // Always send the tags list (empty = clear). editTags is pre-filled from the current tags, so
                    // an untouched field re-sends them unchanged; clearing the field now actually clears them —
                    // the backend treats an empty list as "clear" and null as "leave unchanged", and the old
                    // `.takeIf { isNotEmpty() }` collapsed a cleared field to null, so tags could never be removed.
                    val tags: List<String> =
                        editTags.split(",").map { it.trim() }.filter { it.isNotEmpty() }
                    onSave(
                        editTitle.trim().takeIf { it.isNotEmpty() },
                        selectedGame?.name?.trim()?.takeIf { it.isNotEmpty() },
                        tags,
                    )
                },
            ) { Text(stringResource(Res.string.home_stream_save)) }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) { Text(stringResource(Res.string.home_live_ops_cancel)) }
        },
    )
}

// Where a poll runs: a bot chat poll (viewers type an option number, works on every platform) or Twitch's
// native channel poll. One dialog, one entry point — the toggle keeps both mechanisms without a second form.
private enum class PollTarget {
    Chat,
    Twitch,
}

// The single "Start poll" modal. Picks the target, then shows one pretty form: question, a dynamic option list
// (2–10 for chat, 2–5 for Twitch), and the per-target extras (chat: optional auto-close + announce; Twitch: a
// required duration). It closes itself only when the start succeeds, so a failed start (e.g. 409 "a poll is
// already open", or a non-affiliate 403 on Twitch) keeps the typed input for a retry.
@Composable
private fun StartPollDialog(
    onStartChatPoll: suspend (
        question: String,
        options: List<String>,
        durationSeconds: Int?,
        announce: Boolean,
    ) -> Boolean,
    onStartTwitchPoll: suspend (title: String, choices: List<String>, durationSeconds: Int) -> Boolean,
    onDismiss: () -> Unit,
) {
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    val scope = rememberCoroutineScope()

    var target: PollTarget by remember { mutableStateOf(PollTarget.Chat) }
    var question: String by remember { mutableStateOf("") }
    var options: List<String> by remember { mutableStateOf(listOf("", "")) }
    var chatDurationText: String by remember { mutableStateOf("") }
    var twitchDuration: Float by remember { mutableStateOf(60f) }
    var announce: Boolean by remember { mutableStateOf(true) }

    val maxOptions: Int = if (target == PollTarget.Twitch) 5 else 10
    val nonBlank: Int = options.count { it.isNotBlank() }
    val canStart: Boolean = question.isNotBlank() && nonBlank >= 2

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(Res.string.home_live_ops_create_poll)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                Text(
                    text = stringResource(Res.string.home_poll_target_label),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
                TabsList {
                    TabsTrigger(selected = target == PollTarget.Chat, onClick = { target = PollTarget.Chat }) {
                        Text(stringResource(Res.string.home_poll_target_chat), maxLines = 1)
                    }
                    TabsTrigger(selected = target == PollTarget.Twitch, onClick = { target = PollTarget.Twitch }) {
                        Text(stringResource(Res.string.home_poll_target_twitch), maxLines = 1)
                    }
                }
                Text(
                    text =
                        if (target == PollTarget.Chat) stringResource(Res.string.chat_poll_subtitle)
                        else stringResource(Res.string.home_poll_twitch_hint),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                )

                AppTextField(
                    value = question,
                    onValueChange = { question = it },
                    label = stringResource(Res.string.home_live_ops_poll_title_label),
                    modifier = Modifier.fillMaxWidth(),
                )
                options.forEachIndexed { index, value ->
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        AppTextField(
                            value = value,
                            onValueChange = { updated ->
                                options = options.toMutableList().also { it[index] = updated }
                            },
                            label = stringResource(Res.string.chat_poll_option_label, index + 1),
                            modifier = Modifier.weight(1f),
                        )
                        if (options.size > 2) {
                            GlyphButton(
                                icon = RemoveGlyph,
                                label = stringResource(Res.string.chat_poll_option_label, index + 1),
                                onClick = { options = options.toMutableList().also { it.removeAt(index) } },
                                tint = tokens.destructive,
                            )
                        }
                    }
                }
                if (options.size < maxOptions) {
                    GlyphButton(
                        icon = AddGlyph,
                        label = stringResource(Res.string.chat_poll_add_option),
                        onClick = { options = options + "" },
                        tint = tokens.primary,
                    )
                }

                if (target == PollTarget.Twitch) {
                    Text(
                        text =
                            stringResource(
                                Res.string.home_live_ops_poll_duration_label,
                                twitchDuration.toInt(),
                            ),
                        style = typography.sm,
                    )
                    Slider(
                        value = twitchDuration,
                        onValueChange = { twitchDuration = it },
                        valueRange = 15f..1800f,
                    )
                } else {
                    AppTextField(
                        value = chatDurationText,
                        onValueChange = { chatDurationText = it.filter { c -> c.isDigit() } },
                        label = stringResource(Res.string.chat_poll_duration_label),
                        modifier = Modifier.fillMaxWidth(),
                    )
                    Badge(selected = announce, onClick = { announce = !announce }) {
                        Text(stringResource(Res.string.chat_poll_announce), maxLines = 1)
                    }
                }
            }
        },
        confirmButton = {
            Button(
                onClick = {
                    scope.launch {
                        val cleaned: List<String> =
                            options.map { it.trim() }.filter { it.isNotEmpty() }
                        val started: Boolean =
                            if (target == PollTarget.Twitch) {
                                onStartTwitchPoll(question.trim(), cleaned.take(5), twitchDuration.toInt())
                            } else {
                                onStartChatPoll(
                                    question.trim(),
                                    cleaned,
                                    chatDurationText.toIntOrNull()?.takeIf { it > 0 },
                                    announce,
                                )
                            }
                        if (started) onDismiss()
                    }
                },
                enabled = canStart,
            ) { Text(stringResource(Res.string.home_live_ops_poll_confirm)) }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) { Text(stringResource(Res.string.home_live_ops_cancel)) }
        },
    )
}

@Composable
private fun PredictionDialog(
    onConfirm: (title: String, outcomes: List<String>, windowSeconds: Int) -> Unit,
    onDismiss: () -> Unit,
) {
    var title: String by remember { mutableStateOf("") }
    // One field per outcome (2–10, add/remove) — same fix as the poll: the old single-line "one per line"
    // input could never hold 2 lines, so the confirm button was permanently disabled.
    var outcomes: List<String> by remember { mutableStateOf(listOf("", "")) }
    var window: Float by remember { mutableStateOf(120f) }
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current
    val nonBlank: Int = outcomes.count { it.isNotBlank() }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(Res.string.home_live_ops_create_prediction)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                AppTextField(
                    value = title,
                    onValueChange = { title = it },
                    label = stringResource(Res.string.home_live_ops_prediction_title_label),
                    modifier = Modifier.fillMaxWidth(),
                )
                outcomes.forEachIndexed { index, value ->
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        AppTextField(
                            value = value,
                            onValueChange = { updated ->
                                outcomes = outcomes.toMutableList().also { it[index] = updated }
                            },
                            label = stringResource(Res.string.chat_poll_option_label, index + 1),
                            modifier = Modifier.weight(1f),
                        )
                        if (outcomes.size > 2) {
                            GlyphButton(
                                icon = RemoveGlyph,
                                label = stringResource(Res.string.chat_poll_option_label, index + 1),
                                onClick = { outcomes = outcomes.toMutableList().also { it.removeAt(index) } },
                                tint = tokens.destructive,
                            )
                        }
                    }
                }
                if (outcomes.size < 10) {
                    GlyphButton(
                        icon = AddGlyph,
                        label = stringResource(Res.string.chat_poll_add_option),
                        onClick = { outcomes = outcomes + "" },
                        tint = tokens.primary,
                    )
                }
                Text(
                    text = stringResource(Res.string.home_live_ops_prediction_window_label, window.toInt()),
                    style = LocalTypography.current.sm,
                )
                Slider(value = window, onValueChange = { window = it }, valueRange = 30f..1800f)
            }
        },
        confirmButton = {
            Button(
                onClick = {
                    onConfirm(title, outcomes.map { it.trim() }.filter { it.isNotEmpty() }, window.toInt())
                },
                enabled = title.isNotBlank() && nonBlank >= 2,
            ) { Text(stringResource(Res.string.home_live_ops_prediction_confirm)) }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) { Text(stringResource(Res.string.home_live_ops_cancel)) }
        },
    )
}

@Composable
private fun RaidDialog(
    onSearchRaidTargets: suspend (String) -> List<PickerOption>,
    onConfirm: (targetBroadcasterId: String) -> Unit,
    onDismiss: () -> Unit,
) {
    // The picker's PickerRef.id is the Twitch broadcaster id the raid write consumes; the search only finds the
    // channel's own known viewers/chatters by name (the available endpoint).
    var selected: PickerRef? by remember { mutableStateOf(null) }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(Res.string.home_live_ops_start_raid)) },
        text = {
            SearchPickerField(
                search = onSearchRaidTargets,
                selected = selected,
                onSelect = { selected = it },
                onClear = { selected = null },
                label = stringResource(Res.string.channel_picker_label),
                placeholder = stringResource(Res.string.channel_picker_placeholder),
                emptyText = stringResource(Res.string.channel_picker_empty),
                modifier = Modifier.fillMaxWidth(),
            )
        },
        confirmButton = {
            Button(onClick = { selected?.let { onConfirm(it.id) } }, enabled = selected != null) {
                Text(stringResource(Res.string.home_live_ops_raid_confirm))
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) { Text(stringResource(Res.string.home_live_ops_cancel)) }
        },
    )
}

@Composable
private fun CommercialDialog(
    onConfirm: (lengthSeconds: Int) -> Unit,
    onDismiss: () -> Unit,
) {
    val lengths: List<Int> = listOf(30, 60, 90, 120, 150, 180)
    var selected: Int by remember { mutableStateOf(30) }
    val spacing = LocalSpacing.current

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(Res.string.home_live_ops_start_commercial)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
                Text(
                    text = stringResource(Res.string.home_live_ops_commercial_length_label),
                    style = LocalTypography.current.sm,
                )
                @OptIn(ExperimentalLayoutApi::class)
                FlowRow(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                    lengths.forEach { len: Int ->
                        TextButton(
                            onClick = { selected = len },
                            modifier = if (selected == len) Modifier.background(
                                LocalTokens.current.accent,
                                RoundedCornerShape(LocalTokens.current.radius.md),
                            ) else Modifier,
                        ) { Text("${len}s") }
                    }
                }
            }
        },
        confirmButton = {
            Button(onClick = { onConfirm(selected) }) {
                Text(stringResource(Res.string.home_live_ops_commercial_confirm))
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) { Text(stringResource(Res.string.home_live_ops_cancel)) }
        },
    )
}

@Composable
private fun ResolvePredictionDialog(
    prediction: LiveOpsPrediction,
    onConfirm: (winningOutcomeId: String) -> Unit,
    onDismiss: () -> Unit,
) {
    val spacing = LocalSpacing.current

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(Res.string.home_live_ops_resolve_prediction)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
                prediction.outcomes.forEach { outcome ->
                    TextButton(
                        onClick = { onConfirm(outcome.id) },
                        modifier = Modifier.fillMaxWidth(),
                    ) {
                        Text(stringResource(Res.string.home_live_ops_outcome_pick, outcome.title))
                    }
                }
            }
        },
        confirmButton = {},
        dismissButton = {
            TextButton(onClick = onDismiss) { Text(stringResource(Res.string.home_live_ops_cancel)) }
        },
    )
}

// ─── Shared utilities ─────────────────────────────────────────────────────────

@Composable
private fun ErrorContent(detail: String, onRetry: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Column(
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            Text(
                text = stringResource(Res.string.home_error, detail),
                style = typography.base,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.home_retry)) }
        }
    }
}

@Composable
private fun CenteredMessage(text: String) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Text(text = text, style = typography.base, color = tokens.mutedForeground)
    }
}

@Composable
private fun uptimeLabel(seconds: Long?): String =
    if (seconds == null) {
        stringResource(Res.string.home_uptime_offline)
    } else {
        stringResource(
            Res.string.home_uptime_format,
            (seconds / 3600).toInt(),
            ((seconds % 3600) / 60).toInt(),
        )
    }
