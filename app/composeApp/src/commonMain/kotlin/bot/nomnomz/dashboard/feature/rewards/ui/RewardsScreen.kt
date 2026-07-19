// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.rewards.ui

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import bot.nomnomz.dashboard.core.designsystem.component.AlertDialog
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.ColorField
import bot.nomnomz.dashboard.core.designsystem.component.parseHexColor
import bot.nomnomz.dashboard.core.designsystem.component.ButtonSize
import bot.nomnomz.dashboard.core.designsystem.component.ButtonVariant
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenu
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenuItem
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Switch
import androidx.compose.material3.Text
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
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.ActionErrorBanner
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.GlyphButton
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.icon.CheckCircleGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.EditGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.RemoveGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.TrashGlyph
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.PipelineSummary
import bot.nomnomz.dashboard.core.network.RedemptionSummary
import bot.nomnomz.dashboard.core.network.RedemptionTimer
import bot.nomnomz.dashboard.core.network.RewardSummary
import bot.nomnomz.dashboard.feature.rewards.state.RewardsController
import bot.nomnomz.dashboard.feature.rewards.state.RewardsState
import bot.nomnomz.dashboard.feature.shell.nav.ManageAction
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.rewards_action_error
import nomnomzbot.composeapp.generated.resources.rewards_cost
import nomnomzbot.composeapp.generated.resources.rewards_delete_action
import nomnomzbot.composeapp.generated.resources.rewards_delete_action_short
import nomnomzbot.composeapp.generated.resources.rewards_delete_cancel
import nomnomzbot.composeapp.generated.resources.rewards_delete_confirm
import nomnomzbot.composeapp.generated.resources.rewards_delete_message
import nomnomzbot.composeapp.generated.resources.rewards_delete_title
import nomnomzbot.composeapp.generated.resources.rewards_dialog_background_color_label
import nomnomzbot.composeapp.generated.resources.rewards_dialog_cancel
import nomnomzbot.composeapp.generated.resources.rewards_dialog_cooldown_label
import nomnomzbot.composeapp.generated.resources.rewards_dialog_cost_label
import nomnomzbot.composeapp.generated.resources.rewards_dialog_create
import nomnomzbot.composeapp.generated.resources.rewards_dialog_create_title
import nomnomzbot.composeapp.generated.resources.rewards_dialog_edit_title
import nomnomzbot.composeapp.generated.resources.rewards_dialog_enabled_label
import nomnomzbot.composeapp.generated.resources.rewards_dialog_max_per_stream_label
import nomnomzbot.composeapp.generated.resources.rewards_dialog_max_per_user_label
import nomnomzbot.composeapp.generated.resources.rewards_dialog_paused_label
import nomnomzbot.composeapp.generated.resources.rewards_dialog_prompt_label
import nomnomzbot.composeapp.generated.resources.rewards_dialog_require_input_label
import nomnomzbot.composeapp.generated.resources.rewards_dialog_save
import nomnomzbot.composeapp.generated.resources.rewards_dialog_title_label
import nomnomzbot.composeapp.generated.resources.rewards_disabled
import nomnomzbot.composeapp.generated.resources.rewards_edit_action
import nomnomzbot.composeapp.generated.resources.rewards_edit_action_short
import nomnomzbot.composeapp.generated.resources.rewards_empty
import nomnomzbot.composeapp.generated.resources.rewards_enabled
import nomnomzbot.composeapp.generated.resources.rewards_error
import nomnomzbot.composeapp.generated.resources.rewards_loading
import nomnomzbot.composeapp.generated.resources.rewards_external_readonly_reason
import nomnomzbot.composeapp.generated.resources.rewards_import_action
import nomnomzbot.composeapp.generated.resources.rewards_new_action
import nomnomzbot.composeapp.generated.resources.rewards_recreate_action
import nomnomzbot.composeapp.generated.resources.rewards_sync_action
import nomnomzbot.composeapp.generated.resources.rewards_queue_by
import nomnomzbot.composeapp.generated.resources.rewards_queue_fulfill
import nomnomzbot.composeapp.generated.resources.rewards_queue_fulfill_action
import nomnomzbot.composeapp.generated.resources.rewards_queue_refund
import nomnomzbot.composeapp.generated.resources.rewards_queue_refund_action
import nomnomzbot.composeapp.generated.resources.rewards_queue_row
import nomnomzbot.composeapp.generated.resources.rewards_queue_title
import nomnomzbot.composeapp.generated.resources.rewards_retry
import nomnomzbot.composeapp.generated.resources.rewards_row_description
import nomnomzbot.composeapp.generated.resources.rewards_title
import nomnomzbot.composeapp.generated.resources.rewards_toggle_action
import bot.nomnomz.dashboard.core.realtime.HubEvent
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.SharedFlow
import nomnomzbot.composeapp.generated.resources.rewards_dialog_pipeline_label
import nomnomzbot.composeapp.generated.resources.rewards_dialog_pipeline_none
import nomnomzbot.composeapp.generated.resources.rewards_dialog_timer_label
import nomnomzbot.composeapp.generated.resources.rewards_timer_cancel
import nomnomzbot.composeapp.generated.resources.rewards_timer_complete
import nomnomzbot.composeapp.generated.resources.rewards_timer_pause
import nomnomzbot.composeapp.generated.resources.rewards_timer_remaining
import nomnomzbot.composeapp.generated.resources.rewards_timer_resume
import nomnomzbot.composeapp.generated.resources.rewards_timer_status_canceled
import nomnomzbot.composeapp.generated.resources.rewards_timer_status_completed
import nomnomzbot.composeapp.generated.resources.rewards_timer_status_paused
import nomnomzbot.composeapp.generated.resources.rewards_timer_status_running
import nomnomzbot.composeapp.generated.resources.rewards_timers_title
import org.jetbrains.compose.resources.stringResource

