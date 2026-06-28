// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.songrequests.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.selection.SelectionContainer
import androidx.compose.material3.Switch
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.material3.SwitchDefaults
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
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.platform.LocalClipboardManager
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.MusicConfig
import bot.nomnomz.dashboard.core.network.QueuedSong
import bot.nomnomz.dashboard.core.network.UpdateMusicConfigBody
import bot.nomnomz.dashboard.feature.songrequests.state.SongRequestsController
import bot.nomnomz.dashboard.feature.songrequests.state.SongRequestsState
import bot.nomnomz.dashboard.feature.shell.nav.ManageAction
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.shell_nav_song_requests
import nomnomzbot.composeapp.generated.resources.songrequests_action_error
import nomnomzbot.composeapp.generated.resources.songrequests_config_allow_spotify
import nomnomzbot.composeapp.generated.resources.songrequests_config_allow_youtube
import nomnomzbot.composeapp.generated.resources.songrequests_config_enabled
import nomnomzbot.composeapp.generated.resources.songrequests_config_title
import nomnomzbot.composeapp.generated.resources.songrequests_empty
import nomnomzbot.composeapp.generated.resources.songrequests_error
import nomnomzbot.composeapp.generated.resources.songrequests_loading
import nomnomzbot.composeapp.generated.resources.songrequests_pause
import nomnomzbot.composeapp.generated.resources.songrequests_position
import nomnomzbot.composeapp.generated.resources.songrequests_queue_title
import nomnomzbot.composeapp.generated.resources.songrequests_remove_action
import nomnomzbot.composeapp.generated.resources.songrequests_remove_action_short
import nomnomzbot.composeapp.generated.resources.songrequests_remove_confirm
import nomnomzbot.composeapp.generated.resources.songrequests_remove_dismiss
import nomnomzbot.composeapp.generated.resources.songrequests_remove_message
import nomnomzbot.composeapp.generated.resources.songrequests_remove_title
import nomnomzbot.composeapp.generated.resources.songrequests_requested_by
import nomnomzbot.composeapp.generated.resources.songrequests_resume
import nomnomzbot.composeapp.generated.resources.songrequests_retry
import nomnomzbot.composeapp.generated.resources.songrequests_row_description
import nomnomzbot.composeapp.generated.resources.songrequests_skip
import nomnomzbot.composeapp.generated.resources.songrequests_token_copy
import nomnomzbot.composeapp.generated.resources.songrequests_token_rotate
import nomnomzbot.composeapp.generated.resources.songrequests_token_rotate_confirm
import nomnomzbot.composeapp.generated.resources.songrequests_token_rotate_dismiss
import nomnomzbot.composeapp.generated.resources.songrequests_token_rotate_message
import nomnomzbot.composeapp.generated.resources.songrequests_token_rotate_title
import nomnomzbot.composeapp.generated.resources.songrequests_token_title
import nomnomzbot.composeapp.generated.resources.songrequests_unknown_requester
import org.jetbrains.compose.resources.stringResource

// The Song Requests page: the channel's live music queue + SR management (config, SR-page token). Loads all
// three in parallel; the queue + controls render immediately. The config section surfaces the key SR toggles
// (enabled, providers) with inline saves. The SR-page token section shows the shareable link + rotate action.
@Composable
fun SongRequestsScreen(controller: SongRequestsController, role: ManagementRole?) {
    val state: SongRequestsState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    val moderate: ManageDecision =
        rememberManageDecision(role, ShellRoute.SongRequests, ManageAction.SongQueueModeration)
    val configure: ManageDecision =
        rememberManageDecision(role, ShellRoute.SongRequests, ManageAction.MusicConfig)

    LaunchedEffect(Unit) { controller.load() }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: SongRequestsState = state) {
            is SongRequestsState.Loading ->
                CenteredMessage(stringResource(Res.string.songrequests_loading))
            is SongRequestsState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is SongRequestsState.Ready ->
                ReadyContent(
                    queue = current.queue,
                    config = current.config,
                    srPageToken = current.srPageToken,
                    actionError = current.actionError,
                    moderate = moderate,
                    configure = configure,
                    onSkip = { scope.launch { controller.skip() } },
                    onPause = { scope.launch { controller.pause() } },
                    onResume = { scope.launch { controller.resume() } },
                    onRemove = { position -> scope.launch { controller.remove(position) } },
                    onUpdateConfig = { body -> scope.launch { controller.updateConfig(body) } },
                    onRotateToken = { scope.launch { controller.rotateSrPageToken() } },
                )
        }
    }
}

