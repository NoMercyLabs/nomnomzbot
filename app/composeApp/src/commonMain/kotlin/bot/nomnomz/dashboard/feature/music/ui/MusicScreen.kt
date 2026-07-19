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
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.verticalScroll
import bot.nomnomz.dashboard.core.designsystem.component.Badge as DsBadge
import bot.nomnomz.dashboard.core.designsystem.component.TabsList
import bot.nomnomz.dashboard.core.designsystem.component.TabsTrigger
import bot.nomnomz.dashboard.core.designsystem.component.Button
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
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.CopyLinkButton
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.GlyphButton
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.designsystem.icon.RefreshGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.TrashGlyph
import bot.nomnomz.dashboard.core.designsystem.component.ActionErrorBanner
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.network.BlockedTrack
import bot.nomnomz.dashboard.core.network.MusicConfig
import bot.nomnomz.dashboard.core.network.MusicDevice
import bot.nomnomz.dashboard.core.network.MusicPlaylist
import bot.nomnomz.dashboard.core.network.MusicTrack
import bot.nomnomz.dashboard.core.network.NowPlaying
import bot.nomnomz.dashboard.core.network.UpdateMusicConfigBody
import bot.nomnomz.dashboard.feature.music.state.MusicController
import bot.nomnomz.dashboard.feature.music.state.MusicState
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import kotlinx.coroutines.delay
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
import nomnomzbot.composeapp.generated.resources.music_seek_back
import nomnomzbot.composeapp.generated.resources.music_seek_back_description
import nomnomzbot.composeapp.generated.resources.music_seek_forward
import nomnomzbot.composeapp.generated.resources.music_seek_forward_description
import nomnomzbot.composeapp.generated.resources.music_queue_empty
import nomnomzbot.composeapp.generated.resources.music_queue_label
import nomnomzbot.composeapp.generated.resources.music_remove_action
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
import nomnomzbot.composeapp.generated.resources.music_add_title
import nomnomzbot.composeapp.generated.resources.music_add_query
import nomnomzbot.composeapp.generated.resources.music_add_requested_by
import nomnomzbot.composeapp.generated.resources.music_add_action
import nomnomzbot.composeapp.generated.resources.music_config_title
import nomnomzbot.composeapp.generated.resources.music_config_enabled
import nomnomzbot.composeapp.generated.resources.music_config_provider
import nomnomzbot.composeapp.generated.resources.music_config_provider_auto
import nomnomzbot.composeapp.generated.resources.music_config_provider_spotify
import nomnomzbot.composeapp.generated.resources.music_config_provider_youtube
import nomnomzbot.composeapp.generated.resources.music_config_max_queue
import nomnomzbot.composeapp.generated.resources.music_config_max_per_user
import nomnomzbot.composeapp.generated.resources.music_config_allow_youtube
import nomnomzbot.composeapp.generated.resources.music_config_allow_spotify
import nomnomzbot.composeapp.generated.resources.music_config_trust
import nomnomzbot.composeapp.generated.resources.music_config_save
import nomnomzbot.composeapp.generated.resources.music_token_title
import nomnomzbot.composeapp.generated.resources.music_share_link_copied
import nomnomzbot.composeapp.generated.resources.music_share_link_copy
import nomnomzbot.composeapp.generated.resources.music_share_link_value
import nomnomzbot.composeapp.generated.resources.music_token_value
import nomnomzbot.composeapp.generated.resources.music_token_rotate
import nomnomzbot.composeapp.generated.resources.music_token_rotate_title
import nomnomzbot.composeapp.generated.resources.music_token_rotate_message
import nomnomzbot.composeapp.generated.resources.music_token_rotate_confirm
import nomnomzbot.composeapp.generated.resources.music_token_rotate_dismiss
import nomnomzbot.composeapp.generated.resources.music_remote_title
import nomnomzbot.composeapp.generated.resources.music_shuffle_label
import nomnomzbot.composeapp.generated.resources.music_repeat_off
import nomnomzbot.composeapp.generated.resources.music_repeat_track
import nomnomzbot.composeapp.generated.resources.music_repeat_context
import nomnomzbot.composeapp.generated.resources.music_devices_title
import nomnomzbot.composeapp.generated.resources.music_device_transfer
import nomnomzbot.composeapp.generated.resources.music_playlists_title
import nomnomzbot.composeapp.generated.resources.music_playlist_play
import nomnomzbot.composeapp.generated.resources.music_blocked_title
import nomnomzbot.composeapp.generated.resources.music_blocked_empty
import nomnomzbot.composeapp.generated.resources.music_blocked_reason
import nomnomzbot.composeapp.generated.resources.music_blocked_date
import nomnomzbot.composeapp.generated.resources.music_blocked_count
import nomnomzbot.composeapp.generated.resources.music_blocked_prev
import nomnomzbot.composeapp.generated.resources.music_blocked_next
import nomnomzbot.composeapp.generated.resources.music_blocked_unblock
import nomnomzbot.composeapp.generated.resources.music_blocked_unblock_title
import nomnomzbot.composeapp.generated.resources.music_blocked_unblock_message
import nomnomzbot.composeapp.generated.resources.music_blocked_unblock_confirm
import nomnomzbot.composeapp.generated.resources.music_blocked_unblock_dismiss
import nomnomzbot.composeapp.generated.resources.music_block_form_title
import nomnomzbot.composeapp.generated.resources.music_block_provider
import nomnomzbot.composeapp.generated.resources.music_block_uri
import nomnomzbot.composeapp.generated.resources.music_block_track_title
import nomnomzbot.composeapp.generated.resources.music_block_reason
import nomnomzbot.composeapp.generated.resources.music_block_action
import nomnomzbot.composeapp.generated.resources.shell_nav_music
import bot.nomnomz.dashboard.core.realtime.HubEvent
import kotlinx.coroutines.flow.SharedFlow
import org.jetbrains.compose.resources.stringResource

