// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.mediashare.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.ActionErrorBanner
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Badge
import bot.nomnomz.dashboard.core.designsystem.component.BadgeVariant
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.ButtonSize
import bot.nomnomz.dashboard.core.designsystem.component.ButtonVariant
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.GlyphButton
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Switch
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.icon.ArrowDownGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.ArrowUpGlyph
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.MediaShareConfig
import bot.nomnomz.dashboard.core.network.MediaShareRequest
import bot.nomnomz.dashboard.feature.mediashare.state.MediaShareController
import bot.nomnomz.dashboard.feature.mediashare.state.MediaShareUiState
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecisionAtFloor
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.mediashare_action_error
import nomnomzbot.composeapp.generated.resources.mediashare_allow_twitch_clips
import nomnomzbot.composeapp.generated.resources.mediashare_allow_youtube
import nomnomzbot.composeapp.generated.resources.mediashare_approve
import nomnomzbot.composeapp.generated.resources.mediashare_cancel
import nomnomzbot.composeapp.generated.resources.mediashare_config_title
import nomnomzbot.composeapp.generated.resources.mediashare_cooldown
import nomnomzbot.composeapp.generated.resources.mediashare_empty
import nomnomzbot.composeapp.generated.resources.mediashare_enabled
import nomnomzbot.composeapp.generated.resources.mediashare_entry_cost
import nomnomzbot.composeapp.generated.resources.mediashare_error
import nomnomzbot.composeapp.generated.resources.mediashare_filter_all
import nomnomzbot.composeapp.generated.resources.mediashare_filter_approved
import nomnomzbot.composeapp.generated.resources.mediashare_filter_pending
import nomnomzbot.composeapp.generated.resources.mediashare_filter_played
import nomnomzbot.composeapp.generated.resources.mediashare_loading
import nomnomzbot.composeapp.generated.resources.mediashare_mark_played
import nomnomzbot.composeapp.generated.resources.mediashare_max_duration
import nomnomzbot.composeapp.generated.resources.mediashare_max_queue
import nomnomzbot.composeapp.generated.resources.mediashare_move_down
import nomnomzbot.composeapp.generated.resources.mediashare_move_up
import nomnomzbot.composeapp.generated.resources.mediashare_queue_title
import nomnomzbot.composeapp.generated.resources.mediashare_reject
import nomnomzbot.composeapp.generated.resources.mediashare_reject_confirm
import nomnomzbot.composeapp.generated.resources.mediashare_reject_message
import nomnomzbot.composeapp.generated.resources.mediashare_reject_title
import nomnomzbot.composeapp.generated.resources.mediashare_require_approval
import nomnomzbot.composeapp.generated.resources.mediashare_requested_by
import nomnomzbot.composeapp.generated.resources.mediashare_retry
import nomnomzbot.composeapp.generated.resources.mediashare_save
import nomnomzbot.composeapp.generated.resources.mediashare_skip
import nomnomzbot.composeapp.generated.resources.mediashare_skip_confirm
import nomnomzbot.composeapp.generated.resources.mediashare_skip_message
import nomnomzbot.composeapp.generated.resources.mediashare_skip_title
import nomnomzbot.composeapp.generated.resources.mediashare_source_twitch_clip
import nomnomzbot.composeapp.generated.resources.mediashare_source_youtube
import nomnomzbot.composeapp.generated.resources.mediashare_status_approved
import nomnomzbot.composeapp.generated.resources.mediashare_status_played
import nomnomzbot.composeapp.generated.resources.mediashare_status_playing
import nomnomzbot.composeapp.generated.resources.mediashare_status_pending
import nomnomzbot.composeapp.generated.resources.mediashare_status_rejected
import nomnomzbot.composeapp.generated.resources.mediashare_status_skipped
import nomnomzbot.composeapp.generated.resources.mediashare_submit_hint
import nomnomzbot.composeapp.generated.resources.mediashare_subtitle
import nomnomzbot.composeapp.generated.resources.mediashare_title
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource

