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
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Slider
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
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
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.text.style.TextAlign
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.DashboardStats
import bot.nomnomz.dashboard.core.network.LiveOpsPoll
import bot.nomnomz.dashboard.core.network.LiveOpsPrediction
import bot.nomnomz.dashboard.core.realtime.HubEvent
import bot.nomnomz.dashboard.feature.home.state.HomeController
import bot.nomnomz.dashboard.feature.home.state.HomeState
import bot.nomnomz.dashboard.feature.liveops.state.LiveOpsController
import bot.nomnomz.dashboard.feature.liveops.state.LiveOpsState
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.home_error
import nomnomzbot.composeapp.generated.resources.home_subtitle
import nomnomzbot.composeapp.generated.resources.shell_nav_dashboard
import nomnomzbot.composeapp.generated.resources.home_game_label
import nomnomzbot.composeapp.generated.resources.home_live_ops_active_poll
import nomnomzbot.composeapp.generated.resources.home_live_ops_active_prediction
import nomnomzbot.composeapp.generated.resources.home_live_ops_cancel
import nomnomzbot.composeapp.generated.resources.home_live_ops_cancel_prediction
import nomnomzbot.composeapp.generated.resources.home_live_ops_cancel_raid
import nomnomzbot.composeapp.generated.resources.home_live_ops_commercial_confirm
import nomnomzbot.composeapp.generated.resources.home_live_ops_commercial_length_label
import nomnomzbot.composeapp.generated.resources.home_live_ops_create_clip
import nomnomzbot.composeapp.generated.resources.home_live_ops_create_poll
import nomnomzbot.composeapp.generated.resources.home_live_ops_create_prediction
import nomnomzbot.composeapp.generated.resources.home_live_ops_end_poll
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
import nomnomzbot.composeapp.generated.resources.home_stat_commands
import nomnomzbot.composeapp.generated.resources.home_stat_followers
import nomnomzbot.composeapp.generated.resources.home_stat_messages
import nomnomzbot.composeapp.generated.resources.home_stat_uptime
import nomnomzbot.composeapp.generated.resources.home_stat_viewers
import nomnomzbot.composeapp.generated.resources.home_status_live
import nomnomzbot.composeapp.generated.resources.home_status_offline
import nomnomzbot.composeapp.generated.resources.home_uptime_format
import nomnomzbot.composeapp.generated.resources.home_uptime_offline
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource

// The Home page (frontend-ia.md §3): the live channel landing — current stream state + the headline counters,
// all real data from [HomeController]. The screen is a pure projection of the controller's state; it loads on
// first composition and offers a retry on failure.
@Composable
fun HomeScreen(
    controller: HomeController,
    liveOpsController: LiveOpsController,
    hubEvents: SharedFlow<HubEvent>? = null,
) {
    val state: HomeState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    LaunchedEffect(Unit) {
        controller.load()
        liveOpsController.load()
    }

    // Real-time stream status: update isLive immediately when the hub fires stream.online/offline.
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
            is HomeState.Ready -> ReadyContent(stats = current.stats, liveOpsController = liveOpsController)
        }
    }
}

@Composable
private fun ReadyContent(stats: DashboardStats, liveOpsController: LiveOpsController) {
    val spacing = LocalSpacing.current

    Column(
        modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        PageHeader(
            title = stringResource(Res.string.shell_nav_dashboard),
            subtitle = stringResource(Res.string.home_subtitle),
        )
        LiveBanner(stats = stats)
        StatTiles(stats = stats)
        LiveOpsSection(controller = liveOpsController)
    }
}

@Composable
private fun LiveBanner(stats: DashboardStats) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
            .padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Row(
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            Box(
                modifier = Modifier
                    .size(spacing.s2)
                    .clip(CircleShape)
                    .background(if (stats.isLive) tokens.primary else tokens.mutedForeground),
            )
            Text(
                text =
                    stringResource(
                        if (stats.isLive) Res.string.home_status_live else Res.string.home_status_offline
                    ),
                style = typography.sm,
                color = tokens.mutedForeground,
            )
        }
        Text(
            text = stats.streamTitle?.takeIf { it.isNotBlank() }
                ?: stringResource(Res.string.home_no_title),
            style = typography.xl,
            color = tokens.cardForeground,
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

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun StatTiles(stats: DashboardStats) {
    val spacing = LocalSpacing.current

    FlowRow(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        StatTile(Res.string.home_stat_viewers, stats.viewerCount.toString())
        StatTile(Res.string.home_stat_followers, stats.followerCount.toString())
        StatTile(Res.string.home_stat_commands, stats.commandsUsed.toString())
        StatTile(Res.string.home_stat_messages, stats.messagesCount.toString())
        StatTile(Res.string.home_stat_uptime, uptimeLabel(stats.uptime))
    }
}

@Composable
private fun StatTile(labelRes: StringResource, value: String) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val label: String = stringResource(labelRes)

    Column(
        modifier = Modifier
            .width(spacing.s24 * 1.6f)
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
            .padding(spacing.s4)
            // One node for screen readers: "Viewers: 42" rather than two disconnected texts.
            .clearAndSetSemantics { contentDescription = "$label: $value" },
        verticalArrangement = Arrangement.spacedBy(spacing.s1),
    ) {
        Text(text = value, style = typography.xl2, color = tokens.cardForeground)
        Text(text = label, style = typography.sm, color = tokens.mutedForeground)
    }
}

// ─── Live-ops quick-actions ───────────────────────────────────────────────────