// The Music page: the channel's live playback, made controllable — every track is real data from
// [MusicController] (the backend sources now-playing + the queue from the connected music provider). The
// screen is a pure projection of the controller's state; it loads on first composition and offers a retry on
// failure. The now-playing card shows the current track with its progress and provider, and a control row
// drives playback (Play/Pause toggles on the live isPlaying flag; Skip advances). Each queued track carries a
// Remove affordance that only runs once confirmed in the shared ConfirmDialog (the controller reloads on
// success, so the now-playing and queue both re-project).
@Composable
fun MusicScreen(
    controller: MusicController,
    role: ManagementRole?,
    hubEvents: SharedFlow<HubEvent>? = null,
) {
    val state: MusicState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    // One decision for the whole page: Music gates every playback/queue write control at its single Editor
    // manage floor (frontend-ia.md §3). A caller below it sees now-playing and the queue but every
    // play/pause/skip/remove control renders disabled with "Requires Editor" (§7); the backend re-checks every
    // write regardless.
    val manage: ManageDecision = rememberManageDecision(role, ShellRoute.Music)

    LaunchedEffect(Unit) { controller.load() }
    if (hubEvents != null) {
        LaunchedEffect(hubEvents) { controller.subscribeToHub(hubEvents) }
    }

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
                    config = current.config,
                    srPageToken = current.srPageToken,
                    shareLink = current.shareLink,
                    devices = current.devices,
                    playlists = current.playlists,
                    blockedTracks = current.blockedTracks,
                    blockedPage = current.blockedPage,
                    blockedTotal = current.blockedTotal,
                    blockedHasMore = current.blockedHasMore,
                    actionError = current.actionError,
                    manage = manage,
                    onPlay = { scope.launch { controller.resume() } },
                    onPause = { scope.launch { controller.pause() } },
                    onSeek = { positionMs -> scope.launch { controller.seek(positionMs) } },
                    onSkip = { scope.launch { controller.skip() } },
                    onRemove = { position -> scope.launch { controller.remove(position) } },
                    onAddToQueue = { query, requestedBy -> scope.launch { controller.addToQueue(query, requestedBy) } },
                    onSaveConfig = { body -> scope.launch { controller.updateConfig(body) } },
                    onRotateToken = { scope.launch { controller.rotateSrPageToken() } },
                    onSetShuffle = { enabled -> scope.launch { controller.setShuffle(enabled) } },
                    onSetRepeat = { mode -> scope.launch { controller.setRepeat(mode) } },
                    onTransfer = { deviceId -> scope.launch { controller.transferPlayback(deviceId, play = true) } },
                    onPlayPlaylist = { uri -> scope.launch { controller.playContext(uri) } },
                    onBlockTrack = { provider, trackUri, title, reason ->
                        scope.launch { controller.blockTrack(provider, trackUri, title, reason) }
                    },
                    onUnblockTrack = { id -> scope.launch { controller.unblockTrack(id) } },
                    onBlockedPage = { page -> scope.launch { controller.loadBlockedTracks(page) } },
                )
        }
    }
}