// The Rewards page (frontend-ia.md §3): the channel's channel-point rewards — every reward is real data from
// [RewardsController] (the backend sources it from Twitch's Helix Custom Rewards endpoint). The screen is a pure
// projection of the controller's state; it loads on first composition. This is the full management surface —
// create, edit, enable/disable, and delete — each routed back through the controller, which re-lists after every
// successful write so the page reflects the backend.
@Composable
fun RewardsScreen(
    controller: RewardsController,
    role: ManagementRole?,
    hubEvents: SharedFlow<HubEvent>? = null,
) {
    val state: RewardsState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    // Rewards splits its write floor (frontend-ia.md §3 Loyalty row): editing/toggling a reward gates at the
    // page's Editor floor, but CREATING or DELETING one is a Broadcaster-only lifecycle action. Two decisions,
    // each resolved once and handed to the matching controls — an Editor sees Edit live and New/Delete disabled
    // with "Requires Broadcaster" (§7); the backend re-checks every write.
    val edit: ManageDecision = rememberManageDecision(role, ShellRoute.Rewards)
    val lifecycle: ManageDecision =
        rememberManageDecision(role, ShellRoute.Rewards, ManageAction.RewardLifecycle)

    // The create/edit dialog target: null = closed, a value = open (an empty editor = create, a pre-filled one
    // = edit). The delete-confirm target is the reward pending confirmation, or null when none.
    var editor: RewardEditor? by remember { mutableStateOf(null) }
    var pendingDelete: RewardSummary? by remember { mutableStateOf(null) }

    LaunchedEffect(Unit) { controller.load() }
    if (hubEvents != null) {
        LaunchedEffect(hubEvents) { controller.subscribeToHub(hubEvents) }
    }
    // Keep the live countdowns fresh: while any timer is running, re-fetch the (clock-derived) remaining seconds
    // every few seconds so the displayed values stay accurate without a full page reload.
    val hasRunningTimer: Boolean =
        (state as? RewardsState.Ready)?.timers?.any { it.status == "running" } == true
    if (hasRunningTimer) {
        LaunchedEffect(Unit) {
            while (true) {
                delay(3000)
                controller.refreshTimers()
            }
        }
    }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: RewardsState = state) {
            is RewardsState.Loading -> CenteredMessage(stringResource(Res.string.rewards_loading))
            is RewardsState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is RewardsState.Empty ->
                ManagedContent(
                    rewards = emptyList(),
                    redemptions = emptyList(),
                    timers = emptyList(),
                    actionError = null,
                    edit = edit,
                    lifecycle = lifecycle,
                    onNew = { editor = RewardEditor.create() },
                    onSync = { scope.launch { controller.sync() } },
                    onImport = { scope.launch { controller.import() } },
                    onEdit = { reward -> editor = RewardEditor.edit(reward) },
                    onToggle = { reward, enabled ->
                        scope.launch { controller.toggleReward(reward.id, enabled) }
                    },
                    onDelete = { reward -> pendingDelete = reward },
                    onRecreate = { reward -> scope.launch { controller.recreate(reward.id) } },
                    onFulfill = { redemption ->
                        scope.launch { controller.fulfillRedemption(redemption.redemptionId) }
                    },
                    onRefund = { redemption ->
                        scope.launch { controller.refundRedemption(redemption.redemptionId) }
                    },
                    onTimerAction = { _, _ -> },
                )
            is RewardsState.Ready ->
                ManagedContent(
                    rewards = current.rewards,
                    redemptions = current.redemptions,
                    timers = current.timers,
                    actionError = current.actionError,
                    edit = edit,
                    lifecycle = lifecycle,
                    onNew = { editor = RewardEditor.create() },
                    onSync = { scope.launch { controller.sync() } },
                    onImport = { scope.launch { controller.import() } },
                    onEdit = { reward -> editor = RewardEditor.edit(reward) },
                    onToggle = { reward, enabled ->
                        scope.launch { controller.toggleReward(reward.id, enabled) }
                    },
                    onDelete = { reward -> pendingDelete = reward },
                    onRecreate = { reward -> scope.launch { controller.recreate(reward.id) } },
                    onFulfill = { redemption ->
                        scope.launch { controller.fulfillRedemption(redemption.redemptionId) }
                    },
                    onRefund = { redemption ->
                        scope.launch { controller.refundRedemption(redemption.redemptionId) }
                    },
                    onTimerAction = { timerId, action ->
                        scope.launch {
                            when (action) {
                                TimerAction.Pause -> controller.pauseTimer(timerId)
                                TimerAction.Resume -> controller.resumeTimer(timerId)
                                TimerAction.Complete -> controller.completeTimer(timerId)
                                TimerAction.Cancel -> controller.cancelTimer(timerId)
                            }
                        }
                    },
                )
        }
    }

    editor?.let { open ->
        val pipelines: List<PipelineSummary> = (state as? RewardsState.Ready)?.pipelines ?: emptyList()
        RewardFormDialog(
            editor = open,
            pipelines = pipelines,
            onDismiss = { editor = null },
            onSubmit = { result ->
                editor = null
                scope.launch {
                    if (open.isEdit)
                        controller.updateReward(
                            open.id,
                            result.title,
                            result.cost,
                            result.prompt,
                            result.isEnabled,
                            result.isPaused,
                            result.isUserInputRequired,
                            result.backgroundColor,
                            result.maxPerStream,
                            result.maxPerUserPerStream,
                            result.globalCooldownSeconds,
                            result.timerDurationSeconds,
                            result.pipelineId,
                        )
                    else
                        controller.createReward(
                            result.title,
                            result.cost,
                            result.prompt,
                            result.isUserInputRequired,
                            result.backgroundColor,
                            result.maxPerStream,
                            result.maxPerUserPerStream,
                            result.globalCooldownSeconds,
                            result.timerDurationSeconds,
                            result.pipelineId,
                        )
                }
            },
        )
    }

    pendingDelete?.let { reward ->
        ConfirmDialog(
            title = stringResource(Res.string.rewards_delete_title),
            message = stringResource(Res.string.rewards_delete_message, reward.title),
            confirmLabel = stringResource(Res.string.rewards_delete_confirm),
            dismissLabel = stringResource(Res.string.rewards_delete_cancel),
            destructive = true,
            onConfirm = {
                pendingDelete = null
                scope.launch { controller.deleteReward(reward.id) }
            },
            onDismiss = { pendingDelete = null },
        )
    }
}