@Composable
private fun ReadyContent(
    queue: List<QueuedSong>,
    config: MusicConfig?,
    srPageToken: String?,
    actionError: String?,
    moderate: ManageDecision,
    configure: ManageDecision,
    onSkip: () -> Unit,
    onPause: () -> Unit,
    onResume: () -> Unit,
    onRemove: (position: Int) -> Unit,
    onUpdateConfig: (UpdateMusicConfigBody) -> Unit,
    onRotateToken: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var pendingRemoval: QueuedSong? by remember { mutableStateOf(null) }
    var showRotateConfirm: Boolean by remember { mutableStateOf(false) }

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
        contentPadding = PaddingValues(bottom = spacing.s6),
    ) {
        item(key = "page-header") { PageHeader(title = stringResource(Res.string.shell_nav_song_requests)) }

        // ── Playback controls ────────────────────────────────────────────────
        item {
            PlaybackControls(moderate = moderate, onSkip = onSkip, onPause = onPause, onResume = onResume)
        }

        actionError?.let { detail ->
            item {
                Text(
                    text = stringResource(Res.string.songrequests_action_error, detail),
                    style = typography.sm,
                    color = tokens.destructive,
                    modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s1),
                )
            }
        }

        // ── Queue list ───────────────────────────────────────────────────────
        item {
            Text(
                text = stringResource(Res.string.songrequests_queue_title),
                style = typography.base.copy(fontWeight = FontWeight.SemiBold),
                color = tokens.foreground,
            )
        }

        if (queue.isEmpty()) {
            item {
                CenteredMessage(stringResource(Res.string.songrequests_empty))
            }
        } else {
            items(items = queue, key = { song -> song.position }) { song ->
                QueueRow(song = song, moderate = moderate, onRemove = { pendingRemoval = song })
            }
        }

        // ── Config section ───────────────────────────────────────────────────
        if (config != null) {
            item {
                ConfigSection(
                    config = config,
                    configure = configure,
                    onUpdate = onUpdateConfig,
                )
            }
        }

        // ── SR page token ────────────────────────────────────────────────────
        if (srPageToken != null) {
            item {
                SrTokenSection(
                    token = srPageToken,
                    configure = configure,
                    onRotate = { showRotateConfirm = true },
                )
            }
        }
    }

    // Removal confirmation
    pendingRemoval?.let { song ->
        val title: String = song.trackName.takeIf { it.isNotBlank() } ?: song.artist
        ConfirmDialog(
            title = stringResource(Res.string.songrequests_remove_title),
            message = stringResource(Res.string.songrequests_remove_message, title),
            confirmLabel = stringResource(Res.string.songrequests_remove_confirm),
            dismissLabel = stringResource(Res.string.songrequests_remove_dismiss),
            destructive = true,
            onConfirm = {
                onRemove(song.position)
                pendingRemoval = null
            },
            onDismiss = { pendingRemoval = null },
        )
    }

    // Token-rotate confirmation
    if (showRotateConfirm) {
        ConfirmDialog(
            title = stringResource(Res.string.songrequests_token_rotate_title),
            message = stringResource(Res.string.songrequests_token_rotate_message),
            confirmLabel = stringResource(Res.string.songrequests_token_rotate_confirm),
            dismissLabel = stringResource(Res.string.songrequests_token_rotate_dismiss),
            destructive = true,
            onConfirm = {
                onRotateToken()
                showRotateConfirm = false
            },
            onDismiss = { showRotateConfirm = false },
        )
    }
}

@Composable
private fun ConfigSection(
    config: MusicConfig,
    configure: ManageDecision,
    onUpdate: (UpdateMusicConfigBody) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
            .padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Text(
            text = stringResource(Res.string.songrequests_config_title),
            style = typography.base.copy(fontWeight = FontWeight.SemiBold),
            color = tokens.cardForeground,
        )
        SrToggleRow(
            label = stringResource(Res.string.songrequests_config_enabled),
            checked = config.isEnabled,
            configure = configure,
            onToggle = { onUpdate(UpdateMusicConfigBody(isEnabled = it)) },
        )
        SrToggleRow(
            label = stringResource(Res.string.songrequests_config_allow_spotify),
            checked = config.allowSpotify,
            configure = configure,
            onToggle = { onUpdate(UpdateMusicConfigBody(allowSpotify = it)) },
        )
        SrToggleRow(
            label = stringResource(Res.string.songrequests_config_allow_youtube),
            checked = config.allowYouTube,
            configure = configure,
            onToggle = { onUpdate(UpdateMusicConfigBody(allowYouTube = it)) },
        )
    }
}

@Composable
private fun SrToggleRow(
    label: String,
    checked: Boolean,
    configure: ManageDecision,
    onToggle: (Boolean) -> Unit,
) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.SpaceBetween,
    ) {
        Text(text = label, style = typography.sm, color = tokens.cardForeground)
        ManageGate(decision = configure) { enabled ->
            Switch(
                checked = checked,
                onCheckedChange = { onToggle(it) },
                enabled = enabled,
                colors = SwitchDefaults.colors(
                    checkedTrackColor = tokens.primary,
                    checkedThumbColor = tokens.primaryForeground,
                ),
            )
        }
    }
}