@Composable
private fun ReadyContent(
    nowPlaying: NowPlaying?,
    queue: List<MusicTrack>,
    config: MusicConfig?,
    srPageToken: String?,
    shareLink: String?,
    devices: List<MusicDevice>,
    playlists: List<MusicPlaylist>,
    blockedTracks: List<BlockedTrack>,
    blockedPage: Int,
    blockedTotal: Int,
    blockedHasMore: Boolean,
    actionError: String?,
    manage: ManageDecision,
    onPlay: () -> Unit,
    onPause: () -> Unit,
    onSeek: (positionMs: Int) -> Unit,
    onSkip: () -> Unit,
    onRemove: (position: Int) -> Unit,
    onAddToQueue: (query: String, requestedBy: String) -> Unit,
    onSaveConfig: (UpdateMusicConfigBody) -> Unit,
    onRotateToken: () -> Unit,
    onSetShuffle: (Boolean) -> Unit,
    onSetRepeat: (String) -> Unit,
    onTransfer: (deviceId: String) -> Unit,
    onPlayPlaylist: (uri: String) -> Unit,
    onBlockTrack: (provider: String, trackUri: String, title: String, reason: String?) -> Unit,
    onUnblockTrack: (blockedTrackId: String) -> Unit,
    onBlockedPage: (page: Int) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var pendingRemoval: MusicTrack? by remember { mutableStateOf(null) }
    var pendingRotate: Boolean by remember { mutableStateOf(false) }
    var pendingUnblock: BlockedTrack? by remember { mutableStateOf(null) }

    Column(
        modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        PageHeader(title = stringResource(Res.string.shell_nav_music))
        if (nowPlaying != null) {
            NowPlayingCard(
                nowPlaying = nowPlaying,
                manage = manage,
                onPlay = onPlay,
                onPause = onPause,
                onSeek = onSeek,
                onSkip = onSkip,
            )
        }

        actionError?.let { detail ->
            ActionErrorBanner(message = stringResource(Res.string.music_action_error, detail))
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
            Card(modifier = Modifier.fillMaxWidth()) {
                Column {
                    queue.forEachIndexed { index, track ->
                        QueueRow(track = track, manage = manage, onRemove = { pendingRemoval = track })
                        if (index < queue.lastIndex) {
                            Separator()
                        }
                    }
                }
            }
        }

        // ── Add to queue ──────────────────────────────────────────────────
        Separator()
        AddToQueueSection(manage = manage, onAdd = onAddToQueue)

        // ── SR config ────────────────────────────────────────────────────
        if (config != null) {
            Separator()
            MusicConfigSection(config = config, manage = manage, onSave = onSaveConfig)
        }

        // ── SR-page token + shareable link ────────────────────────────────
        if (srPageToken != null) {
            Separator()
            SrTokenSection(
                token = srPageToken,
                shareLink = shareLink,
                manage = manage,
                onRotate = { pendingRotate = true },
            )
        }

        // ── Remote controls (shuffle / repeat / devices / playlists) ──────
        if (devices.isNotEmpty() || playlists.isNotEmpty()) {
            Separator()
            RemoteControlsSection(
                devices = devices,
                playlists = playlists,
                manage = manage,
                onSetShuffle = onSetShuffle,
                onSetRepeat = onSetRepeat,
                onTransfer = onTransfer,
                onPlayPlaylist = onPlayPlaylist,
            )
        }

        // ── Blocked songs (the legacy `!bansong` list) ────────────────────
        Separator()
        BlockedTracksSection(
            blockedTracks = blockedTracks,
            blockedPage = blockedPage,
            blockedTotal = blockedTotal,
            blockedHasMore = blockedHasMore,
            manage = manage,
            onBlockTrack = onBlockTrack,
            onUnblock = { track -> pendingUnblock = track },
            onPage = onBlockedPage,
        )
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

    pendingUnblock?.let { track ->
        val title: String = track.title.takeIf { it.isNotBlank() } ?: track.trackUri
        ConfirmDialog(
            title = stringResource(Res.string.music_blocked_unblock_title),
            message = stringResource(Res.string.music_blocked_unblock_message, title),
            confirmLabel = stringResource(Res.string.music_blocked_unblock_confirm),
            dismissLabel = stringResource(Res.string.music_blocked_unblock_dismiss),
            destructive = true,
            onConfirm = {
                onUnblockTrack(track.id)
                pendingUnblock = null
            },
            onDismiss = { pendingUnblock = null },
        )
    }

    if (pendingRotate) {
        ConfirmDialog(
            title = stringResource(Res.string.music_token_rotate_title),
            message = stringResource(Res.string.music_token_rotate_message),
            confirmLabel = stringResource(Res.string.music_token_rotate_confirm),
            dismissLabel = stringResource(Res.string.music_token_rotate_dismiss),
            destructive = true,
            onConfirm = {
                pendingRotate = false
                onRotateToken()
            },
            onDismiss = { pendingRotate = false },
        )
    }
}

@Composable
private fun NowPlayingCard(
    nowPlaying: NowPlaying,
    manage: ManageDecision,
    onPlay: () -> Unit,
    onPause: () -> Unit,
    onSeek: (positionMs: Int) -> Unit,
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

    // A locally-ticking progress: the backend now-playing is only re-read on a poll/hub push, so the bar would
    // otherwise sit still between fetches. Seed from the fetched progress (re-seeded whenever a fresh snapshot
    // for this track lands) and advance it once a second while the track is playing, capped at the duration.
    var tickedMs: Int by
        remember(nowPlaying.trackName, nowPlaying.progressMs, nowPlaying.isPlaying) {
            mutableStateOf(nowPlaying.progressMs)
        }
    LaunchedEffect(nowPlaying.trackName, nowPlaying.progressMs, nowPlaying.isPlaying, nowPlaying.durationMs) {
        if (!nowPlaying.isPlaying) return@LaunchedEffect
        while (nowPlaying.durationMs <= 0 || tickedMs < nowPlaying.durationMs) {
            delay(1_000)
            tickedMs =
                if (nowPlaying.durationMs > 0) (tickedMs + 1_000).coerceAtMost(nowPlaying.durationMs)
                else tickedMs + 1_000
        }
    }

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

        ProgressBar(progressMs = tickedMs, durationMs = nowPlaying.durationMs)

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
            progressMs = tickedMs,
            durationMs = nowPlaying.durationMs,
            manage = manage,
            onPlay = onPlay,
            onPause = onPause,
            onSeek = onSeek,
            onSkip = onSkip,
        )
    }
}