// The Media-Share moderator-queue page (frontend-ia.md, Live-ops group; media-share.md §5): a mod approves,
// reorders, skips and marks-played the viewer-submitted clip queue, and (at the Editor floor) edits the channel's
// Media-Share config. A pure projection of [MediaShareController]. Queue writes gate at the page's Moderator
// manage floor; the config write gates one rung higher, at Editor. Viewers submit clips via the `!media <url>`
// chat command or a channel-point redeem — never from this page (the config card notes this).
@Composable
fun MediaShareScreen(controller: MediaShareController, role: ManagementRole?) {
    val state: MediaShareUiState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    // Queue moderate actions gate at the page's Moderator floor; the config write gates one rung up at Editor.
    val moderate: ManageDecision = rememberManageDecision(role, ShellRoute.MediaShare)
    val configManage: ManageDecision = rememberManageDecisionAtFloor(role, ManagementRole.Editor)

    var pendingReject: MediaShareRequest? by remember { mutableStateOf(null) }
    var pendingSkip: MediaShareRequest? by remember { mutableStateOf(null) }

    LaunchedEffect(Unit) { controller.load() }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: MediaShareUiState = state) {
            is MediaShareUiState.Loading -> CenteredMessage(stringResource(Res.string.mediashare_loading))
            is MediaShareUiState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is MediaShareUiState.Ready ->
                Column(
                    modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()),
                    verticalArrangement = Arrangement.spacedBy(spacing.s4),
                ) {
                    PageHeader(
                        title = stringResource(Res.string.mediashare_title),
                        subtitle = stringResource(Res.string.mediashare_subtitle),
                    )

                    current.actionError?.let {
                        ActionErrorBanner(message = stringResource(Res.string.mediashare_action_error, it))
                    }

                    StatusFilterRow(
                        selected = current.statusFilter,
                        onSelect = { status -> scope.launch { controller.setStatusFilter(status) } },
                    )

                    QueueCard(
                        queue = current.queue,
                        moderate = moderate,
                        onApprove = { id -> scope.launch { controller.approve(id) } },
                        onMarkPlayed = { id -> scope.launch { controller.markPlayed(id) } },
                        onReject = { request -> pendingReject = request },
                        onSkip = { request -> pendingSkip = request },
                        onReorder = { id, position -> scope.launch { controller.reorder(id, position) } },
                    )

                    ConfigCard(
                        config = current.config,
                        manage = configManage,
                        onSave = { edited -> scope.launch { controller.saveConfig(edited) } },
                    )
                }
        }
    }

    pendingReject?.let { request ->
        ConfirmDialog(
            title = stringResource(Res.string.mediashare_reject_title),
            message = stringResource(Res.string.mediashare_reject_message, request.title ?: request.mediaRef),
            confirmLabel = stringResource(Res.string.mediashare_reject_confirm),
            dismissLabel = stringResource(Res.string.mediashare_cancel),
            destructive = true,
            onConfirm = {
                pendingReject = null
                scope.launch { controller.reject(request.id) }
            },
            onDismiss = { pendingReject = null },
        )
    }

    pendingSkip?.let { request ->
        ConfirmDialog(
            title = stringResource(Res.string.mediashare_skip_title),
            message = stringResource(Res.string.mediashare_skip_message, request.title ?: request.mediaRef),
            confirmLabel = stringResource(Res.string.mediashare_skip_confirm),
            dismissLabel = stringResource(Res.string.mediashare_cancel),
            destructive = true,
            onConfirm = {
                pendingSkip = null
                scope.launch { controller.skip(request.id) }
            },
            onDismiss = { pendingSkip = null },
        )
    }
}

// ── queue ─────────────────────────────────────────────────────────────────────

