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
import androidx.compose.ui.graphics.vector.ImageVector
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
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecisionAtFloor
import bot.nomnomz.dashboard.core.designsystem.icon.AddGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.ArrowUpGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.CheckCircleGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.CheckGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.CopyGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.EditGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.PlayCircleGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.RefreshGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.RemoveGlyph
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.ActivityEvent
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
import nomnomzbot.composeapp.generated.resources.home_activity_ban
import nomnomzbot.composeapp.generated.resources.home_activity_cheer
import nomnomzbot.composeapp.generated.resources.home_activity_empty
import nomnomzbot.composeapp.generated.resources.home_activity_event
import nomnomzbot.composeapp.generated.resources.home_activity_follow
import nomnomzbot.composeapp.generated.resources.home_activity_mod_add
import nomnomzbot.composeapp.generated.resources.home_activity_mod_remove
import nomnomzbot.composeapp.generated.resources.home_activity_raid
import nomnomzbot.composeapp.generated.resources.home_activity_redemption
import nomnomzbot.composeapp.generated.resources.home_activity_resub
import nomnomzbot.composeapp.generated.resources.home_activity_section
import nomnomzbot.composeapp.generated.resources.home_activity_subscribe
import nomnomzbot.composeapp.generated.resources.home_activity_subscription_gift
import nomnomzbot.composeapp.generated.resources.home_activity_timeout
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
import org.jetbrains.compose.resources.stringResource