@Composable
private fun PlaybackControls(
    isPlaying: Boolean,
    progressMs: Int,
    durationMs: Int,
    manage: ManageDecision,
    onPlay: () -> Unit,
    onPause: () -> Unit,
    onSeek: (positionMs: Int) -> Unit,
    onSkip: () -> Unit,
) {
    val spacing = LocalSpacing.current

    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        ControlButton(
            label = stringResource(Res.string.music_seek_back),
            description = stringResource(Res.string.music_seek_back_description),
            manage = manage,
            onClick = { onSeek((progressMs - 10_000).coerceAtLeast(0)) },
        )
        // Play and pause are the same backend toggle; the live isPlaying flag decides which one is offered.
        if (isPlaying) {
            ControlButton(label = stringResource(Res.string.music_pause), manage = manage, onClick = onPause)
        } else {
            ControlButton(label = stringResource(Res.string.music_play), manage = manage, onClick = onPlay)
        }
        ControlButton(
            label = stringResource(Res.string.music_seek_forward),
            description = stringResource(Res.string.music_seek_forward_description),
            manage = manage,
            onClick = { onSeek((progressMs + 10_000).coerceAtMost(durationMs)) },
        )
        ControlButton(label = stringResource(Res.string.music_skip), manage = manage, onClick = onSkip)
    }
}