// The list-bearing content: the header with the "+ New reward" action, an optional write-failure banner, and
// either the rows or the empty hint. Shared by the Ready and Empty states so a fresh channel can still create
// its first reward from the same header.
@Composable
private fun ManagedContent(
    rewards: List<RewardSummary>,
    redemptions: List<RedemptionSummary>,
    timers: List<RedemptionTimer>,
    actionError: String?,
    edit: ManageDecision,
    lifecycle: ManageDecision,
    onNew: () -> Unit,
    onSync: () -> Unit,
    onImport: () -> Unit,
    onEdit: (RewardSummary) -> Unit,
    onToggle: (RewardSummary, Boolean) -> Unit,
    onDelete: (RewardSummary) -> Unit,
    onRecreate: (RewardSummary) -> Unit,
    onFulfill: (RedemptionSummary) -> Unit,
    onRefund: (RedemptionSummary) -> Unit,
    onTimerAction: (timerId: String, action: TimerAction) -> Unit,
) {
    val spacing = LocalSpacing.current

    Column(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        // Creating/syncing/importing rewards are Broadcaster-only lifecycle actions — New + Sync + Import gate on [lifecycle].
        Header(lifecycle = lifecycle, onNew = onNew, onSync = onSync, onImport = onImport)
        actionError?.let { ActionErrorBanner(message = stringResource(Res.string.rewards_action_error, it)) }

        if (rewards.isEmpty() && redemptions.isEmpty() && timers.isEmpty()) {
            CenteredMessage(stringResource(Res.string.rewards_empty))
        } else {
            RewardList(
                rewards = rewards,
                redemptions = redemptions,
                timers = timers,
                edit = edit,
                lifecycle = lifecycle,
                onEdit = onEdit,
                onToggle = onToggle,
                onDelete = onDelete,
                onRecreate = onRecreate,
                onFulfill = onFulfill,
                onRefund = onRefund,
                onTimerAction = onTimerAction,
            )
        }
    }
}

/** The four lifecycle actions a redemption countdown timer offers on the card. */
enum class TimerAction { Pause, Resume, Complete, Cancel }

@Composable
private fun Header(
    lifecycle: ManageDecision,
    onNew: () -> Unit,
    onSync: () -> Unit,
    onImport: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val newLabel: String = stringResource(Res.string.rewards_new_action)
    val syncLabel: String = stringResource(Res.string.rewards_sync_action)
    val importLabel: String = stringResource(Res.string.rewards_import_action)

    PageHeader(title = stringResource(Res.string.rewards_title)) {
        ManageGate(decision = lifecycle) { enabled ->
            // Sync refreshes only the bot's own rewards; Import pulls EVERYTHING incl. external ones. Both are
            // Twitch-pull text actions; New creates a fresh reward. All three gate on the same lifecycle floor.
            // They MUST sit in a Row: ManageGate wraps its content in a Box, so three bare siblings would stack
            // and overlap (the "overlapping top-right buttons" bug) — the Row lays them out side by side.
            Row(
                horizontalArrangement = Arrangement.spacedBy(spacing.s2),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                TextButton(
                    onClick = onSync,
                    enabled = enabled,
                    modifier = Modifier.semantics { contentDescription = syncLabel },
                ) {
                    Text(
                        text = syncLabel,
                        color = if (enabled) tokens.primary else tokens.mutedForeground,
                        maxLines = 1,
                    )
                }
                TextButton(
                    onClick = onImport,
                    enabled = enabled,
                    modifier = Modifier.semantics { contentDescription = importLabel },
                ) {
                    Text(
                        text = importLabel,
                        color = if (enabled) tokens.primary else tokens.mutedForeground,
                        maxLines = 1,
                    )
                }
                Button(
                    onClick = onNew,
                    enabled = enabled,
                    modifier = Modifier.semantics { contentDescription = newLabel },
                ) {
                    Text(text = newLabel)
                }
            }
        }
    }
}