// The Home page (frontend-ia.md §3): the live channel landing — current stream state, headline counters,
// recent activity feed, and quick-action panel. Pure projection of [HomeController] state.
@Composable
fun HomeScreen(
    controller: HomeController,
    liveOpsController: LiveOpsController,
    chatPollsController: ChatPollsController,
    role: ManagementRole? = null,
    hubEvents: SharedFlow<HubEvent>? = null,
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

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
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
                    streamError = current.streamError,
                    liveOpsController = liveOpsController,
                    chatPollsController = chatPollsController,
                    role = role,
                    onUpdateStream = { title, game, tags ->
                        scope.launch { controller.updateStreamInfo(title, game, tags) }
                    },
                    onSearchCategories = controller::searchCategories,
                    onSearchRaidTargets = controller::searchRaidTargets,
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
    streamError: String?,
    liveOpsController: LiveOpsController,
    chatPollsController: ChatPollsController,
    role: ManagementRole?,
    onUpdateStream: (title: String?, game: String?, tags: List<String>?) -> Unit,
    onSearchCategories: suspend (String) -> List<PickerOption>,
    onSearchRaidTargets: suspend (String) -> List<PickerOption>,
) {
    val spacing = LocalSpacing.current
    val scope = rememberCoroutineScope()
    val liveOpsState: LiveOpsState by liveOpsController.state.collectAsStateWithLifecycle()
    val ready: LiveOpsState.Ready? = liveOpsState as? LiveOpsState.Ready

    // The live-ops quick actions (raid / poll / prediction / commercial / clip / marker) are broadcaster-delegable
    // operator actions; they gate at the Editor floor — matching the Schedule / live-ops:schedule floor — so a
    // caller below it sees them DISABLED with a "Requires Editor" reason rather than a tap that 403s. The Dashboard
    // page itself is read-only (null nav manage floor), so this uses an explicit floor rather than the page's.
    val manage: ManageDecision = rememberManageDecisionAtFloor(role, ManagementRole.Editor)

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
        modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        PageHeader(
            title = stringResource(Res.string.shell_nav_dashboard),
            subtitle = stringResource(Res.string.home_subtitle),
        )

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
                modifier = Modifier.weight(1.6f),
            )

            Column(
                modifier = Modifier.weight(1f),
                verticalArrangement = Arrangement.spacedBy(spacing.s4),
            ) {
                QuickActionsCard(
                    ready = ready,
                    isLive = stats.isLive,
                    manage = manage,
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

@Composable
private fun ActivityFeedCard(events: List<ActivityEvent>, modifier: Modifier = Modifier) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Card(modifier = modifier) {
    Column(
        modifier = Modifier.padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Text(
            text = stringResource(Res.string.home_activity_section),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
        if (events.isEmpty()) {
            Text(
                text = stringResource(Res.string.home_activity_empty),
                style = typography.sm,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
                modifier = Modifier.fillMaxWidth().padding(vertical = spacing.s4),
            )
        } else {
            events.forEach { event -> ActivityRow(event = event) }
        }
    }
    }
}

@Composable
private fun ActivityRow(event: ActivityEvent) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val who: String = event.username ?: "—"
    val label: String = when (event.type) {
        "channel.follow" -> stringResource(Res.string.home_activity_follow, who)
        "channel.subscribe" -> stringResource(Res.string.home_activity_subscribe, who)
        "channel.subscription.message" -> stringResource(Res.string.home_activity_resub, who)
        "channel.subscription.gift" -> stringResource(Res.string.home_activity_subscription_gift, who)
        "channel.cheer" -> stringResource(Res.string.home_activity_cheer, who)
        "channel.raid" -> stringResource(Res.string.home_activity_raid, who)
        "channel.channel_points_custom_reward_redemption.add" -> stringResource(Res.string.home_activity_redemption, who)
        "channel.ban" -> stringResource(Res.string.home_activity_ban, who)
        "channel.timeout" -> stringResource(Res.string.home_activity_timeout, who)
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
            Text(
                text = label,
                style = typography.sm,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
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
    manage: ManageDecision,
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
            Text(
                text = stringResource(Res.string.home_live_ops_active_poll, poll.title),
                style = typography.xs,
                color = tokens.primary,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
        activePrediction?.let { prediction ->
            Text(
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
            GatedQuickAction(manage = manage, modifier = Modifier.weight(1f)) { enabled, mod ->
                QuickActionButton(
                    icon = EditGlyph,
                    label = stringResource(Res.string.home_change_title),
                    onClick = onChangeTitle,
                    enabled = enabled,
                    modifier = mod,
                )
            }

            if (activePoll == null) {
                GatedQuickAction(manage = manage, modifier = Modifier.weight(1f)) { enabled, mod ->
                    QuickActionButton(
                        icon = CheckGlyph,
                        label = stringResource(Res.string.home_live_ops_create_poll),
                        onClick = onStartPoll,
                        enabled = enabled,
                        modifier = mod,
                    )
                }
            } else {
                GatedQuickAction(manage = manage, modifier = Modifier.weight(1f)) { enabled, mod ->
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
                GatedQuickAction(manage = manage, modifier = Modifier.weight(1f)) { enabled, mod ->
                    QuickActionButton(
                        icon = ArrowUpGlyph,
                        label = stringResource(Res.string.home_live_ops_create_prediction),
                        onClick = onStartPrediction,
                        enabled = enabled,
                        modifier = mod,
                    )
                }
            } else {
                GatedQuickAction(manage = manage, modifier = Modifier.weight(1f)) { enabled, mod ->
                    QuickActionButton(
                        icon = CheckCircleGlyph,
                        label = stringResource(Res.string.home_live_ops_resolve_prediction),
                        onClick = onResolvePrediction,
                        enabled = enabled,
                        modifier = mod,
                    )
                }
                GatedQuickAction(manage = manage, modifier = Modifier.weight(1f)) { enabled, mod ->
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

            GatedQuickAction(manage = manage, modifier = Modifier.weight(1f)) { enabled, mod ->
                QuickActionButton(
                    icon = CopyGlyph,
                    label = stringResource(Res.string.home_live_ops_create_clip),
                    onClick = onCreateClip,
                    enabled = enabled,
                    modifier = mod,
                )
            }

            // "Mark this moment" — a VOD bookmark. Twitch only accepts markers while LIVE, so the button is
            // shown only when the channel is live (rather than offering a tap that would always fail offline).
            if (isLive) {
                GatedQuickAction(manage = manage, modifier = Modifier.weight(1f)) { enabled, mod ->
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
                GatedQuickAction(manage = manage, modifier = Modifier.weight(1f)) { enabled, mod ->
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
                GatedQuickAction(manage = manage, modifier = Modifier.weight(1f)) { enabled, mod ->
                    QuickActionButton(
                        icon = ArrowUpGlyph,
                        label = stringResource(Res.string.home_live_ops_start_raid),
                        onClick = onStartRaid,
                        enabled = enabled,
                        modifier = mod,
                    )
                }
            }

            GatedQuickAction(manage = manage, modifier = Modifier.weight(1f)) { enabled, mod ->
                QuickActionButton(
                    icon = PlayCircleGlyph,
                    label = stringResource(Res.string.home_live_ops_start_commercial),
                    onClick = onStartCommercial,
                    enabled = enabled,
                    modifier = mod,
                )
            }

            if (ready?.adSchedule != null && (ready.adSchedule?.snoozeCount ?: 0) > 0) {
                GatedQuickAction(manage = manage, modifier = Modifier.weight(1f)) { enabled, mod ->
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
    manage: ManageDecision,
    modifier: Modifier = Modifier,
    button: @Composable (enabled: Boolean, modifier: Modifier) -> Unit,
) {
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
    icon: ImageVector,
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
            imageVector = icon,
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
                Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                    Badge(selected = target == PollTarget.Chat, onClick = { target = PollTarget.Chat }) {
                        Text(stringResource(Res.string.home_poll_target_chat), maxLines = 1)
                    }
                    Badge(selected = target == PollTarget.Twitch, onClick = { target = PollTarget.Twitch }) {
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
                                imageVector = RemoveGlyph,
                                label = stringResource(Res.string.chat_poll_option_label, index + 1),
                                onClick = { options = options.toMutableList().also { it.removeAt(index) } },
                                tint = tokens.destructive,
                            )
                        }
                    }
                }
                if (options.size < maxOptions) {
                    GlyphButton(
                        imageVector = AddGlyph,
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
                                imageVector = RemoveGlyph,
                                label = stringResource(Res.string.chat_poll_option_label, index + 1),
                                onClick = { outcomes = outcomes.toMutableList().also { it.removeAt(index) } },
                                tint = tokens.destructive,
                            )
                        }
                    }
                }
                if (outcomes.size < 10) {
                    GlyphButton(
                        imageVector = AddGlyph,
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