@Composable
private fun ControlButton(
    label: String,
    manage: ManageDecision,
    onClick: () -> Unit,
    description: String = label,
) {
    val tokens = LocalTokens.current

    ManageGate(decision = manage) { enabled ->
        TextButton(
            onClick = onClick,
            enabled = enabled,
            modifier = Modifier.clearAndSetSemantics { contentDescription = description },
        ) {
            Text(text = label, color = if (enabled) tokens.primary else tokens.mutedForeground, maxLines = 1)
        }
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
private fun QueueRow(track: MusicTrack, manage: ManageDecision, onRemove: () -> Unit) {
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
        ManageGate(decision = manage) { enabled ->
            GlyphButton(
                imageVector = TrashGlyph,
                label = removeLabel,
                onClick = onRemove,
                enabled = enabled,
                tint = tokens.destructive,
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

// ── Add to queue ─────────────────────────────────────────────────────────────

@Composable
private fun AddToQueueSection(manage: ManageDecision, onAdd: (query: String, requestedBy: String) -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var query: String by remember { mutableStateOf("") }
    var requestedBy: String by remember { mutableStateOf("") }
    val canAdd: Boolean = query.isNotBlank() && requestedBy.isNotBlank()

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
        Text(text = stringResource(Res.string.music_add_title), style = typography.base, color = tokens.cardForeground)
        AppTextField(
            value = query,
            onValueChange = { query = it },
            label = stringResource(Res.string.music_add_query),
            isError = false,
            errorText = null,
            modifier = Modifier.fillMaxWidth(),
        )
        AppTextField(
            value = requestedBy,
            onValueChange = { requestedBy = it },
            label = stringResource(Res.string.music_add_requested_by),
            isError = false,
            errorText = null,
            modifier = Modifier.fillMaxWidth(),
        )
        ManageGate(decision = manage) { enabled ->
            Button(
                onClick = {
                    onAdd(query, requestedBy)
                    query = ""
                    requestedBy = ""
                },
                enabled = enabled && canAdd,
            ) {
                Text(text = stringResource(Res.string.music_add_action))
            }
        }
    }
}

// ── Music / SR config ────────────────────────────────────────────────────────

@Composable
private fun MusicConfigSection(config: MusicConfig, manage: ManageDecision, onSave: (UpdateMusicConfigBody) -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var isEnabled: Boolean by remember(config) { mutableStateOf(config.isEnabled) }
    var preferredProvider: String by remember(config) { mutableStateOf(config.preferredProvider) }
    var maxQueueSize: String by remember(config) { mutableStateOf(config.maxQueueSize.toString()) }
    var maxPerUser: String by remember(config) { mutableStateOf(config.maxRequestsPerUser.toString()) }
    var allowYouTube: Boolean by remember(config) { mutableStateOf(config.allowYouTube) }
    var allowSpotify: Boolean by remember(config) { mutableStateOf(config.allowSpotify) }
    var minTrustLevel: String by remember(config) { mutableStateOf(config.minTrustLevel) }

    val trustLevels: List<String> = listOf("everyone", "subscribers", "vip", "moderators", "broadcaster")
    val providerOptions: List<Pair<String, String>> = listOf(
        "auto" to stringResource(Res.string.music_config_provider_auto),
        "spotify" to stringResource(Res.string.music_config_provider_spotify),
        "youtube" to stringResource(Res.string.music_config_provider_youtube),
    )

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
        Text(text = stringResource(Res.string.music_config_title), style = typography.base, color = tokens.cardForeground)

        // Enabled toggle
        ManageGate(decision = manage) { enabled ->
            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.SpaceBetween,
            ) {
                Text(text = stringResource(Res.string.music_config_enabled), color = tokens.cardForeground)
                Switch(
                    checked = isEnabled,
                    onCheckedChange = { isEnabled = it },
                    enabled = enabled,
                )
            }
        }

        // Provider selector
        Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
            Text(text = stringResource(Res.string.music_config_provider), style = typography.sm, color = tokens.mutedForeground)
            TabsList {
                providerOptions.forEach { (key, label) ->
                    ManageGate(decision = manage) { gateEnabled ->
                        TabsTrigger(
                            selected = preferredProvider == key,
                            onClick = { preferredProvider = key },
                            enabled = gateEnabled,
                        ) {
                            Text(label, maxLines = 1)
                        }
                    }
                }
            }
        }

        // Max queue / per-user fields
        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s3)) {
            AppTextField(
                value = maxQueueSize,
                onValueChange = { maxQueueSize = it },
                label = stringResource(Res.string.music_config_max_queue),
                isError = false,
                errorText = null,
                modifier = Modifier.weight(1f),
            )
            AppTextField(
                value = maxPerUser,
                onValueChange = { maxPerUser = it },
                label = stringResource(Res.string.music_config_max_per_user),
                isError = false,
                errorText = null,
                modifier = Modifier.weight(1f),
            )
        }

        // Allow YouTube / Spotify toggles
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(spacing.s4),
        ) {
            ManageGate(decision = manage) { enabled ->
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                    Switch(
                        checked = allowYouTube,
                        onCheckedChange = { allowYouTube = it },
                        enabled = enabled,
                    )
                    Text(text = stringResource(Res.string.music_config_allow_youtube), style = typography.sm, color = tokens.cardForeground)
                }
            }
            ManageGate(decision = manage) { enabled ->
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                    Switch(
                        checked = allowSpotify,
                        onCheckedChange = { allowSpotify = it },
                        enabled = enabled,
                    )
                    Text(text = stringResource(Res.string.music_config_allow_spotify), style = typography.sm, color = tokens.cardForeground)
                }
            }
        }

        // Trust level
        Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
            Text(text = stringResource(Res.string.music_config_trust), style = typography.sm, color = tokens.mutedForeground)
            FlowRow(
                modifier =
                    Modifier
                        .clip(RoundedCornerShape(tokens.radius.md))
                        .background(tokens.muted)
                        .padding(spacing.s1),
                horizontalArrangement = Arrangement.spacedBy(spacing.s1),
                verticalArrangement = Arrangement.spacedBy(spacing.s1),
            ) {
                trustLevels.forEach { level ->
                    ManageGate(decision = manage) { gateEnabled ->
                        TabsTrigger(
                            selected = minTrustLevel == level,
                            onClick = { minTrustLevel = level },
                            enabled = gateEnabled,
                        ) {
                            Text(level, maxLines = 1)
                        }
                    }
                }
            }
        }

        ManageGate(decision = manage) { enabled ->
            Button(
                onClick = {
                    onSave(
                        UpdateMusicConfigBody(
                            isEnabled = isEnabled,
                            preferredProvider = preferredProvider,
                            maxQueueSize = maxQueueSize.toIntOrNull(),
                            maxRequestsPerUser = maxPerUser.toIntOrNull(),
                            allowYouTube = allowYouTube,
                            allowSpotify = allowSpotify,
                            minTrustLevel = minTrustLevel,
                        )
                    )
                },
                enabled = enabled,
            ) {
                Text(text = stringResource(Res.string.music_config_save))
            }
        }
    }
}