@Composable
private fun RewardList(
    rewards: List<RewardSummary>,
    redemptions: List<RedemptionSummary>,
    timers: List<RedemptionTimer>,
    edit: ManageDecision,
    lifecycle: ManageDecision,
    onEdit: (RewardSummary) -> Unit,
    onToggle: (RewardSummary, Boolean) -> Unit,
    onDelete: (RewardSummary) -> Unit,
    onRecreate: (RewardSummary) -> Unit,
    onFulfill: (RedemptionSummary) -> Unit,
    onRefund: (RedemptionSummary) -> Unit,
    onTimerAction: (timerId: String, action: TimerAction) -> Unit,
) {
    Card(modifier = Modifier.fillMaxWidth()) {
        LazyColumn(modifier = Modifier.fillMaxSize()) {
            itemsIndexed(items = rewards, key = { _, reward -> reward.id }) { index, reward ->
                if (index > 0) {
                    Separator()
                }
                RewardRow(
                    reward = reward,
                    edit = edit,
                    lifecycle = lifecycle,
                    onEdit = { onEdit(reward) },
                    onToggle = { enabled -> onToggle(reward, enabled) },
                    onDelete = { onDelete(reward) },
                    onRecreate = { onRecreate(reward) },
                )
            }

            // The live countdown timers (active first, then recent history) — a labelled section with per-row
            // pause/resume/complete/cancel gated at the page's Editor manage floor.
            if (timers.isNotEmpty()) {
                item(key = "redemption-timers-header") {
                    SectionHeader(stringResource(Res.string.rewards_timers_title))
                }
                itemsIndexed(items = timers, key = { _, t -> t.id }) { index, timer ->
                    if (index > 0) {
                        Separator()
                    }
                    RedemptionTimerRow(
                        timer = timer,
                        edit = edit,
                        onAction = { action -> onTimerAction(timer.id, action) },
                    )
                }
            }

            // The pending redemption queue. A labelled section beneath the rewards so the whole page scrolls as one.
            if (redemptions.isNotEmpty()) {
                item(key = "redemption-queue-header") { RedemptionsHeader() }
                itemsIndexed(items = redemptions, key = { _, r -> r.redemptionId }) { index, redemption ->
                    if (index > 0) {
                        Separator()
                    }
                    RedemptionRow(
                        redemption = redemption,
                        edit = edit,
                        onFulfill = { onFulfill(redemption) },
                        onRefund = { onRefund(redemption) },
                    )
                }
            }
        }
    }
}

@Composable
private fun RedemptionsHeader() {
    SectionHeader(stringResource(Res.string.rewards_queue_title))
}

@Composable
private fun SectionHeader(title: String) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Text(
        text = title,
        style = typography.lg,
        color = tokens.foreground,
        modifier = Modifier.padding(top = spacing.s3, bottom = spacing.s1, start = spacing.s4),
    )
}

