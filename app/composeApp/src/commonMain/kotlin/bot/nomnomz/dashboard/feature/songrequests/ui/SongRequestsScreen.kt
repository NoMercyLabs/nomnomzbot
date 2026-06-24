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
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
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
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.QueuedSong
import bot.nomnomz.dashboard.feature.songrequests.state.SongRequestsController
import bot.nomnomz.dashboard.feature.songrequests.state.SongRequestsState
import bot.nomnomz.dashboard.feature.shell.nav.ManageAction
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.songrequests_action_error
import nomnomzbot.composeapp.generated.resources.songrequests_empty
import nomnomzbot.composeapp.generated.resources.songrequests_error
import nomnomzbot.composeapp.generated.resources.songrequests_loading
import nomnomzbot.composeapp.generated.resources.songrequests_pause
import nomnomzbot.composeapp.generated.resources.songrequests_position
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
import nomnomzbot.composeapp.generated.resources.songrequests_unknown_requester
import org.jetbrains.compose.resources.stringResource

// The Song Requests page: the channel's live music queue, made controllable — every track is real data from
// [SongRequestsController] (the backend sources it from the connected music provider). The screen is a pure
// projection of the controller's state; it loads on first composition and offers a retry on failure. A header
// control area drives playback (Skip / Pause / Resume act directly), and each queued song carries a Remove
// affordance that only runs once confirmed in the shared ConfirmDialog (the controller reloads on success).
@Composable
fun SongRequestsScreen(controller: SongRequestsController, role: ManagementRole?) {
    val state: SongRequestsState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    // Every control on this page is live-queue moderation (skip/pause/resume/remove). The spec drops that below
    // the page's Editor floor to Moderator (frontend-ia.md §3 Media row: "queue moderation: Moderator"), so a
    // Mod can keep the queue moving — one decision feeds all the controls; below Moderator they disable with
    // reason (§7). The backend re-checks every write.
    val moderate: ManageDecision =
        rememberManageDecision(role, ShellRoute.SongRequests, ManageAction.SongQueueModeration)

    LaunchedEffect(Unit) { controller.load() }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: SongRequestsState = state) {
            is SongRequestsState.Loading ->
                CenteredMessage(stringResource(Res.string.songrequests_loading))
            is SongRequestsState.Empty ->
                CenteredMessage(stringResource(Res.string.songrequests_empty))
            is SongRequestsState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is SongRequestsState.Ready ->
                ReadyContent(
                    queue = current.queue,
                    actionError = current.actionError,
                    moderate = moderate,
                    onSkip = { scope.launch { controller.skip() } },
                    onPause = { scope.launch { controller.pause() } },
                    onResume = { scope.launch { controller.resume() } },
                    onRemove = { position -> scope.launch { controller.remove(position) } },
                )
        }
    }
}

@Composable
private fun ReadyContent(
    queue: List<QueuedSong>,
    actionError: String?,
    moderate: ManageDecision,
    onSkip: () -> Unit,
    onPause: () -> Unit,
    onResume: () -> Unit,
    onRemove: (position: Int) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    // The queued song awaiting confirmation, if any — the screen owns the dialog's open/closed state.
    var pendingRemoval: QueuedSong? by remember { mutableStateOf(null) }

    Column(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        PlaybackControls(moderate = moderate, onSkip = onSkip, onPause = onPause, onResume = onResume)

        actionError?.let { detail ->
            Text(
                text = stringResource(Res.string.songrequests_action_error, detail),
                style = typography.sm,
                color = tokens.destructive,
                modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s1),
            )
        }

        LazyColumn(
            modifier = Modifier.fillMaxSize(),
            verticalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            items(items = queue, key = { song -> song.position }) { song ->
                QueueRow(song = song, moderate = moderate, onRemove = { pendingRemoval = song })
            }
        }
    }

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
                // One node for screen readers: "1, Track Title, requested by Stoney_Eagle".
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

    Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Text(text = text, style = typography.base, color = tokens.mutedForeground)
    }
}