@Composable
private fun StatusFilterRow(selected: String?, onSelect: (String?) -> Unit) {
    val spacing = LocalSpacing.current
    Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
        FilterChip(label = stringResource(Res.string.mediashare_filter_all), active = selected == null, onClick = { onSelect(null) })
        FilterChip(label = stringResource(Res.string.mediashare_filter_pending), active = selected == "pending", onClick = { onSelect("pending") })
        FilterChip(label = stringResource(Res.string.mediashare_filter_approved), active = selected == "approved", onClick = { onSelect("approved") })
        FilterChip(label = stringResource(Res.string.mediashare_filter_played), active = selected == "played", onClick = { onSelect("played") })
    }
}

@Composable
private fun FilterChip(label: String, active: Boolean, onClick: () -> Unit) {
    Badge(
        variant = if (active) BadgeVariant.Default else BadgeVariant.Outline,
        selected = active,
        onClick = onClick,
    ) {
        Text(text = label, maxLines = 1)
    }
}

@Composable
private fun QueueCard(
    queue: List<MediaShareRequest>,
    moderate: ManageDecision,
    onApprove: (String) -> Unit,
    onMarkPlayed: (String) -> Unit,
    onReject: (MediaShareRequest) -> Unit,
    onSkip: (MediaShareRequest) -> Unit,
    onReorder: (id: String, position: Int) -> Unit,
) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current

    if (queue.isEmpty()) {
        CenteredMessage(stringResource(Res.string.mediashare_empty))
        return
    }

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
        Text(text = stringResource(Res.string.mediashare_queue_title), style = typography.sm, color = tokens.mutedForeground)
        Card(modifier = Modifier.fillMaxWidth()) {
            Column {
                queue.forEachIndexed { index, request ->
                    QueueRow(
                        request = request,
                        moderate = moderate,
                        isFirst = index == 0,
                        isLast = index == queue.lastIndex,
                        onApprove = onApprove,
                        onMarkPlayed = onMarkPlayed,
                        onReject = onReject,
                        onSkip = onSkip,
                        onReorder = onReorder,
                    )
                    if (index < queue.lastIndex) Separator()
                }
            }
        }
    }
}

@Composable
private fun QueueRow(
    request: MediaShareRequest,
    moderate: ManageDecision,
    isFirst: Boolean,
    isLast: Boolean,
    onApprove: (String) -> Unit,
    onMarkPlayed: (String) -> Unit,
    onReject: (MediaShareRequest) -> Unit,
    onSkip: (MediaShareRequest) -> Unit,
    onReorder: (id: String, position: Int) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val isPending: Boolean = request.status == "pending"
    val isApproved: Boolean = request.status == "approved"
    val actionable: Boolean = isPending || isApproved
    val position: Int? = request.queuePosition

    Row(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                Text(
                    text = request.title ?: request.mediaRef,
                    style = typography.base,
                    color = tokens.cardForeground,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                    modifier = Modifier.weight(1f, fill = false),
                )
                Badge(variant = sourceBadgeVariant(request.sourceType)) {
                    Text(text = stringResource(sourceLabel(request.sourceType)), style = typography.xs)
                }
                Badge(variant = statusBadgeVariant(request.status)) {
                    Text(text = stringResource(statusLabel(request.status)), style = typography.xs)
                }
            }
            Text(
                text = "${formatMmSs(request.durationSeconds)} · ${stringResource(Res.string.mediashare_requested_by, request.requesterUserId)}",
                style = typography.xs,
                color = tokens.mutedForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }

        if (actionable) {
            // Reorder within the lane: up decrements, down increments the 0-based queue position.
            ManageGate(decision = moderate) { enabled ->
                GlyphButton(
                    imageVector = ArrowUpGlyph,
                    label = stringResource(Res.string.mediashare_move_up),
                    onClick = { position?.let { onReorder(request.id, it - 1) } },
                    enabled = enabled && position != null && !isFirst,
                )
            }
            ManageGate(decision = moderate) { enabled ->
                GlyphButton(
                    imageVector = ArrowDownGlyph,
                    label = stringResource(Res.string.mediashare_move_down),
                    onClick = { position?.let { onReorder(request.id, it + 1) } },
                    enabled = enabled && position != null && !isLast,
                )
            }
            if (isPending) {
                ManageGate(decision = moderate) { enabled ->
                    Button(onClick = { onApprove(request.id) }, size = ButtonSize.Sm, enabled = enabled) {
                        Text(text = stringResource(Res.string.mediashare_approve))
                    }
                }
            }
            if (isApproved) {
                ManageGate(decision = moderate) { enabled ->
                    Button(onClick = { onMarkPlayed(request.id) }, size = ButtonSize.Sm, enabled = enabled) {
                        Text(text = stringResource(Res.string.mediashare_mark_played))
                    }
                }
            }
            ManageGate(decision = moderate) { enabled ->
                Button(
                    onClick = { onSkip(request) },
                    variant = ButtonVariant.Outline,
                    size = ButtonSize.Sm,
                    enabled = enabled,
                ) {
                    Text(text = stringResource(Res.string.mediashare_skip))
                }
            }
            ManageGate(decision = moderate) { enabled ->
                Button(
                    onClick = { onReject(request) },
                    variant = ButtonVariant.Destructive,
                    size = ButtonSize.Sm,
                    enabled = enabled,
                ) {
                    Text(text = stringResource(Res.string.mediashare_reject))
                }
            }
        }
    }
}

