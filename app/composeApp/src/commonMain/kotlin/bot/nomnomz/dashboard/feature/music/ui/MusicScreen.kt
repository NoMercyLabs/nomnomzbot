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
import bot.nomnomz.dashboard.core.designsystem.component.Badge
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
import androidx.compose.ui.layout.ContentScale
import coil3.compose.AsyncImage
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.designsystem.component.InlineError
import bot.nomnomz.dashboard.core.network.MusicDevice
import bot.nomnomz.dashboard.core.network.MusicPlaylist
import bot.nomnomz.dashboard.core.network.NowPlaying
import bot.nomnomz.dashboard.feature.music.state.MusicController
import bot.nomnomz.dashboard.feature.music.state.MusicState
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.music_spotify_needs_reauth
import nomnomzbot.composeapp.generated.resources.music_spotify_reconnect
import nomnomzbot.composeapp.generated.resources.music_art_placeholder
import nomnomzbot.composeapp.generated.resources.music_empty
import nomnomzbot.composeapp.generated.resources.music_error
import nomnomzbot.composeapp.generated.resources.music_loading
import nomnomzbot.composeapp.generated.resources.music_now_playing_description
import nomnomzbot.composeapp.generated.resources.music_now_playing_label
import nomnomzbot.composeapp.generated.resources.music_pause
import nomnomzbot.composeapp.generated.resources.music_play
import nomnomzbot.composeapp.generated.resources.music_progress
import nomnomzbot.composeapp.generated.resources.music_provider
import nomnomzbot.composeapp.generated.resources.music_seek_back
import nomnomzbot.composeapp.generated.resources.music_seek_back_description
import nomnomzbot.composeapp.generated.resources.music_seek_forward
import nomnomzbot.composeapp.generated.resources.music_seek_forward_description
import nomnomzbot.composeapp.generated.resources.music_playing_from_provider
import nomnomzbot.composeapp.generated.resources.music_requested_by
import nomnomzbot.composeapp.generated.resources.music_unknown_provider
import nomnomzbot.composeapp.generated.resources.music_retry
import nomnomzbot.composeapp.generated.resources.music_skip
import nomnomzbot.composeapp.generated.resources.music_unknown_track
import nomnomzbot.composeapp.generated.resources.music_manage_song_requests
import nomnomzbot.composeapp.generated.resources.music_remote_title
import nomnomzbot.composeapp.generated.resources.music_shuffle_label
import nomnomzbot.composeapp.generated.resources.music_repeat_off
import nomnomzbot.composeapp.generated.resources.music_repeat_track
import nomnomzbot.composeapp.generated.resources.music_repeat_context
import nomnomzbot.composeapp.generated.resources.music_devices_title
import nomnomzbot.composeapp.generated.resources.music_device_transfer
import nomnomzbot.composeapp.generated.resources.music_playlists_title
import nomnomzbot.composeapp.generated.resources.music_playlist_play
import nomnomzbot.composeapp.generated.resources.music_blocked_by_provider
import nomnomzbot.composeapp.generated.resources.shell_nav_music
import bot.nomnomz.dashboard.core.realtime.HubEvent
import kotlinx.coroutines.flow.SharedFlow
import org.jetbrains.compose.resources.stringResource

