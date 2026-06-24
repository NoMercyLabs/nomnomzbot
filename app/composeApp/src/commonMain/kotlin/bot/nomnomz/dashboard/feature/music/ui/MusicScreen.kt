// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.music.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
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
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.MusicTrack
import bot.nomnomz.dashboard.core.network.NowPlaying
import bot.nomnomz.dashboard.feature.music.state.MusicController
import bot.nomnomz.dashboard.feature.music.state.MusicState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.music_action_error
import nomnomzbot.composeapp.generated.resources.music_art_placeholder
import nomnomzbot.composeapp.generated.resources.music_empty
import nomnomzbot.composeapp.generated.resources.music_error
import nomnomzbot.composeapp.generated.resources.music_loading
import nomnomzbot.composeapp.generated.resources.music_now_playing_description
import nomnomzbot.composeapp.generated.resources.music_now_playing_label
import nomnomzbot.composeapp.generated.resources.music_pause
import nomnomzbot.composeapp.generated.resources.music_play
import nomnomzbot.composeapp.generated.resources.music_position
import nomnomzbot.composeapp.generated.resources.music_progress
import nomnomzbot.composeapp.generated.resources.music_provider
import nomnomzbot.composeapp.generated.resources.music_queue_empty
import nomnomzbot.composeapp.generated.resources.music_queue_label
import nomnomzbot.composeapp.generated.resources.music_remove_action
import nomnomzbot.composeapp.generated.resources.music_remove_action_short
import nomnomzbot.composeapp.generated.resources.music_remove_confirm
import nomnomzbot.composeapp.generated.resources.music_remove_dismiss
import nomnomzbot.composeapp.generated.resources.music_remove_message
import nomnomzbot.composeapp.generated.resources.music_remove_title
import nomnomzbot.composeapp.generated.resources.music_requested_by
import nomnomzbot.composeapp.generated.resources.music_retry
import nomnomzbot.composeapp.generated.resources.music_row_description
import nomnomzbot.composeapp.generated.resources.music_skip
import nomnomzbot.composeapp.generated.resources.music_unknown_requester
import nomnomzbot.composeapp.generated.resources.music_unknown_track
import org.jetbrains.compose.resources.stringResource

// The Music page: the channel's live playback, made controllable — every track is real data from
// [MusicController] (the backend sources now-playing + the queue from the connected music provider). The
// screen is a pure projection of the controller's state; it loads on first composition and offers a retry on
// failure. The now-playing card shows the current track with its progress and provider, and a control row
// drives playback (Play/Pause toggles on the live isPlaying flag; Skip advances). Each queued track carries a
// Remove affordance that only runs once confirmed in the shared ConfirmDialog (the controller reloads on
// success, so the now-playing and queue both re-project).
@Composable
fun MusicScreen(controller: MusicController) {
    val state: MusicState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    LaunchedEffect(Unit) { controller.load() }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: MusicState = state) {
            is MusicState.Loading -> CenteredMessage(stringResource(Res.string.music_loading))
            is MusicState.Empty -> CenteredMessage(stringResource(Res.string.music_empty))
            is MusicState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is MusicState.Ready ->
                ReadyContent(
                    nowPlaying = current.nowPlaying,
                    queue = current.queue,
                    actionError = current.actionError,
                    onPlay = { scope.launch { controller.resume() } },
                    onPause = { scope.launch { controller.pause() } },
                    onSkip = { scope.launch { controller.skip() } },
                    onRemove = { position -> scope.launch { controller.remove(position) } },
                )
        }
    }
}