@Composable
private fun LiveOpsSection(controller: LiveOpsController) {
    val liveOpsState: LiveOpsState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var showPollDialog: Boolean by remember { mutableStateOf(false) }
    var showPredictionDialog: Boolean by remember { mutableStateOf(false) }
    var showRaidDialog: Boolean by remember { mutableStateOf(false) }
    var showCommercialDialog: Boolean by remember { mutableStateOf(false) }
    var showResolvePredictionDialog: Boolean by remember { mutableStateOf(false) }

    val ready: LiveOpsState.Ready? = liveOpsState as? LiveOpsState.Ready

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
            .padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Text(
            text = stringResource(Res.string.home_live_ops_title),
            style = typography.sm,
            color = tokens.mutedForeground,
        )

        // Active poll info + end action
        ready?.activePoll?.let { poll: LiveOpsPoll ->
            Text(
                text = stringResource(Res.string.home_live_ops_active_poll, poll.title),
                style = typography.sm,
                color = tokens.cardForeground,
            )
            TextButton(onClick = { scope.launch { controller.endPoll("TERMINATED") } }) {
                Text(stringResource(Res.string.home_live_ops_end_poll))
            }
        }

        // Active prediction info + resolve / cancel actions
        ready?.activePrediction?.let { prediction: LiveOpsPrediction ->
            Text(
                text = stringResource(Res.string.home_live_ops_active_prediction, prediction.title),
                style = typography.sm,
                color = tokens.cardForeground,
            )
            Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                TextButton(onClick = { showResolvePredictionDialog = true }) {
                    Text(stringResource(Res.string.home_live_ops_resolve_prediction))
                }
                TextButton(onClick = { scope.launch { controller.cancelPrediction() } }) {
                    Text(stringResource(Res.string.home_live_ops_cancel_prediction))
                }
            }
        }

        // Primary action buttons
        @OptIn(ExperimentalLayoutApi::class)
        FlowRow(
            horizontalArrangement = Arrangement.spacedBy(spacing.s2),
            verticalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            if (ready?.activePoll == null) {
                TextButton(onClick = { showPollDialog = true }) {
                    Text(stringResource(Res.string.home_live_ops_create_poll))
                }
            }
            if (ready?.activePrediction == null) {
                TextButton(onClick = { showPredictionDialog = true }) {
                    Text(stringResource(Res.string.home_live_ops_create_prediction))
                }
            }
            TextButton(onClick = { showRaidDialog = true }) {
                Text(stringResource(Res.string.home_live_ops_start_raid))
            }
            TextButton(onClick = { scope.launch { controller.createClip() } }) {
                Text(stringResource(Res.string.home_live_ops_create_clip))
            }
            if (ready?.adSchedule != null && (ready.adSchedule?.snoozeCount ?: 0) > 0) {
                TextButton(onClick = { scope.launch { controller.snoozeNextAd() } }) {
                    Text(stringResource(Res.string.home_live_ops_snooze_ad))
                }
            }
            TextButton(onClick = { showCommercialDialog = true }) {
                Text(stringResource(Res.string.home_live_ops_start_commercial))
            }
        }
    }

    // ─── Dialogs ──────────────────────────────────────────────────────────────

    if (showPollDialog) {
        PollDialog(
            onConfirm = { title, choices, duration ->
                showPollDialog = false
                scope.launch { controller.createPoll(title, choices, duration) }
            },
            onDismiss = { showPollDialog = false },
        )
    }

    if (showPredictionDialog) {
        PredictionDialog(
            onConfirm = { title, outcomes, window ->
                showPredictionDialog = false
                scope.launch { controller.createPrediction(title, outcomes, window) }
            },
            onDismiss = { showPredictionDialog = false },
        )
    }

    if (showRaidDialog) {
        RaidDialog(
            onConfirm = { target ->
                showRaidDialog = false
                scope.launch { controller.startRaid(target) }
            },
            onDismiss = { showRaidDialog = false },
        )
    }

    if (showCommercialDialog) {
        CommercialDialog(
            onConfirm = { length ->
                showCommercialDialog = false
                scope.launch { controller.startCommercial(length) }
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
                    scope.launch { controller.resolvePrediction(winningId) }
                },
                onDismiss = { showResolvePredictionDialog = false },
            )
        }
    }
}

@Composable
private fun PollDialog(
    onConfirm: (title: String, choices: List<String>, durationSeconds: Int) -> Unit,
    onDismiss: () -> Unit,
) {
    var title: String by remember { mutableStateOf("") }
    var choicesText: String by remember { mutableStateOf("") }
    var duration: Float by remember { mutableStateOf(60f) }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(Res.string.home_live_ops_create_poll)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(LocalSpacing.current.s3)) {
                OutlinedTextField(
                    value = title,
                    onValueChange = { title = it },
                    label = { Text(stringResource(Res.string.home_live_ops_poll_title_label)) },
                    modifier = Modifier.fillMaxWidth(),
                )
                OutlinedTextField(
                    value = choicesText,
                    onValueChange = { choicesText = it },
                    label = { Text(stringResource(Res.string.home_live_ops_poll_choices_label)) },
                    minLines = 3,
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

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(Res.string.home_live_ops_create_prediction)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(LocalSpacing.current.s3)) {
                OutlinedTextField(
                    value = title,
                    onValueChange = { title = it },
                    label = { Text(stringResource(Res.string.home_live_ops_prediction_title_label)) },
                    modifier = Modifier.fillMaxWidth(),
                )
                OutlinedTextField(
                    value = outcomesText,
                    onValueChange = { outcomesText = it },
                    label = { Text(stringResource(Res.string.home_live_ops_prediction_outcomes_label)) },
                    minLines = 2,
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
            OutlinedTextField(
                value = target,
                onValueChange = { target = it },
                label = { Text(stringResource(Res.string.home_live_ops_raid_target_label)) },
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

/** Render uptime seconds as "Xh Ym", or the offline placeholder when there is no live stream. */
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