// ── SR page token ─────────────────────────────────────────────────────────────

@Composable
private fun SrTokenSection(
    token: String,
    shareLink: String?,
    manage: ManageDecision,
    onRotate: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
        Text(text = stringResource(Res.string.music_token_title), style = typography.base, color = tokens.cardForeground)
        // The pretty, say-it-on-stream link (`/sr/@name`) — offered first with a one-click copy when known.
        if (!shareLink.isNullOrBlank()) {
            Text(
                text = stringResource(Res.string.music_share_link_value, shareLink),
                style = typography.sm,
                color = tokens.cardForeground,
                maxLines = 2,
                overflow = TextOverflow.Ellipsis,
            )
            CopyLinkButton(
                url = shareLink,
                copyLabel = stringResource(Res.string.music_share_link_copy),
                copiedLabel = stringResource(Res.string.music_share_link_copied),
            )
        }
        Text(
            text = stringResource(Res.string.music_token_value, token),
            style = typography.sm,
            color = tokens.mutedForeground,
            maxLines = 2,
            overflow = TextOverflow.Ellipsis,
        )
        ManageGate(decision = manage) { enabled ->
            GlyphButton(
                imageVector = RefreshGlyph,
                label = stringResource(Res.string.music_token_rotate),
                onClick = onRotate,
                enabled = enabled,
                tint = tokens.destructive,
            )
        }
    }
}