@Composable
private fun ReadyContent(
    nowPlaying: NowPlaying?,
    queue: List<MusicTrack>,
    actionError: String?,
    onPlay: () -> Unit,
    onPause: () -> Unit,
    onSkip: () -> Unit,
    onRemove: (position: Int) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    // The queued track awaiting confirmation, if any — the screen owns the dialog's open/closed state.
    var pendingRemoval: MusicTrack? by remember { mutableStateOf(null) }

    Column(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        if (nowPlaying != null) {
            NowPlayingCard(
                nowPlaying = nowPlaying,
                onPlay = onPlay,
                onPause = onPause,
                onSkip = onSkip,
            )
        }

        actionError?.let { detail ->
            Text(
                text = stringResource(Res.string.music_action_error, detail),
                style = typography.sm,
                color = tokens.destructive,
                modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s1),
            )
        }

        Text(
            text = stringResource(Res.string.music_queue_label),
            style = typography.sm,
            color = tokens.mutedForeground,
            modifier = Modifier.padding(horizontal = spacing.s1),
        )

        if (queue.isEmpty()) {
            Text(
                text = stringResource(Res.string.music_queue_empty),
                style = typography.sm,
                color = tokens.mutedForeground,
                modifier = Modifier.padding(horizontal = spacing.s1),
            )
        } else {
            LazyColumn(
                modifier = Modifier.fillMaxSize(),
                verticalArrangement = Arrangement.spacedBy(spacing.s2),
            ) {
                items(items = queue, key = { track -> track.position }) { track ->
                    QueueRow(track = track, onRemove = { pendingRemoval = track })
                }
            }
        }
    }

    pendingRemoval?.let { track ->
        val title: String = track.trackName.takeIf { it.isNotBlank() } ?: track.artist
        ConfirmDialog(
            title = stringResource(Res.string.music_remove_title),
            message = stringResource(Res.string.music_remove_message, title),
            confirmLabel = stringResource(Res.string.music_remove_confirm),
            dismissLabel = stringResource(Res.string.music_remove_dismiss),
            destructive = true,
            onConfirm = {
                onRemove(track.position)
                pendingRemoval = null
            },
            onDismiss = { pendingRemoval = null },
        )
    }
}

@Composable
private fun NowPlayingCard(
    nowPlaying: NowPlaying,
    onPlay: () -> Unit,
    onPause: () -> Unit,
    onSkip: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val title: String =
        nowPlaying.trackName?.takeIf { it.isNotBlank() }
            ?: stringResource(Res.string.music_unknown_track)
    val artist: String = nowPlaying.artist.orEmpty()
    val album: String = nowPlaying.album.orEmpty()
    val requester: String =
        nowPlaying.requestedBy?.takeIf { it.isNotBlank() }
            ?: stringResource(Res.string.music_unknown_requester)
    val cardDescription: String =
        stringResource(Res.string.music_now_playing_description, title, artist.ifBlank { requester })

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
            .padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Text(
            text = stringResource(Res.string.music_now_playing_label),
            style = typography.xs,
            color = tokens.mutedForeground,
        )

        Row(
            modifier = Modifier
                .fillMaxWidth()
                // One node for screen readers: "Now playing: Track Title, Artist".
                .clearAndSetSemantics { contentDescription = cardDescription },
            horizontalArrangement = Arrangement.spacedBy(spacing.s3),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            AlbumArt(title = title)
            Column(
                modifier = Modifier.weight(1f),
                verticalArrangement = Arrangement.spacedBy(spacing.s1),
            ) {
                Text(
                    text = title,
                    style = typography.base,
                    color = tokens.cardForeground,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                if (artist.isNotBlank()) {
                    Text(
                        text = artist,
                        style = typography.sm,
                        color = tokens.mutedForeground,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                }
                if (album.isNotBlank()) {
                    Text(
                        text = album,
                        style = typography.xs,
                        color = tokens.mutedForeground,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                }
            }
        }

        ProgressBar(progressMs = nowPlaying.progressMs, durationMs = nowPlaying.durationMs)

        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(spacing.s2),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Badge(
                label = stringResource(Res.string.music_provider, nowPlaying.provider),
                background = tokens.secondary,
                foreground = tokens.secondaryForeground,
            )
            Text(
                text = stringResource(Res.string.music_requested_by, requester),
                style = typography.xs,
                color = tokens.mutedForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }

        PlaybackControls(
            isPlaying = nowPlaying.isPlaying,
            onPlay = onPlay,
            onPause = onPause,
            onSkip = onSkip,
        )
    }
}

@Composable
private fun PlaybackControls(
    isPlaying: Boolean,
    onPlay: () -> Unit,
    onPause: () -> Unit,
    onSkip: () -> Unit,
) {
    val spacing = LocalSpacing.current

    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        // Play and pause are the same backend toggle; the live isPlaying flag decides which one is offered.
        if (isPlaying) {
            ControlButton(label = stringResource(Res.string.music_pause), onClick = onPause)
        } else {
            ControlButton(label = stringResource(Res.string.music_play), onClick = onPlay)
        }
        ControlButton(label = stringResource(Res.string.music_skip), onClick = onSkip)
    }
}

@Composable
private fun ControlButton(label: String, onClick: () -> Unit) {
    val tokens = LocalTokens.current

    TextButton(
        onClick = onClick,
        modifier = Modifier.clearAndSetSemantics { contentDescription = label },
    ) {
        Text(text = label, color = tokens.primary, maxLines = 1)
    }
}

// A determinate progress bar from the provider's progress/duration; degrades to an empty track when the
// duration is unknown (0), so it never divides by zero or overflows.
@Composable
private fun ProgressBar(progressMs: Int, durationMs: Int) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val fraction: Float =
        if (durationMs > 0) (progressMs.toFloat() / durationMs.toFloat()).coerceIn(0f, 1f) else 0f
    val progressLabel: String =
        stringResource(Res.string.music_progress, formatMs(progressMs), formatMs(durationMs))

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .height(spacing.s2)
                .clip(RoundedCornerShape(tokens.radius.sm))
                .background(tokens.muted)
                .clearAndSetSemantics { contentDescription = progressLabel },
        ) {
            Box(
                modifier = Modifier
                    .fillMaxWidth(fraction)
                    .height(spacing.s2)
                    .clip(RoundedCornerShape(tokens.radius.sm))
                    .background(tokens.primary),
            )
        }
        Text(text = progressLabel, style = typography.xs, color = tokens.mutedForeground)
    }
}