// ── config ──────────────────────────────────────────────────────────────────

@Composable
private fun ConfigCard(config: MediaShareConfig, manage: ManageDecision, onSave: (MediaShareConfig) -> Unit) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current

    // The edited config is held locally, seeded from the Ready config and re-synced whenever it changes (a save
    // or reload). Number fields are edited as strings so a transiently-empty field is a valid intermediate state.
    var isEnabled: Boolean by remember { mutableStateOf(config.isEnabled) }
    var requireApproval: Boolean by remember { mutableStateOf(config.requireApproval) }
    var allowTwitchClips: Boolean by remember { mutableStateOf(config.allowTwitchClips) }
    var allowYouTube: Boolean by remember { mutableStateOf(config.allowYouTube) }
    var maxDuration: String by remember { mutableStateOf(config.maxDurationSeconds.toString()) }
    var entryCost: String by remember { mutableStateOf(config.entryCost?.toString() ?: "") }
    var maxQueue: String by remember { mutableStateOf(config.maxQueueLength.toString()) }
    var cooldown: String by remember { mutableStateOf(config.perUserCooldownSeconds.toString()) }

    LaunchedEffect(config) {
        isEnabled = config.isEnabled
        requireApproval = config.requireApproval
        allowTwitchClips = config.allowTwitchClips
        allowYouTube = config.allowYouTube
        maxDuration = config.maxDurationSeconds.toString()
        entryCost = config.entryCost?.toString() ?: ""
        maxQueue = config.maxQueueLength.toString()
        cooldown = config.perUserCooldownSeconds.toString()
    }

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.fillMaxWidth().padding(spacing.s4),
            verticalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            Text(text = stringResource(Res.string.mediashare_config_title), style = typography.lg, color = tokens.cardForeground)

            SwitchRow(label = stringResource(Res.string.mediashare_enabled), checked = isEnabled, manage = manage, onCheckedChange = { isEnabled = it })
            SwitchRow(label = stringResource(Res.string.mediashare_require_approval), checked = requireApproval, manage = manage, onCheckedChange = { requireApproval = it })
            SwitchRow(label = stringResource(Res.string.mediashare_allow_twitch_clips), checked = allowTwitchClips, manage = manage, onCheckedChange = { allowTwitchClips = it })
            SwitchRow(label = stringResource(Res.string.mediashare_allow_youtube), checked = allowYouTube, manage = manage, onCheckedChange = { allowYouTube = it })

            NumberField(label = stringResource(Res.string.mediashare_max_duration), value = maxDuration, manage = manage, onValueChange = { maxDuration = it })
            NumberField(label = stringResource(Res.string.mediashare_entry_cost), value = entryCost, manage = manage, onValueChange = { entryCost = it })
            NumberField(label = stringResource(Res.string.mediashare_max_queue), value = maxQueue, manage = manage, onValueChange = { maxQueue = it })
            NumberField(label = stringResource(Res.string.mediashare_cooldown), value = cooldown, manage = manage, onValueChange = { cooldown = it })

            Text(text = stringResource(Res.string.mediashare_submit_hint), style = typography.xs, color = tokens.mutedForeground)

            ManageGate(decision = manage) { enabled ->
                Button(
                    onClick = {
                        onSave(
                            config.copy(
                                isEnabled = isEnabled,
                                requireApproval = requireApproval,
                                allowTwitchClips = allowTwitchClips,
                                allowYouTube = allowYouTube,
                                maxDurationSeconds = maxDuration.toIntOrNull() ?: 0,
                                entryCost = entryCost.toLongOrNull(),
                                maxQueueLength = maxQueue.toIntOrNull() ?: 0,
                                perUserCooldownSeconds = cooldown.toIntOrNull() ?: 0,
                            )
                        )
                    },
                    enabled = enabled,
                ) {
                    Text(text = stringResource(Res.string.mediashare_save))
                }
            }
        }
    }
}