@Composable
private fun RemoteControlsSection(
    devices: List<MusicDevice>,
    playlists: List<MusicPlaylist>,
    manage: ManageDecision,
    onSetShuffle: (Boolean) -> Unit,
    onSetRepeat: (String) -> Unit,
    onTransfer: (deviceId: String) -> Unit,
    onPlayPlaylist: (uri: String) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
        Text(
            text = stringResource(Res.string.music_remote_title),
            style = typography.sm,
            color = tokens.mutedForeground,
            modifier = Modifier.padding(horizontal = spacing.s1),
        )

        // Shuffle / repeat quick-actions
        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            Button(
                onClick = { if (manage.isAllowed) onSetShuffle(true) },
                enabled = manage.isAllowed,
                modifier = Modifier.weight(1f),
            ) {
                Text(
                    text = stringResource(Res.string.music_shuffle_label),
                    style = typography.sm,
                    color = tokens.secondaryForeground,
                )
            }
            listOf(
                "off" to Res.string.music_repeat_off,
                "track" to Res.string.music_repeat_track,
                "context" to Res.string.music_repeat_context,
            ).forEach { (mode, labelRes) ->
                Button(
                    onClick = { if (manage.isAllowed) onSetRepeat(mode) },
                    enabled = manage.isAllowed,
                ) {
                    Text(
                        text = stringResource(labelRes),
                        style = typography.sm,
                        color = tokens.secondaryForeground,
                    )
                }
            }
        }

        // Devices
        if (devices.isNotEmpty()) {
            Text(
                text = stringResource(Res.string.music_devices_title),
                style = typography.sm,
                color = tokens.mutedForeground,
                modifier = Modifier.padding(horizontal = spacing.s1),
            )
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
                devices.forEach { device ->
                    TextButton(
                        onClick = { if (manage.isAllowed) onTransfer(device.id) },
                        enabled = manage.isAllowed && !device.isActive,
                    ) {
                        Text(
                            text = stringResource(Res.string.music_device_transfer, device.name),
                            style = typography.sm,
                            color = if (device.isActive) tokens.accent else tokens.foreground,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                        )
                    }
                }
            }
        }

        // Playlists
        if (playlists.isNotEmpty()) {
            Text(
                text = stringResource(Res.string.music_playlists_title),
                style = typography.sm,
                color = tokens.mutedForeground,
                modifier = Modifier.padding(horizontal = spacing.s1),
            )
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
                playlists.forEach { playlist ->
                    TextButton(onClick = { if (manage.isAllowed) onPlayPlaylist(playlist.uri) }, enabled = manage.isAllowed) {
                        Text(
                            text = stringResource(Res.string.music_playlist_play, playlist.name),
                            style = typography.sm,
                            color = tokens.foreground,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                        )
                    }
                }
            }
        }
    }
}

// ── Blocked songs ────────────────────────────────────────────────────────────

// The channel's blocked song-request list: a paged table of every banned track (title, provider, URI, reason,
// blocked date) with a per-row unblock (confirmed in the shared dialog) and a small block-a-track form. All
// mutations sit behind the page's single manage gate — a caller below the floor sees the list but every
// unblock/block control renders disabled with the gate's reason tooltip.
@Composable
private fun BlockedTracksSection(
    blockedTracks: List<BlockedTrack>,
    blockedPage: Int,
    blockedTotal: Int,
    blockedHasMore: Boolean,
    manage: ManageDecision,
    onBlockTrack: (provider: String, trackUri: String, title: String, reason: String?) -> Unit,
    onUnblock: (BlockedTrack) -> Unit,
    onPage: (page: Int) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.SpaceBetween,
        ) {
            Text(
                text = stringResource(Res.string.music_blocked_title),
                style = typography.base,
                color = tokens.cardForeground,
            )
            Text(
                text = stringResource(Res.string.music_blocked_count, blockedTotal),
                style = typography.xs,
                color = tokens.mutedForeground,
            )
        }

        if (blockedTracks.isEmpty()) {
            Text(
                text = stringResource(Res.string.music_blocked_empty),
                style = typography.sm,
                color = tokens.mutedForeground,
                modifier = Modifier.padding(horizontal = spacing.s1),
            )
        } else {
            Card(modifier = Modifier.fillMaxWidth()) {
                Column {
                    blockedTracks.forEachIndexed { index, track ->
                        BlockedTrackRow(track = track, manage = manage, onUnblock = { onUnblock(track) })
                        if (index < blockedTracks.lastIndex) {
                            Separator()
                        }
                    }
                }
            }
        }

        // Pager — previous/next only when there is somewhere to go; the total header shows where you are.
        if (blockedPage > 1 || blockedHasMore) {
            Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                TextButton(onClick = { onPage(blockedPage - 1) }, enabled = blockedPage > 1) {
                    Text(text = stringResource(Res.string.music_blocked_prev), maxLines = 1)
                }
                TextButton(onClick = { onPage(blockedPage + 1) }, enabled = blockedHasMore) {
                    Text(text = stringResource(Res.string.music_blocked_next), maxLines = 1)
                }
            }
        }

        BlockTrackForm(manage = manage, onBlock = onBlockTrack)
    }
}