// The Music page: the channel's live playback/transport, made controllable — every track is real data from
// [MusicController] (the backend sources the now-playing snapshot from the connected music provider). The
// screen is a pure projection of the controller's state; it loads on first composition and offers a retry on
// failure. The now-playing card shows the current track with its progress and provider, and a control row
// drives playback (Play/Pause toggles on the live isPlaying flag; Skip advances) plus the Spotify-specific
// remote controls (shuffle/repeat/devices/playlists). The upcoming request queue, its config, and its share
// link are owned by the Song Requests page — this screen links over to it instead of re-rendering the same
// queue under a second set of controls (S-OBS-04).
@Composable
fun MusicScreen(
    controller: MusicController,
    role: ManagementRole?,
    hubEvents: SharedFlow<HubEvent>? = null,
    // S003b — where the reconnect notice's action sends the streamer; defaults to a no-op so screens/tests
    // that don't wire shell navigation still compile.
    onNavigateToIntegrations: () -> Unit = {},
    // The request queue, its SR config, and its share link are owned by the Song Requests page — this link
    // sends the streamer there instead of re-rendering a second copy of the same queue here.
    onNavigateToSongRequests: () -> Unit = {},
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

    Box(modifier = Modifier.fillMaxSize()) {
        when (val current: MusicState = state) {
            is MusicState.Loading -> CenteredMessage(stringResource(Res.string.music_loading))
            is MusicState.Empty -> CenteredMessage(stringResource(Res.string.music_empty))
            is MusicState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is MusicState.Ready ->
                ReadyContent(
                    nowPlaying = current.nowPlaying,
                    devices = current.devices,
                    playlists = current.playlists,
                    manage = manage,
                    onPlay = { scope.launch { controller.resume() } },
                    onPause = { scope.launch { controller.pause() } },
                    onSeek = { positionMs -> scope.launch { controller.seek(positionMs) } },
                    onSkip = { scope.launch { controller.skip() } },
                    onTrackEndReached = { scope.launch { controller.onPredictedTrackEndReached() } },
                    onSetShuffle = { enabled -> scope.launch { controller.setShuffle(enabled) } },
                    onSetRepeat = { mode -> scope.launch { controller.setRepeat(mode) } },
                    onTransfer = { deviceId -> scope.launch { controller.transferPlayback(deviceId, play = true) } },
                    onPlayPlaylist = { uri -> scope.launch { controller.playContext(uri) } },
                    spotifyNeedsReauth = current.spotifyNeedsReauth,
                    onReconnectSpotify = onNavigateToIntegrations,
                    onNavigateToSongRequests = onNavigateToSongRequests,
                )
        }
    }
}

