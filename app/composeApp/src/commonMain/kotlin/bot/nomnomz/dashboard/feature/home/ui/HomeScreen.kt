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
import bot.nomnomz.dashboard.core.designsystem.component.AlertDialog
import bot.nomnomz.dashboard.core.designsystem.component.Badge
import bot.nomnomz.dashboard.core.designsystem.component.BadgeVariant
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.Card
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
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
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
import nomnomzbot.composeapp.generated.resources.home_live_ops_commercial_confirm
import nomnomzbot.composeapp.generated.resources.home_live_ops_commercial_length_label
import nomnomzbot.composeapp.generated.resources.home_live_ops_create_clip
import nomnomzbot.composeapp.generated.resources.home_live_ops_create_poll
import nomnomzbot.composeapp.generated.resources.home_live_ops_create_prediction
import nomnomzbot.composeapp.generated.resources.home_live_ops_end_poll
import nomnomzbot.composeapp.generated.resources.home_live_ops_mark_moment
import nomnomzbot.composeapp.generated.resources.home_live_ops_mark_moment_done
import nomnomzbot.composeapp.generated.resources.home_live_ops_mark_moment_failed
import nomnomzbot.composeapp.generated.resources.home_live_ops_outcome_pick
import nomnomzbot.composeapp.generated.resources.home_live_ops_poll_choices_label
import nomnomzbot.composeapp.generated.resources.home_live_ops_poll_confirm
import nomnomzbot.composeapp.generated.resources.home_live_ops_poll_duration_label
import nomnomzbot.composeapp.generated.resources.home_live_ops_poll_title_label
import nomnomzbot.composeapp.generated.resources.home_live_ops_prediction_confirm
import nomnomzbot.composeapp.generated.resources.home_live_ops_prediction_outcomes_label
import nomnomzbot.composeapp.generated.resources.home_live_ops_prediction_title_label
import nomnomzbot.composeapp.generated.resources.home_live_ops_prediction_window_label
import nomnomzbot.composeapp.generated.resources.home_live_ops_raid_confirm
import nomnomzbot.composeapp.generated.resources.home_live_ops_raid_target_label
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
import nomnomzbot.composeapp.generated.resources.home_stat_subscribers
import nomnomzbot.composeapp.generated.resources.home_stat_uptime
import nomnomzbot.composeapp.generated.resources.home_stat_viewers
import nomnomzbot.composeapp.generated.resources.home_platforms_label
import nomnomzbot.composeapp.generated.resources.home_platforms_offline
import nomnomzbot.composeapp.generated.resources.home_status_live
import nomnomzbot.composeapp.generated.resources.home_status_offline
import nomnomzbot.composeapp.generated.resources.home_stream_error
import nomnomzbot.composeapp.generated.resources.home_stream_game_label
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
                    onUpdateStream = { title, game, tags ->
                        scope.launch { controller.updateStreamInfo(title, game, tags) }
                    },
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
    onUpdateStream: (title: String?, game: String?, tags: List<String>?) -> Unit,
) {
    val spacing = LocalSpacing.current
    val scope = rememberCoroutineScope()
    val liveOpsState: LiveOpsState by liveOpsController.state.collectAsStateWithLifecycle()
    val ready: LiveOpsState.Ready? = liveOpsState as? LiveOpsState.Ready

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
                    onChangeTitle = { showChangeTitleDialog = true },
                    onCreateClip = { scope.launch { liveOpsController.createClip() } },
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
                    onStartCommercial = { showCommercialDialog = true },
                    onSnoozeAd = { scope.launch { liveOpsController.snoozeNextAd() } },
                )

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
            onSave = { title, game, tags ->
                showChangeTitleDialog = false
                onUpdateStream(title, game, tags)
            },
            onDismiss = { showChangeTitleDialog = false },
        )
    }

    if (showPollDialog) {
        PollDialog(
            onConfirm = { title, choices, duration ->
                showPollDialog = false
                scope.launch { liveOpsController.createPoll(title, choices, duration) }
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
            onConfirm = { target ->
                showRaidDialog = false
                scope.launch { liveOpsController.startRaid(target) }
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
    onChangeTitle: () -> Unit,
    onCreateClip: () -> Unit,
    onMarkMoment: () -> Unit,
    onStartPoll: () -> Unit,
    onEndPoll: () -> Unit,
    onStartPrediction: () -> Unit,
    onResolvePrediction: () -> Unit,
    onCancelPrediction: () -> Unit,
    onStartRaid: () -> Unit,
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
            QuickActionButton(
                icon = EditGlyph,
                label = stringResource(Res.string.home_change_title),
                onClick = onChangeTitle,
                modifier = Modifier.weight(1f),
            )

            if (activePoll == null) {
                QuickActionButton(
                    icon = CheckGlyph,
                    label = stringResource(Res.string.home_live_ops_create_poll),
                    onClick = onStartPoll,
                    modifier = Modifier.weight(1f),
                )
            } else {
                QuickActionButton(
                    icon = CheckCircleGlyph,
                    label = stringResource(Res.string.home_live_ops_end_poll),
                    onClick = onEndPoll,
                    modifier = Modifier.weight(1f),
                    destructive = true,
                )
            }

            if (activePrediction == null) {
                QuickActionButton(
                    icon = ArrowUpGlyph,
                    label = stringResource(Res.string.home_live_ops_create_prediction),
                    onClick = onStartPrediction,
                    modifier = Modifier.weight(1f),
                )
            } else {
                QuickActionButton(
                    icon = CheckCircleGlyph,
                    label = stringResource(Res.string.home_live_ops_resolve_prediction),
                    onClick = onResolvePrediction,
                    modifier = Modifier.weight(1f),
                )
                QuickActionButton(
                    icon = RemoveGlyph,
                    label = stringResource(Res.string.home_live_ops_cancel_prediction),
                    onClick = onCancelPrediction,
                    modifier = Modifier.weight(1f),
                    destructive = true,
                )
            }

            QuickActionButton(
                icon = CopyGlyph,
                label = stringResource(Res.string.home_live_ops_create_clip),
                onClick = onCreateClip,
                modifier = Modifier.weight(1f),
            )

            // "Mark this moment" — a VOD bookmark. Twitch only accepts markers while LIVE, so the button is
            // shown only when the channel is live (rather than offering a tap that would always fail offline).
            if (isLive) {
                QuickActionButton(
                    icon = AddGlyph,
                    label = stringResource(Res.string.home_live_ops_mark_moment),
                    onClick = onMarkMoment,
                    modifier = Modifier.weight(1f),
                )
            }

            QuickActionButton(
                icon = ArrowUpGlyph,
                label = stringResource(Res.string.home_live_ops_start_raid),
                onClick = onStartRaid,
                modifier = Modifier.weight(1f),
            )

            QuickActionButton(
                icon = PlayCircleGlyph,
                label = stringResource(Res.string.home_live_ops_start_commercial),
                onClick = onStartCommercial,
                modifier = Modifier.weight(1f),
            )

            if (ready?.adSchedule != null && (ready.adSchedule?.snoozeCount ?: 0) > 0) {
                QuickActionButton(
                    icon = RefreshGlyph,
                    label = stringResource(Res.string.home_live_ops_snooze_ad),
                    onClick = onSnoozeAd,
                    modifier = Modifier.weight(1f),
                )
            }
        }
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
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val iconTint = if (destructive) tokens.destructive else tokens.mutedForeground
    val labelColor = if (destructive) tokens.destructive else tokens.cardForeground

    Column(
        modifier = modifier
            .clip(RoundedCornerShape(tokens.radius.md))
            .background(tokens.muted)
            .clickable(onClick = onClick)
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
    onSave: (title: String?, game: String?, tags: List<String>?) -> Unit,
    onDismiss: () -> Unit,
) {
    var editTitle: String by remember(streamInfo?.title) { mutableStateOf(streamInfo?.title ?: "") }
    var editGame: String by remember(streamInfo?.gameName) { mutableStateOf(streamInfo?.gameName ?: "") }
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
                AppTextField(
                    value = editGame,
                    onValueChange = { editGame = it },
                    label = stringResource(Res.string.home_stream_game_label),
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
                    val tags: List<String>? =
                        editTags.split(",").map { it.trim() }.filter { it.isNotEmpty() }
                            .takeIf { it.isNotEmpty() }
                    onSave(
                        editTitle.trim().takeIf { it.isNotEmpty() },
                        editGame.trim().takeIf { it.isNotEmpty() },
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

@Composable
private fun PollDialog(
    onConfirm: (title: String, choices: List<String>, durationSeconds: Int) -> Unit,
    onDismiss: () -> Unit,
) {
    var title: String by remember { mutableStateOf("") }
    var choicesText: String by remember { mutableStateOf("") }
    var duration: Float by remember { mutableStateOf(60f) }
    val spacing = LocalSpacing.current

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(Res.string.home_live_ops_create_poll)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                AppTextField(
                    value = title,
                    onValueChange = { title = it },
                    label = stringResource(Res.string.home_live_ops_poll_title_label),
                    modifier = Modifier.fillMaxWidth(),
                )
                AppTextField(
                    value = choicesText,
                    onValueChange = { choicesText = it },
                    label = stringResource(Res.string.home_live_ops_poll_choices_label),
                    modifier = Modifier.fillMaxWidth(),
                )
                Text(
                    text = stringResource(Res.string.home_live_ops_poll_duration_label, duration.toInt()),
                    style = LocalTypography.current.sm,
                )
                Slider(value = duration, onValueChange = { duration = it }, valueRange = 15f..1800f)
            }
        },
        confirmButton = {
            Button(
                onClick = {
                    val choices: List<String> = choicesText.lines().map { it.trim() }.filter { it.isNotEmpty() }
                    onConfirm(title, choices, duration.toInt())
                },
                enabled = title.isNotBlank() && choicesText.lines().count { it.isNotBlank() } >= 2,
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
    var outcomesText: String by remember { mutableStateOf("") }
    var window: Float by remember { mutableStateOf(120f) }
    val spacing = LocalSpacing.current

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
                AppTextField(
                    value = outcomesText,
                    onValueChange = { outcomesText = it },
                    label = stringResource(Res.string.home_live_ops_prediction_outcomes_label),
                    modifier = Modifier.fillMaxWidth(),
                )
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
                    val outcomes: List<String> = outcomesText.lines().map { it.trim() }.filter { it.isNotEmpty() }
                    onConfirm(title, outcomes, window.toInt())
                },
                enabled = title.isNotBlank() && outcomesText.lines().count { it.isNotBlank() } >= 2,
            ) { Text(stringResource(Res.string.home_live_ops_prediction_confirm)) }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) { Text(stringResource(Res.string.home_live_ops_cancel)) }
        },
    )
}

@Composable
private fun RaidDialog(
    onConfirm: (targetBroadcasterId: String) -> Unit,
    onDismiss: () -> Unit,
) {
    var target: String by remember { mutableStateOf("") }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(Res.string.home_live_ops_start_raid)) },
        text = {
            AppTextField(
                value = target,
                onValueChange = { target = it },
                label = stringResource(Res.string.home_live_ops_raid_target_label),
                modifier = Modifier.fillMaxWidth(),
            )
        },
        confirmButton = {
            Button(onClick = { onConfirm(target.trim()) }, enabled = target.isNotBlank()) {
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