@Composable
private fun SwitchRow(label: String, checked: Boolean, manage: ManageDecision, onCheckedChange: (Boolean) -> Unit) {
    val tokens = LocalTokens.current
    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.SpaceBetween,
    ) {
        Text(text = label, color = tokens.cardForeground)
        ManageGate(decision = manage) { enabled ->
            Switch(checked = checked, onCheckedChange = onCheckedChange, enabled = enabled)
        }
    }
}

@Composable
private fun NumberField(label: String, value: String, manage: ManageDecision, onValueChange: (String) -> Unit) {
    ManageGate(decision = manage) { enabled ->
        AppTextField(
            value = value,
            onValueChange = onValueChange,
            label = label,
            enabled = enabled,
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
            modifier = Modifier.fillMaxWidth(),
        )
    }
}

// ── labels ────────────────────────────────────────────────────────────────────

private fun sourceLabel(sourceType: String): StringResource =
    when (sourceType) {
        "youtube" -> Res.string.mediashare_source_youtube
        else -> Res.string.mediashare_source_twitch_clip
    }

private fun sourceBadgeVariant(sourceType: String): BadgeVariant =
    when (sourceType) {
        "youtube" -> BadgeVariant.Secondary
        else -> BadgeVariant.Default
    }

private fun statusLabel(status: String): StringResource =
    when (status) {
        "approved" -> Res.string.mediashare_status_approved
        "playing" -> Res.string.mediashare_status_playing
        "played" -> Res.string.mediashare_status_played
        "rejected" -> Res.string.mediashare_status_rejected
        "skipped" -> Res.string.mediashare_status_skipped
        else -> Res.string.mediashare_status_pending
    }

private fun statusBadgeVariant(status: String): BadgeVariant =
    when (status) {
        "approved", "playing" -> BadgeVariant.Default
        "rejected" -> BadgeVariant.Destructive
        "played", "skipped" -> BadgeVariant.Secondary
        else -> BadgeVariant.Outline
    }

private fun formatMmSs(totalSeconds: Int): String {
    val minutes: Int = totalSeconds / 60
    val seconds: Int = totalSeconds % 60
    val ss: String = if (seconds < 10) "0$seconds" else "$seconds"
    return "$minutes:$ss"
}

// ── shared bits ───────────────────────────────────────────────────────────────

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
                text = stringResource(Res.string.mediashare_error, detail),
                style = typography.base,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.mediashare_retry)) }
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