// One live countdown row: the reward + who redeemed it, a ticking mm:ss remaining, a status chip, and the
// pause/resume/complete/cancel controls (gated at the page's Editor floor). The remaining seconds tick down
// locally each second from the server's clock-derived value; the screen's periodic refresh re-syncs it.
@Composable
private fun RedemptionTimerRow(
    timer: RedemptionTimer,
    edit: ManageDecision,
    onAction: (TimerAction) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val running: Boolean = timer.status == "running"
    val terminal: Boolean = timer.status == "completed" || timer.status == "canceled"

    // Tick down locally while running; reset whenever the server value / status changes (the refresh re-seeds it).
    var displayed: Int by remember(timer.id, timer.remainingSeconds, timer.status) {
        mutableStateOf(timer.remainingSeconds)
    }
    if (running) {
        LaunchedEffect(timer.id, timer.remainingSeconds, timer.status) {
            while (displayed > 0) {
                delay(1000)
                displayed -= 1
            }
        }
    }

    val statusLabel: String =
        stringResource(
            when (timer.status) {
                "paused" -> Res.string.rewards_timer_status_paused
                "completed" -> Res.string.rewards_timer_status_completed
                "canceled" -> Res.string.rewards_timer_status_canceled
                else -> Res.string.rewards_timer_status_running
            }
        )
    val remainingLabel: String =
        stringResource(Res.string.rewards_timer_remaining, formatDuration(displayed))

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Column(
            modifier = Modifier.weight(1f),
            verticalArrangement = Arrangement.spacedBy(spacing.s1),
        ) {
            Text(
                text = timer.rewardTitle,
                style = typography.base,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = "${timer.redeemedBy} · $statusLabel",
                style = typography.sm,
                color = tokens.mutedForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
        if (!terminal) {
            Text(
                text = remainingLabel,
                style = typography.base,
                color = tokens.primary,
                maxLines = 1,
            )
            if (running) {
                ManageGate(decision = edit) { enabled ->
                    Button(
                        onClick = { onAction(TimerAction.Pause) },
                        enabled = enabled,
                        variant = ButtonVariant.Outline,
                        size = ButtonSize.Sm,
                    ) { Text(text = stringResource(Res.string.rewards_timer_pause), maxLines = 1) }
                }
            } else {
                ManageGate(decision = edit) { enabled ->
                    Button(
                        onClick = { onAction(TimerAction.Resume) },
                        enabled = enabled,
                        variant = ButtonVariant.Outline,
                        size = ButtonSize.Sm,
                    ) { Text(text = stringResource(Res.string.rewards_timer_resume), maxLines = 1) }
                }
            }
            ManageGate(decision = edit) { enabled ->
                Button(
                    onClick = { onAction(TimerAction.Complete) },
                    enabled = enabled,
                    size = ButtonSize.Sm,
                ) { Text(text = stringResource(Res.string.rewards_timer_complete), maxLines = 1) }
            }
            ManageGate(decision = edit) { enabled ->
                GlyphButton(
                    imageVector = RemoveGlyph,
                    label = stringResource(Res.string.rewards_timer_cancel),
                    onClick = { onAction(TimerAction.Cancel) },
                    enabled = enabled,
                    tint = tokens.destructive,
                )
            }
        }
    }
}

// mm:ss for a non-negative seconds count (a countdown never shows a negative value).
private fun formatDuration(totalSeconds: Int): String {
    val safe: Int = totalSeconds.coerceAtLeast(0)
    val minutes: Int = safe / 60
    val seconds: Int = safe % 60
    val paddedSeconds: String = if (seconds < 10) "0$seconds" else "$seconds"
    return "$minutes:$paddedSeconds"
}

@Composable
private fun RedemptionRow(
    redemption: RedemptionSummary,
    edit: ManageDecision,
    onFulfill: () -> Unit,
    onRefund: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val costLabel: String = stringResource(Res.string.rewards_cost, redemption.cost)
    val byLabel: String = stringResource(Res.string.rewards_queue_by, redemption.userDisplayName)
    val rowDescription: String =
        stringResource(
            Res.string.rewards_queue_row,
            redemption.rewardTitle,
            redemption.userDisplayName,
            costLabel,
        )
    val fulfillLabel: String =
        stringResource(Res.string.rewards_queue_fulfill_action, redemption.rewardTitle)
    val refundLabel: String =
        stringResource(Res.string.rewards_queue_refund_action, redemption.rewardTitle)

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Column(
            modifier = Modifier
                .weight(1f)
                // One node for the redemption text: "Hydrate!, redeemed by Buyer, 50 points" — the action
                // buttons keep their own semantics so they stay individually reachable.
                .clearAndSetSemantics { contentDescription = rowDescription },
            verticalArrangement = Arrangement.spacedBy(spacing.s1),
        ) {
            Text(
                text = redemption.rewardTitle,
                style = typography.base,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = byLabel,
                style = typography.sm,
                color = tokens.mutedForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            redemption.userInput?.takeIf { it.isNotBlank() }?.let { input ->
                Text(
                    text = input,
                    style = typography.sm,
                    color = tokens.mutedForeground,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
        }
        Text(text = costLabel, style = typography.sm, color = tokens.mutedForeground, maxLines = 1)

        // Fulfil / refund gate at the page's Editor manage floor; the backend re-checks reward:manage.
        ManageGate(decision = edit) { enabled ->
            GlyphButton(
                imageVector = CheckCircleGlyph,
                label = fulfillLabel,
                onClick = onFulfill,
                enabled = enabled,
                tint = tokens.primary,
            )
        }
        ManageGate(decision = edit) { enabled ->
            GlyphButton(
                imageVector = RemoveGlyph,
                label = refundLabel,
                onClick = onRefund,
                enabled = enabled,
                tint = tokens.destructive,
            )
        }
    }
}

@Composable
private fun RewardRow(
    reward: RewardSummary,
    edit: ManageDecision,
    lifecycle: ManageDecision,
    onEdit: () -> Unit,
    onToggle: (Boolean) -> Unit,
    onDelete: () -> Unit,
    onRecreate: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val costLabel: String = stringResource(Res.string.rewards_cost, reward.cost)
    val stateLabel: String =
        stringResource(if (reward.isEnabled) Res.string.rewards_enabled else Res.string.rewards_disabled)
    val rowDescription: String =
        stringResource(Res.string.rewards_row_description, reward.title, costLabel, stateLabel)
    val toggleLabel: String = stringResource(Res.string.rewards_toggle_action, reward.title)
    val editLabel: String = stringResource(Res.string.rewards_edit_action, reward.title)
    val deleteLabel: String = stringResource(Res.string.rewards_delete_action, reward.title)
    val recreateLabel: String = stringResource(Res.string.rewards_recreate_action)

    // Twitch reality: the bot can only edit/toggle/delete rewards ITS OWN client created. An EXTERNAL reward
    // (isManageable == false — made in the Twitch UI or by another app) is read-only to us until recreated under
    // the bot. So for an external reward the write controls are DISABLED (not hidden) with an actionable reason,
    // and a "Take control" action appears instead. A manageable reward keeps its full role-gated CRUD.
    val externalReason: String = stringResource(Res.string.rewards_external_readonly_reason)
    val rowEdit: ManageDecision = if (reward.isManageable) edit else ManageDecision.Denied(externalReason)
    val rowLifecycle: ManageDecision =
        if (reward.isManageable) lifecycle else ManageDecision.Denied(externalReason)

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Column(
            modifier = Modifier
                .weight(1f)
                // One node for the text block: "Hydrate!, 500 points, Enabled".
                .clearAndSetSemantics { contentDescription = rowDescription },
            verticalArrangement = Arrangement.spacedBy(spacing.s1),
        ) {
            Text(
                text = reward.title,
                style = typography.lg,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = costLabel,
                style = typography.sm,
                color = tokens.mutedForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }

        // Take control: only on EXTERNAL rewards. Recreating an equivalent under the bot's client is a
        // Broadcaster-only lifecycle action, so it gates on the real [lifecycle] floor (a Broadcaster CAN do it).
        if (!reward.isManageable) {
            ManageGate(decision = lifecycle) { enabled ->
                Button(
                    onClick = onRecreate,
                    enabled = enabled,
                    variant = ButtonVariant.Outline,
                    size = ButtonSize.Sm,
                ) {
                    Text(text = recreateLabel, maxLines = 1)
                }
            }
        }

        // Toggle + edit gate at the page's Editor floor; delete is the Broadcaster-only lifecycle action. On an
        // external reward all three collapse to Denied(externalReason) so they render disabled with the reason.
        ManageGate(decision = rowEdit) { enabled ->
            Switch(
                checked = reward.isEnabled,
                onCheckedChange = onToggle,
                enabled = enabled,
                modifier = Modifier.semantics { contentDescription = toggleLabel },
            )
        }
        ManageGate(decision = rowEdit) { enabled ->
            GlyphButton(imageVector = EditGlyph, label = editLabel, onClick = onEdit, enabled = enabled)
        }
        ManageGate(decision = rowLifecycle) { enabled ->
            GlyphButton(
                imageVector = TrashGlyph,
                label = deleteLabel,
                onClick = onDelete,
                enabled = enabled,
                tint = tokens.destructive,
            )
        }
    }
}

// The collected reward-form values handed back on submit. Blank optional number/colour fields resolve to null
// (= "no limit" / Twitch default); the timer resolves to 0 when blank (= clear the countdown).
private data class RewardFormResult(
    val title: String,
    val cost: Int,
    val prompt: String,
    val isEnabled: Boolean,
    val isPaused: Boolean,
    val isUserInputRequired: Boolean,
    val backgroundColor: String?,
    val maxPerStream: Int?,
    val maxPerUserPerStream: Int?,
    val globalCooldownSeconds: Int?,
    val timerDurationSeconds: Int?,
    val pipelineId: String?,
)

// One composable for both create and edit (DRY): an empty [editor] = create, a pre-filled one = edit. The
// affirmative button is disabled until the title is non-blank and the cost parses to a positive whole number,
// so a malformed reward can never be submitted. The cost and limit fields are digits-only.
@Composable
private fun RewardFormDialog(
    editor: RewardEditor,
    pipelines: List<PipelineSummary>,
    onDismiss: () -> Unit,
    onSubmit: (RewardFormResult) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    var title: String by remember { mutableStateOf(editor.title) }
    var cost: String by remember { mutableStateOf(editor.cost) }
    var prompt: String by remember { mutableStateOf(editor.prompt) }
    var enabled: Boolean by remember { mutableStateOf(editor.isEnabled) }
    var paused: Boolean by remember { mutableStateOf(editor.isPaused) }
    var requireInput: Boolean by remember { mutableStateOf(editor.isUserInputRequired) }
    var backgroundColor: String by remember { mutableStateOf(editor.backgroundColor) }
    var maxPerStream: String by remember { mutableStateOf(editor.maxPerStream) }
    var maxPerUser: String by remember { mutableStateOf(editor.maxPerUserPerStream) }
    var globalCooldown: String by remember { mutableStateOf(editor.globalCooldownSeconds) }
    var timerSeconds: String by remember { mutableStateOf(editor.timerDurationSeconds) }
    var selectedPipelineId: String? by remember { mutableStateOf(editor.pipelineId) }
    var pipelineMenuOpen: Boolean by remember { mutableStateOf(false) }

    val parsedCost: Int? = cost.toIntOrNull()
    // A blank timer field means "no timer" (send 0 to clear); a non-blank one must parse to a non-negative int.
    val parsedTimer: Int? = timerSeconds.ifBlank { "0" }.toIntOrNull()
    val timerValid: Boolean = parsedTimer != null && parsedTimer >= 0
    // Blank limit fields mean "no limit" (send null); a non-blank one must parse to a positive int.
    val parsedMaxPerStream: Int? = maxPerStream.toIntOrNull()
    val parsedMaxPerUser: Int? = maxPerUser.toIntOrNull()
    val parsedCooldown: Int? = globalCooldown.toIntOrNull()
    // A background colour is optional; when present it must be a valid hex ("#RGB"/"#RRGGBB"/"#AARRGGBB").
    val colorValid: Boolean = backgroundColor.isBlank() || parseHexColor(backgroundColor) != null
    val canSubmit: Boolean =
        title.isNotBlank() && parsedCost != null && parsedCost > 0 && timerValid && colorValid
    val pipelineNoneLabel: String = stringResource(Res.string.rewards_dialog_pipeline_none)
    val selectedPipelineName: String =
        selectedPipelineId?.let { id -> pipelines.firstOrNull { it.id == id }?.name } ?: pipelineNoneLabel
    val dialogTitle: String =
        stringResource(
            if (editor.isEdit) Res.string.rewards_dialog_edit_title
            else Res.string.rewards_dialog_create_title
        )
    val submitLabel: String =
        stringResource(
            if (editor.isEdit) Res.string.rewards_dialog_save else Res.string.rewards_dialog_create
        )
    val enabledLabel: String = stringResource(Res.string.rewards_dialog_enabled_label)
    val pausedLabel: String = stringResource(Res.string.rewards_dialog_paused_label)
    val requireInputLabel: String = stringResource(Res.string.rewards_dialog_require_input_label)

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(text = dialogTitle) },
        text = {
            Column(
                verticalArrangement = Arrangement.spacedBy(spacing.s3),
                modifier = Modifier.verticalScroll(rememberScrollState()),
            ) {
                AppTextField(
                    value = title,
                    onValueChange = { title = it },
                    modifier = Modifier.fillMaxWidth(),
                    label = stringResource(Res.string.rewards_dialog_title_label),
                )
                AppTextField(
                    value = cost,
                    // Digits only — drop anything else so the cost field can never hold a non-number.
                    onValueChange = { input -> cost = input.filter { it.isDigit() } },
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    modifier = Modifier.fillMaxWidth(),
                    label = stringResource(Res.string.rewards_dialog_cost_label),
                )
                AppTextField(
                    value = prompt,
                    onValueChange = { prompt = it },
                    modifier = Modifier.fillMaxWidth(),
                    label = stringResource(Res.string.rewards_dialog_prompt_label),
                )
                // The reward card's background colour (hex). Blank = Twitch's default. Uses the design-system
                // colour control (hex field + live swatch).
                ColorField(
                    value = backgroundColor,
                    onValueChange = { backgroundColor = it },
                    isError = !colorValid,
                    modifier = Modifier.fillMaxWidth(),
                    label = stringResource(Res.string.rewards_dialog_background_color_label),
                )
                // Twitch redemption limits — blank = no limit. Digits only.
                AppTextField(
                    value = maxPerStream,
                    onValueChange = { input -> maxPerStream = input.filter { it.isDigit() } },
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    modifier = Modifier.fillMaxWidth(),
                    label = stringResource(Res.string.rewards_dialog_max_per_stream_label),
                )
                AppTextField(
                    value = maxPerUser,
                    onValueChange = { input -> maxPerUser = input.filter { it.isDigit() } },
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    modifier = Modifier.fillMaxWidth(),
                    label = stringResource(Res.string.rewards_dialog_max_per_user_label),
                )
                AppTextField(
                    value = globalCooldown,
                    onValueChange = { input -> globalCooldown = input.filter { it.isDigit() } },
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    modifier = Modifier.fillMaxWidth(),
                    label = stringResource(Res.string.rewards_dialog_cooldown_label),
                )
                // Optional countdown a redemption auto-starts (seconds; blank/0 = none). Digits only.
                AppTextField(
                    value = timerSeconds,
                    onValueChange = { input -> timerSeconds = input.filter { it.isDigit() } },
                    isError = !timerValid,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    modifier = Modifier.fillMaxWidth(),
                    label = stringResource(Res.string.rewards_dialog_timer_label),
                )
                // Optional pipeline to run on redemption. Only shown when the channel has pipelines to bind.
                if (pipelines.isNotEmpty()) {
                    Box {
                        AppTextField(
                            value = selectedPipelineName,
                            onValueChange = {},
                            modifier = Modifier.fillMaxWidth().clickable { pipelineMenuOpen = true },
                            label = stringResource(Res.string.rewards_dialog_pipeline_label),
                        )
                        DropdownMenu(
                            expanded = pipelineMenuOpen,
                            onDismissRequest = { pipelineMenuOpen = false },
                        ) {
                            DropdownMenuItem(
                                text = { Text(pipelineNoneLabel, color = tokens.mutedForeground) },
                                onClick = {
                                    selectedPipelineId = null
                                    pipelineMenuOpen = false
                                },
                            )
                            pipelines.forEach { pipeline ->
                                DropdownMenuItem(
                                    text = { Text(pipeline.name, color = tokens.cardForeground) },
                                    onClick = {
                                        selectedPipelineId = pipeline.id
                                        pipelineMenuOpen = false
                                    },
                                )
                            }
                        }
                    }
                }
                ToggleRow(
                    label = requireInputLabel,
                    checked = requireInput,
                    onCheckedChange = { requireInput = it },
                )
                ToggleRow(label = enabledLabel, checked = enabled, onCheckedChange = { enabled = it })
                // Pause is an edit-only concept (a freshly created reward is never pre-paused).
                if (editor.isEdit) {
                    ToggleRow(label = pausedLabel, checked = paused, onCheckedChange = { paused = it })
                }
            }
        },
        confirmButton = {
            TextButton(
                onClick = {
                    parsedCost?.let { validCost ->
                        onSubmit(
                            RewardFormResult(
                                title = title,
                                cost = validCost,
                                prompt = prompt,
                                isEnabled = enabled,
                                isPaused = paused,
                                isUserInputRequired = requireInput,
                                backgroundColor = backgroundColor.ifBlank { null },
                                maxPerStream = parsedMaxPerStream,
                                maxPerUserPerStream = parsedMaxPerUser,
                                globalCooldownSeconds = parsedCooldown,
                                timerDurationSeconds = parsedTimer ?: 0,
                                pipelineId = selectedPipelineId,
                            )
                        )
                    }
                },
                enabled = canSubmit,
            ) {
                Text(
                    text = submitLabel,
                    color = if (canSubmit) tokens.primary else tokens.mutedForeground,
                )
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(
                    text = stringResource(Res.string.rewards_dialog_cancel),
                    color = tokens.mutedForeground,
                )
            }
        },
    )
}

// A labelled switch row inside the reward form — the label on the leading edge, the switch trailing.
@Composable
private fun ToggleRow(label: String, checked: Boolean, onCheckedChange: (Boolean) -> Unit) {
    val tokens = LocalTokens.current
    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.SpaceBetween,
    ) {
        Text(text = label, color = tokens.cardForeground)
        Switch(
            checked = checked,
            onCheckedChange = onCheckedChange,
            modifier = Modifier.semantics { contentDescription = label },
        )
    }
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
                text = stringResource(Res.string.rewards_error, detail),
                style = typography.base,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.rewards_retry)) }
        }
    }
}