@Composable
private fun BlockedTrackRow(track: BlockedTrack, manage: ManageDecision, onUnblock: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val title: String = track.title.takeIf { it.isNotBlank() } ?: track.trackUri
    val unblockLabel: String = stringResource(Res.string.music_blocked_unblock, title)
    // createdAt is an ISO-8601 instant; the date part is all the row needs.
    val blockedDate: String = track.createdAt.take(10)

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Badge(
            label = stringResource(Res.string.music_provider, track.provider),
            background = tokens.secondary,
            foreground = tokens.secondaryForeground,
        )
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
            Text(
                text = track.trackUri,
                style = typography.xs,
                color = tokens.mutedForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            track.reason?.takeIf { it.isNotBlank() }?.let { reason ->
                Text(
                    text = stringResource(Res.string.music_blocked_reason, reason),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
        }
        if (blockedDate.isNotBlank()) {
            Text(
                text = stringResource(Res.string.music_blocked_date, blockedDate),
                style = typography.xs,
                color = tokens.mutedForeground,
                maxLines = 1,
            )
        }
        ManageGate(decision = manage) { enabled ->
            GlyphButton(
                imageVector = TrashGlyph,
                label = unblockLabel,
                onClick = onUnblock,
                enabled = enabled,
                tint = tokens.destructive,
            )
        }
    }
}

@Composable
private fun BlockTrackForm(
    manage: ManageDecision,
    onBlock: (provider: String, trackUri: String, title: String, reason: String?) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var provider: String by remember { mutableStateOf("spotify") }
    var trackUri: String by remember { mutableStateOf("") }
    var title: String by remember { mutableStateOf("") }
    var reason: String by remember { mutableStateOf("") }
    val canBlock: Boolean = trackUri.isNotBlank() && title.isNotBlank()

    val providerOptions: List<Pair<String, String>> = listOf(
        "spotify" to stringResource(Res.string.music_config_provider_spotify),
        "youtube" to stringResource(Res.string.music_config_provider_youtube),
    )

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
        Text(
            text = stringResource(Res.string.music_block_form_title),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
        Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
            Text(
                text = stringResource(Res.string.music_block_provider),
                style = typography.sm,
                color = tokens.mutedForeground,
            )
            TabsList {
                providerOptions.forEach { (key, label) ->
                    ManageGate(decision = manage) { gateEnabled ->
                        TabsTrigger(
                            selected = provider == key,
                            onClick = { provider = key },
                            enabled = gateEnabled,
                        ) {
                            Text(label, maxLines = 1)
                        }
                    }
                }
            }
        }
        AppTextField(
            value = trackUri,
            onValueChange = { trackUri = it },
            label = stringResource(Res.string.music_block_uri),
            isError = false,
            errorText = null,
            modifier = Modifier.fillMaxWidth(),
        )
        AppTextField(
            value = title,
            onValueChange = { title = it },
            label = stringResource(Res.string.music_block_track_title),
            isError = false,
            errorText = null,
            modifier = Modifier.fillMaxWidth(),
        )
        AppTextField(
            value = reason,
            onValueChange = { reason = it },
            label = stringResource(Res.string.music_block_reason),
            isError = false,
            errorText = null,
            modifier = Modifier.fillMaxWidth(),
        )
        ManageGate(decision = manage) { enabled ->
            Button(
                onClick = {
                    onBlock(provider, trackUri, title, reason.takeIf { it.isNotBlank() })
                    trackUri = ""
                    title = ""
                    reason = ""
                },
                enabled = enabled && canBlock,
            ) {
                Text(text = stringResource(Res.string.music_block_action))
            }
        }
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