@Composable
private fun ReadyContent(
    nowPlaying: NowPlaying?,
    devices: List<MusicDevice>,
    playlists: List<MusicPlaylist>,
    manage: ManageDecision,
    onPlay: () -> Unit,
    onPause: () -> Unit,
    onSeek: (positionMs: Int) -> Unit,
    onSkip: () -> Unit,
    onTrackEndReached: () -> Unit,
    onSetShuffle: (Boolean) -> Unit,
    onSetRepeat: (String) -> Unit,
    onTransfer: (deviceId: String) -> Unit,
    onPlayPlaylist: (uri: String) -> Unit,
    spotifyNeedsReauth: Boolean = false,
    onReconnectSpotify: () -> Unit = {},
    onNavigateToSongRequests: () -> Unit = {},
) {
    val spacing = LocalSpacing.current

    Column(
        modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(spacing.s6),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        PageHeader(title = stringResource(Res.string.shell_nav_music))
        if (spotifyNeedsReauth) {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
                InlineError(message = stringResource(Res.string.music_spotify_needs_reauth))
                Button(onClick = onReconnectSpotify) {
                    Text(stringResource(Res.string.music_spotify_reconnect), maxLines = 1)
                }
            }
        }
        if (nowPlaying != null) {
            NowPlayingCard(
                nowPlaying = nowPlaying,
                manage = manage,
                onPlay = onPlay,
                onPause = onPause,
                onSeek = onSeek,
                onSkip = onSkip,
                onTrackEndReached = onTrackEndReached,
            )
        }

        // The request queue (add/remove/promote/ban), its config, and its share link are owned by the Song
        // Requests page — this is a link over to that page, not a second projection of the same queue.
        TextButton(onClick = onNavigateToSongRequests) {
            Text(text = stringResource(Res.string.music_manage_song_requests))
        }

        // ── Remote controls (shuffle / repeat / devices / playlists) ──────
        if (devices.isNotEmpty() || playlists.isNotEmpty()) {
            Separator()
            RemoteControlsSection(
                devices = devices,
                playlists = playlists,
                manage = manage,
                shuffleOn = nowPlaying?.shuffleState ?: false,
                repeatMode = nowPlaying?.repeatState ?: "off",
                canSetShuffle = nowPlaying?.canSetShuffle ?: true,
                canSetRepeat = nowPlaying?.canSetRepeat ?: true,
                provider = nowPlaying?.provider,
                onSetShuffle = onSetShuffle,
                onSetRepeat = onSetRepeat,
                onTransfer = onTransfer,
                onPlayPlaylist = onPlayPlaylist,
            )
        }
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
    onTrackEndReached: () -> Unit = {},
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val title: String =
        nowPlaying.trackName?.takeIf { it.isNotBlank() }
            ?: stringResource(Res.string.music_unknown_track)
    val artist: String = nowPlaying.artist.orEmpty()
    val album: String = nowPlaying.album.orEmpty()
    // A track with no requester was NOT requested by anyone — it is the provider's own playback (Spotify
    // autoplay, a playlist rolling on, YouTube's next video). Rendering that as "Requested by someone"
    // invented a viewer who does not exist and made a provider-picked track look like an unattributed
    // request. Say which it is instead.
    val requesterOrNull: String? = nowPlaying.requestedBy?.takeIf { it.isNotBlank() }
    val providerLabel: String = nowPlaying.provider.takeIf { it.isNotBlank() }?.replaceFirstChar {
        it.uppercase()
    } ?: stringResource(Res.string.music_unknown_provider)
    val attribution: String =
        requesterOrNull?.let { stringResource(Res.string.music_requested_by, it) }
            ?: stringResource(Res.string.music_playing_from_provider, providerLabel)
    val cardDescription: String =
        stringResource(Res.string.music_now_playing_description, title, artist.ifBlank { attribution })

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
        // The ticker reached the track's predicted natural end. subscribeToHub() is the only other source of
        // updates, and a hub push can be missed silently (reconnect gap, dropped frame) — without this, a missed
        // push leaves the card frozen at 100% forever. onTrackEndReached() re-checks the backend directly.
        if (nowPlaying.durationMs > 0) onTrackEndReached()
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
            AlbumArt(title = title, imageUrl = nowPlaying.imageUrl)
            Column(
                modifier = Modifier.weight(1f),
                verticalArrangement = Arrangement.spacedBy(spacing.s1),
            ) {
                Text(
                    text = title,
                    style = typography.base,
                    color = tokens.cardForeground,
                    maxLines = 2,
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
                text = attribution,
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
            canSeek = nowPlaying.canSeek,
            canPause = nowPlaying.canPause,
            canResume = nowPlaying.canResume,
            canSkipNext = nowPlaying.canSkipNext,
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
    canSeek: Boolean,
    canPause: Boolean,
    canResume: Boolean,
    canSkipNext: Boolean,
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
            providerAllowed = canSeek,
            onClick = { onSeek((progressMs - 10_000).coerceAtLeast(0)) },
        )
        // Play and pause are the same backend toggle; the live isPlaying flag decides which one is offered.
        if (isPlaying) {
            ControlButton(
                label = stringResource(Res.string.music_pause),
                manage = manage,
                providerAllowed = canPause,
                onClick = onPause,
            )
        } else {
            ControlButton(
                label = stringResource(Res.string.music_play),
                manage = manage,
                providerAllowed = canResume,
                onClick = onPlay,
            )
        }
        ControlButton(
            label = stringResource(Res.string.music_seek_forward),
            description = stringResource(Res.string.music_seek_forward_description),
            manage = manage,
            providerAllowed = canSeek,
            onClick = { onSeek((progressMs + 10_000).coerceAtMost(durationMs)) },
        )
        ControlButton(
            label = stringResource(Res.string.music_skip),
            manage = manage,
            providerAllowed = canSkipNext,
            onClick = onSkip,
        )
    }
}