@Composable
private fun SrTokenSection(
    token: String,
    configure: ManageDecision,
    onRotate: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val clipboard = LocalClipboardManager.current

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
            .padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Text(
            text = stringResource(Res.string.songrequests_token_title),
            style = typography.base.copy(fontWeight = FontWeight.SemiBold),
            color = tokens.cardForeground,
        )
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            SelectionContainer(modifier = Modifier.weight(1f)) {
                Text(
                    text = token,
                    style = typography.sm,
                    color = tokens.mutedForeground,
                    maxLines = 2,
                    overflow = TextOverflow.Ellipsis,
                )
            }
            TextButton(onClick = { clipboard.setText(AnnotatedString(token)) }) {
                Text(
                    text = stringResource(Res.string.songrequests_token_copy),
                    color = tokens.primary,
                    style = typography.sm,
                )
            }
        }
        ManageGate(decision = configure) { enabled ->
            TextButton(onClick = onRotate, enabled = enabled) {
                Text(
                    text = stringResource(Res.string.songrequests_token_rotate),
                    color = if (enabled) tokens.destructive else tokens.mutedForeground,
                    style = typography.sm,
                )
            }
        }
    }
}

@Composable
private fun PlaybackControls(
    moderate: ManageDecision,
    onSkip: () -> Unit,
    onPause: () -> Unit,
    onResume: () -> Unit,
) {
    val spacing = LocalSpacing.current

    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        ControlButton(label = stringResource(Res.string.songrequests_skip), moderate = moderate, onClick = onSkip)
        ControlButton(label = stringResource(Res.string.songrequests_pause), moderate = moderate, onClick = onPause)
        ControlButton(label = stringResource(Res.string.songrequests_resume), moderate = moderate, onClick = onResume)
    }
}

@Composable
private fun ControlButton(label: String, moderate: ManageDecision, onClick: () -> Unit) {
    val tokens = LocalTokens.current

    ManageGate(decision = moderate) { enabled ->
        TextButton(
            onClick = onClick,
            enabled = enabled,
            modifier = Modifier.clearAndSetSemantics { contentDescription = label },
        ) {
            Text(text = label, color = if (enabled) tokens.primary else tokens.mutedForeground, maxLines = 1)
        }
    }
}

@Composable
private fun QueueRow(song: QueuedSong, moderate: ManageDecision, onRemove: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val title: String = song.trackName.takeIf { it.isNotBlank() } ?: song.artist
    val requester: String =
        song.requestedBy?.takeIf { it.isNotBlank() }
            ?: stringResource(Res.string.songrequests_unknown_requester)
    val positionLabel: String = stringResource(Res.string.songrequests_position, song.position + 1)
    val requestedLabel: String = stringResource(Res.string.songrequests_requested_by, requester)
    val rowDescription: String =
        stringResource(Res.string.songrequests_row_description, positionLabel, title, requester)
    val removeLabel: String = stringResource(Res.string.songrequests_remove_action, title)

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Badge(
            label = positionLabel,
            background = tokens.secondary,
            foreground = tokens.secondaryForeground,
        )
        Column(
            modifier = Modifier
                .weight(1f)
                .clearAndSetSemantics { contentDescription = rowDescription },
            verticalArrangement = Arrangement.spacedBy(spacing.s1),
        ) {
            Text(
                text = title,
                style = typography.base,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            if (song.artist.isNotBlank()) {
                Text(
                    text = song.artist,
                    style = typography.sm,
                    color = tokens.mutedForeground,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
        }
        Text(
            text = requestedLabel,
            style = typography.xs,
            color = tokens.mutedForeground,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
        ManageGate(decision = moderate) { enabled ->
            TextButton(
                onClick = onRemove,
                enabled = enabled,
                modifier = Modifier.clearAndSetSemantics { contentDescription = removeLabel },
            ) {
                Text(
                    text = stringResource(Res.string.songrequests_remove_action_short),
                    color = if (enabled) tokens.destructive else tokens.mutedForeground,
                    maxLines = 1,
                )
            }
        }
    }
}

@Composable
private fun Badge(
    label: String,
    background: Color,
    foreground: Color,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Box(
        modifier = Modifier
            .clip(RoundedCornerShape(tokens.radius.sm))
            .background(background)
            .padding(horizontal = spacing.s2, vertical = spacing.s1),
    ) {
        Text(text = label, style = typography.xs, color = foreground, maxLines = 1)
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
                text = stringResource(Res.string.songrequests_error, detail),
                style = typography.base,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.songrequests_retry)) }
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