@Composable
private fun CenteredMessage(text: String) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    Box(modifier = Modifier.fillMaxWidth(), contentAlignment = Alignment.Center) {
        Text(text = text, style = typography.base, color = tokens.mutedForeground)
    }
}

// The create/edit dialog's seed: an empty editor opens a blank create form; one seeded from a reward opens a
// pre-filled edit form. [isEdit] decides create-vs-update on submit and [id] addresses the row the update /
// toggle / delete targets. The list-row projection carries no prompt (it is a detail-only field), so the edit
// form opens with an empty prompt the operator can fill to set it on save.
private data class RewardEditor(
    val isEdit: Boolean,
    val id: String,
    val title: String,
    val cost: String,
    val prompt: String,
    val isEnabled: Boolean,
    // Paused = live but temporarily not redeemable; require-input = viewer must type text on redeem.
    val isPaused: Boolean,
    val isUserInputRequired: Boolean,
    // The card background colour as an editable hex string ("" = Twitch default).
    val backgroundColor: String,
    // Twitch redemption limits as editable strings ("" = no limit): max per stream, max per user per stream,
    // and a global cooldown in seconds.
    val maxPerStream: String,
    val maxPerUserPerStream: String,
    val globalCooldownSeconds: String,
    // The countdown length in seconds as an editable string ("" = no timer) and the bound pipeline id (null = none).
    val timerDurationSeconds: String,
    val pipelineId: String?,
) {
    companion object {
        fun create(): RewardEditor =
            RewardEditor(
                isEdit = false,
                id = "",
                title = "",
                cost = "",
                prompt = "",
                isEnabled = true,
                isPaused = false,
                isUserInputRequired = false,
                backgroundColor = "",
                maxPerStream = "",
                maxPerUserPerStream = "",
                globalCooldownSeconds = "",
                timerDurationSeconds = "",
                pipelineId = null,
            )

        fun edit(reward: RewardSummary): RewardEditor =
            RewardEditor(
                isEdit = true,
                id = reward.id,
                title = reward.title,
                cost = reward.cost.toString(),
                prompt = reward.prompt.orEmpty(),
                isEnabled = reward.isEnabled,
                isPaused = reward.isPaused,
                isUserInputRequired = reward.isUserInputRequired,
                backgroundColor = reward.backgroundColor.orEmpty(),
                // A 0/null stored limit shows as blank ("no limit"); a positive one pre-fills the field.
                maxPerStream = reward.maxPerStream?.takeIf { it > 0 }?.toString().orEmpty(),
                maxPerUserPerStream = reward.maxPerUserPerStream?.takeIf { it > 0 }?.toString().orEmpty(),
                globalCooldownSeconds = reward.globalCooldownSeconds?.takeIf { it > 0 }?.toString().orEmpty(),
                // A 0/null stored duration shows as blank ("no timer"); a positive one pre-fills the field.
                timerDurationSeconds = reward.timerDurationSeconds?.takeIf { it > 0 }?.toString().orEmpty(),
                pipelineId = reward.pipelineId,
            )
    }
}