@Composable
private fun ControlButton(
    label: String,
    manage: ManageDecision,
    onClick: () -> Unit,
    description: String = label,
    // False when the PROVIDER (not the caller's role) currently blocks this action — an ad break, a
    // restricted market, a non-Premium account. Independent of the role gate: a fully-permitted Editor
    // still can't skip while Spotify itself refuses it, so this ANDs into the same disabled affordance
    // rather than a second gate the operator has to reason about separately.
    providerAllowed: Boolean = true,
) {
    val tokens = LocalTokens.current

    ManageGate(decision = manage) { manageEnabled ->
        val enabled: Boolean = manageEnabled && providerAllowed
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

// The album art slot: the real cover art via Coil when the now-playing snapshot carries a URL (Spotify
// always does for a track with artwork), falling back to the track's initial as a placeholder tile (the
// same approach the profile Avatar uses for an unrendered image URL) when it doesn't.
@Composable
private fun AlbumArt(title: String, imageUrl: String?) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val description: String = stringResource(Res.string.music_art_placeholder)

    Box(
        modifier = Modifier
            .size(spacing.s12)
            .clip(RoundedCornerShape(tokens.radius.md))
            .background(tokens.muted)
            .clearAndSetSemantics { contentDescription = description },
        contentAlignment = Alignment.Center,
    ) {
        if (!imageUrl.isNullOrBlank()) {
            AsyncImage(
                model = imageUrl,
                contentDescription = description,
                modifier = Modifier.fillMaxSize(),
                contentScale = ContentScale.Crop,
            )
        } else {
            val initial: String = title.trim().firstOrNull()?.uppercase() ?: "?"
            Text(text = initial, style = typography.base, color = tokens.mutedForeground)
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
private fun RemoteControlsSection(
    devices: List<MusicDevice>,
    playlists: List<MusicPlaylist>,
    manage: ManageDecision,
    shuffleOn: Boolean,
    repeatMode: String,
    canSetShuffle: Boolean,
    canSetRepeat: Boolean,
    provider: String?,
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

        // Shuffle toggle — reflects the live player state and FLIPS it (not the old one-way "always on"):
        // the Switch shows whether shuffle is on and a tap sends the opposite. control() reloads on success,
        // so the Switch settles on the real post-write state. `canSetShuffle` is the PROVIDER's own live
        // restriction (an ad break, a restricted market) — independent of the role gate, so it ANDs in too.
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(spacing.s2),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(
                text = stringResource(Res.string.music_shuffle_label),
                style = typography.sm,
                color = tokens.cardForeground,
                modifier = Modifier.weight(1f),
            )
            Switch(
                checked = shuffleOn,
                onCheckedChange = { enabled -> if (manage.isAllowed && canSetShuffle) onSetShuffle(enabled) },
                enabled = manage.isAllowed && canSetShuffle,
            )
        }
        // Repeat mode — the ACTIVE mode is highlighted (off | track | context), so the operator can see and
        // change what the player is doing instead of firing blind buttons.
        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            listOf(
                "off" to Res.string.music_repeat_off,
                "track" to Res.string.music_repeat_track,
                "context" to Res.string.music_repeat_context,
            ).forEach { (mode, labelRes) ->
                Badge(
                    selected = repeatMode == mode,
                    enabled = manage.isAllowed && canSetRepeat,
                    onClick = { onSetRepeat(mode) },
                ) {
                    Text(text = stringResource(labelRes), maxLines = 1)
                }
            }
        }
        if (provider != null && (!canSetShuffle || !canSetRepeat)) {
            Text(
                text = stringResource(Res.string.music_blocked_by_provider, provider),
                style = typography.xs,
                color = tokens.mutedForeground,
                modifier = Modifier.padding(horizontal = spacing.s1),
            )
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
                            maxLines = 2,
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
                            maxLines = 2,
                            overflow = TextOverflow.Ellipsis,
                        )
                    }
                }
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