// The album art slot. No remote-image loader is wired into the dashboard yet, so this renders the track's
// initial as a placeholder tile (the same approach the profile Avatar uses for an unrendered image URL),
// keeping the page on-token and dependency-free; the real artwork lands when an image loader is introduced.
@Composable
private fun AlbumArt(title: String) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val initial: String = title.trim().firstOrNull()?.uppercase() ?: "?"
    val description: String = stringResource(Res.string.music_art_placeholder)

    Box(
        modifier = Modifier
            .size(spacing.s12)
            .clip(RoundedCornerShape(tokens.radius.md))
            .background(tokens.muted)
            .clearAndSetSemantics { contentDescription = description },
        contentAlignment = Alignment.Center,
    ) {
        Text(text = initial, style = typography.base, color = tokens.mutedForeground)
    }
}

@Composable
private fun QueueRow(track: MusicTrack, onRemove: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val title: String = track.trackName.takeIf { it.isNotBlank() } ?: track.artist
    val requester: String =
        track.requestedBy?.takeIf { it.isNotBlank() }
            ?: stringResource(Res.string.music_unknown_requester)
    val positionLabel: String = stringResource(Res.string.music_position, track.position + 1)
    val requestedLabel: String = stringResource(Res.string.music_requested_by, requester)
    val rowDescription: String =
        stringResource(Res.string.music_row_description, positionLabel, title, requester)
    val removeLabel: String = stringResource(Res.string.music_remove_action, title)

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
            if (track.artist.isNotBlank()) {
                Text(
                    text = track.artist,
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
        TextButton(
            onClick = onRemove,
            modifier = Modifier.clearAndSetSemantics { contentDescription = removeLabel },
        ) {
            Text(
                text = stringResource(Res.string.music_remove_action_short),
                color = tokens.destructive,
                maxLines = 1,
            )
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
                text = stringResource(Res.string.music_error, detail),
                style = typography.base,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.music_retry)) }
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

// Render a millisecond duration as m:ss (e.g. 213000 → "3:33"). Negative inputs clamp to zero.
private fun formatMs(ms: Int): String {
    val totalSeconds: Int = (ms.coerceAtLeast(0)) / 1000
    val minutes: Int = totalSeconds / 60
    val seconds: Int = totalSeconds % 60
    val paddedSeconds: String = if (seconds < 10) "0$seconds" else seconds.toString()
    return "$minutes:$paddedSeconds"
}
