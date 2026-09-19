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
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.material3.Text
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.TabsList
import bot.nomnomz.dashboard.core.designsystem.component.TabsTrigger
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.component.Button
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
import bot.nomnomz.dashboard.core.designsystem.component.GlyphButton
import bot.nomnomz.dashboard.core.designsystem.component.CopyLinkButton
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Slider
import bot.nomnomz.dashboard.core.designsystem.component.Switch
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.designsystem.icon.ArrowUpGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.RefreshGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.RemoveGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.TrashGlyph
import bot.nomnomz.dashboard.core.network.BlockedTrack
import bot.nomnomz.dashboard.core.network.MusicConfig
import bot.nomnomz.dashboard.core.network.QueuedSong
import bot.nomnomz.dashboard.core.network.UpdateMusicConfigBody
import bot.nomnomz.dashboard.core.realtime.HubEvent
import bot.nomnomz.dashboard.feature.songrequests.state.SongRequestsController
import bot.nomnomz.dashboard.feature.songrequests.state.SongRequestsState
import bot.nomnomz.dashboard.feature.shell.nav.ManageAction
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.launch
import kotlin.math.roundToInt
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.shell_nav_song_requests
import nomnomzbot.composeapp.generated.resources.songrequests_action_error
import nomnomzbot.composeapp.generated.resources.songrequests_ban_action
import nomnomzbot.composeapp.generated.resources.songrequests_ban_confirm
import nomnomzbot.composeapp.generated.resources.songrequests_ban_dismiss
import nomnomzbot.composeapp.generated.resources.songrequests_ban_message
import nomnomzbot.composeapp.generated.resources.songrequests_ban_title
import nomnomzbot.composeapp.generated.resources.songrequests_paid_badge
import nomnomzbot.composeapp.generated.resources.songrequests_promote_action
import nomnomzbot.composeapp.generated.resources.songrequests_config_allow_spotify
import nomnomzbot.composeapp.generated.resources.songrequests_config_allow_youtube
import nomnomzbot.composeapp.generated.resources.songrequests_config_enabled
import nomnomzbot.composeapp.generated.resources.songrequests_config_max_per_user
import nomnomzbot.composeapp.generated.resources.songrequests_config_max_queue
import nomnomzbot.composeapp.generated.resources.songrequests_config_provider
import nomnomzbot.composeapp.generated.resources.songrequests_config_provider_auto
import nomnomzbot.composeapp.generated.resources.songrequests_config_provider_spotify
import nomnomzbot.composeapp.generated.resources.songrequests_config_provider_youtube
import nomnomzbot.composeapp.generated.resources.songrequests_config_save
import nomnomzbot.composeapp.generated.resources.songrequests_config_title
import nomnomzbot.composeapp.generated.resources.songrequests_config_trust
import nomnomzbot.composeapp.generated.resources.songrequests_empty
import nomnomzbot.composeapp.generated.resources.songrequests_error
import nomnomzbot.composeapp.generated.resources.songrequests_loading
import nomnomzbot.composeapp.generated.resources.songrequests_pause
import nomnomzbot.composeapp.generated.resources.songrequests_position
import nomnomzbot.composeapp.generated.resources.songrequests_queue_title
import nomnomzbot.composeapp.generated.resources.songrequests_remove_action
import nomnomzbot.composeapp.generated.resources.songrequests_remove_confirm
import nomnomzbot.composeapp.generated.resources.songrequests_remove_dismiss
import nomnomzbot.composeapp.generated.resources.songrequests_remove_message
import nomnomzbot.composeapp.generated.resources.songrequests_remove_title
import nomnomzbot.composeapp.generated.resources.songrequests_requested_by
import nomnomzbot.composeapp.generated.resources.songrequests_resume
import nomnomzbot.composeapp.generated.resources.songrequests_retry
import nomnomzbot.composeapp.generated.resources.songrequests_row_description
import nomnomzbot.composeapp.generated.resources.songrequests_skip
import nomnomzbot.composeapp.generated.resources.songrequests_token_rotate
import nomnomzbot.composeapp.generated.resources.songrequests_token_rotate_confirm
import nomnomzbot.composeapp.generated.resources.songrequests_token_rotate_dismiss
import nomnomzbot.composeapp.generated.resources.songrequests_token_rotate_message
import nomnomzbot.composeapp.generated.resources.songrequests_token_rotate_title
import nomnomzbot.composeapp.generated.resources.songrequests_token_title
import nomnomzbot.composeapp.generated.resources.songrequests_unknown_requester
import nomnomzbot.composeapp.generated.resources.music_add_title
import nomnomzbot.composeapp.generated.resources.music_add_query
import nomnomzbot.composeapp.generated.resources.music_add_requested_by
import nomnomzbot.composeapp.generated.resources.music_add_action
import nomnomzbot.composeapp.generated.resources.music_share_link_copied
import nomnomzbot.composeapp.generated.resources.music_share_link_copy
import nomnomzbot.composeapp.generated.resources.music_share_link_value
import nomnomzbot.composeapp.generated.resources.music_token_value
import nomnomzbot.composeapp.generated.resources.music_token_link_value
import nomnomzbot.composeapp.generated.resources.music_token_link_copy
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
import nomnomzbot.composeapp.generated.resources.music_block_provider_spotify
import nomnomzbot.composeapp.generated.resources.music_block_provider_youtube
import nomnomzbot.composeapp.generated.resources.music_block_uri
import nomnomzbot.composeapp.generated.resources.music_block_track_title
import nomnomzbot.composeapp.generated.resources.music_block_reason
import nomnomzbot.composeapp.generated.resources.music_block_action
import nomnomzbot.composeapp.generated.resources.music_provider
import org.jetbrains.compose.resources.stringResource

// The Song Requests page: the channel's live music queue + SR management (config, SR-page token). Loads all
// three in parallel; the queue + controls render immediately. The config section surfaces the key SR toggles
// (enabled, providers) with inline saves. The SR-page token section shows the shareable link + rotate action.

// Real backend bounds (`UpdateMusicConfigDto` [Range] attributes, server/src/NomNomzBot.Application/Music/
// Dtos/MusicConfigDtos.cs) — never invented client-side. A value outside these is rejected server-side, so
// the stepper below cannot produce a value the backend would refuse.
internal const val MIN_MAX_QUEUE_SIZE: Int = 1
internal const val MAX_MAX_QUEUE_SIZE: Int = 500
internal const val MIN_MAX_REQUESTS_PER_USER: Int = 1
internal const val MAX_MAX_REQUESTS_PER_USER: Int = 50

/** Clamp a candidate `MaxQueueSize` to the backend-enforced `[1, 500]` range. */
internal fun clampMaxQueueSize(value: Int): Int = value.coerceIn(MIN_MAX_QUEUE_SIZE, MAX_MAX_QUEUE_SIZE)

/** Clamp a candidate `MaxRequestsPerUser` to the backend-enforced `[1, 50]` range. */
internal fun clampMaxRequestsPerUser(value: Int): Int =
    value.coerceIn(MIN_MAX_REQUESTS_PER_USER, MAX_MAX_REQUESTS_PER_USER)

@Composable
fun SongRequestsScreen(
    controller: SongRequestsController,
    role: ManagementRole?,
    hubEvents: SharedFlow<HubEvent>? = null,
) {
    val state: SongRequestsState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    val moderate: ManageDecision =
        rememberManageDecision(role, ShellRoute.SongRequests, ManageAction.SongQueueModeration)
    val configure: ManageDecision =
        rememberManageDecision(role, ShellRoute.SongRequests, ManageAction.MusicConfig)

    LaunchedEffect(Unit) { controller.load() }
    if (hubEvents != null) {
        LaunchedEffect(hubEvents) { controller.subscribeToHub(hubEvents) }
    }

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
                    shareLink = current.shareLink,
                    tokenUrl = current.tokenUrl,
                    blockedTracks = current.blockedTracks,
                    blockedPage = current.blockedPage,
                    blockedTotal = current.blockedTotal,
                    blockedHasMore = current.blockedHasMore,
                    moderate = moderate,
                    configure = configure,
                    onSkip = { scope.launch { controller.skip() } },
                    onPause = { scope.launch { controller.pause() } },
                    onResume = { scope.launch { controller.resume() } },
                    onRemove = { position -> scope.launch { controller.remove(position) } },
                    onPromote = { position -> scope.launch { controller.promote(position) } },
                    onBan = { position -> scope.launch { controller.ban(position) } },
                    onAddToQueue = { query, requestedBy -> scope.launch { controller.addToQueue(query, requestedBy) } },
                    onUpdateConfig = { body -> scope.launch { controller.updateConfig(body) } },
                    onRotateToken = { scope.launch { controller.rotateSrPageToken() } },
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
    queue: List<QueuedSong>,
    config: MusicConfig?,
    srPageToken: String?,
    shareLink: String?,
    tokenUrl: String?,
    blockedTracks: List<BlockedTrack>,
    blockedPage: Int,
    blockedTotal: Int,
    blockedHasMore: Boolean,
    moderate: ManageDecision,
    configure: ManageDecision,
    onSkip: () -> Unit,
    onPause: () -> Unit,
    onResume: () -> Unit,
    onRemove: (position: Int) -> Unit,
    onPromote: (position: Int) -> Unit,
    onBan: (position: Int) -> Unit,
    onAddToQueue: (query: String, requestedBy: String) -> Unit,
    onUpdateConfig: (UpdateMusicConfigBody) -> Unit,
    onRotateToken: () -> Unit,
    onBlockTrack: (provider: String, trackUri: String, title: String, reason: String?) -> Unit,
    onUnblockTrack: (blockedTrackId: String) -> Unit,
    onBlockedPage: (page: Int) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var pendingRemoval: QueuedSong? by remember { mutableStateOf(null) }
    var pendingBan: QueuedSong? by remember { mutableStateOf(null) }
    var pendingUnblock: BlockedTrack? by remember { mutableStateOf(null) }
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

        // Control failures announce on the shell-level feedback toast (SongRequestsController.surfaceError).

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
            item {
                Card(modifier = Modifier.fillMaxWidth()) {
                    Column {
                        queue.forEachIndexed { index, song ->
                            QueueRow(
                                song = song,
                                moderate = moderate,
                                onRemove = { pendingRemoval = song },
                                onPromote = { onPromote(song.position) },
                                onBan = { pendingBan = song },
                            )
                            if (index < queue.lastIndex) {
                                Separator()
                            }
                        }
                    }
                }
            }
        }

        // ── Add to queue (a manual/DJ addition — the same queue as viewer requests) ─────────────────────────
        item {
            Separator()
            AddToQueueSection(moderate = moderate, onAdd = onAddToQueue)
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

        // ── SR page token + shareable link ───────────────────────────────────
        if (srPageToken != null) {
            item {
                SrTokenSection(
                    token = srPageToken,
                    shareLink = shareLink,
                    tokenUrl = tokenUrl,
                    configure = configure,
                    onRotate = { showRotateConfirm = true },
                )
            }
        }

        // ── Blocked songs (the legacy `!bansong` list) ───────────────────────
        item {
            Separator()
            BlockedTracksSection(
                blockedTracks = blockedTracks,
                blockedPage = blockedPage,
                blockedTotal = blockedTotal,
                blockedHasMore = blockedHasMore,
                moderate = moderate,
                onBlockTrack = onBlockTrack,
                onUnblock = { track -> pendingUnblock = track },
                onPage = onBlockedPage,
            )
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

    // Ban confirmation
    pendingBan?.let { song ->
        val title: String = song.trackName.takeIf { it.isNotBlank() } ?: song.artist
        ConfirmDialog(
            title = stringResource(Res.string.songrequests_ban_title),
            message = stringResource(Res.string.songrequests_ban_message, title),
            confirmLabel = stringResource(Res.string.songrequests_ban_confirm),
            dismissLabel = stringResource(Res.string.songrequests_ban_dismiss),
            destructive = true,
            onConfirm = {
                onBan(song.position)
                pendingBan = null
            },
            onDismiss = { pendingBan = null },
        )
    }

    // Unblock confirmation
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

    // Local draft state for the fields that batch into one explicit Save (provider/queue-size/per-user/trust)
    // — matches the toggles' semantics: PATCH sends only the fields the operator actually touched, so a stale
    // draft can never clobber a setting nobody meant to change. `remember(config)` re-seeds the draft whenever
    // a fresh config lands (e.g. after Save reloads, or another session's edit arrives over the hub).
    var preferredProvider: String by remember(config) { mutableStateOf(config.preferredProvider) }
    var maxQueueSize: Int by remember(config) { mutableStateOf(clampMaxQueueSize(config.maxQueueSize)) }
    var maxPerUser: Int by remember(config) { mutableStateOf(clampMaxRequestsPerUser(config.maxRequestsPerUser)) }
    var minTrustLevel: String by remember(config) { mutableStateOf(config.minTrustLevel) }

    val trustLevels: List<String> = listOf("everyone", "subscribers", "vip", "moderators", "broadcaster")
    val providerOptions: List<Pair<String, String>> = listOf(
        "auto" to stringResource(Res.string.songrequests_config_provider_auto),
        "spotify" to stringResource(Res.string.songrequests_config_provider_spotify),
        "youtube" to stringResource(Res.string.songrequests_config_provider_youtube),
    )

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier
                .fillMaxWidth()
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

            // Preferred provider
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
                Text(
                    text = stringResource(Res.string.songrequests_config_provider),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
                TabsList {
                    providerOptions.forEach { (key, label) ->
                        ManageGate(decision = configure) { enabled ->
                            TabsTrigger(
                                selected = preferredProvider == key,
                                onClick = { preferredProvider = key },
                                enabled = enabled,
                            ) {
                                Text(label, maxLines = 1)
                            }
                        }
                    }
                }
            }

            // Max queue / per-user fields — bounded steppers over the real backend range (`[Range]` on
            // `UpdateMusicConfigDto`), so the operator can never drag past a value the server would reject.
            BoundedIntStepper(
                label = stringResource(Res.string.songrequests_config_max_queue),
                value = maxQueueSize,
                onValueChange = { maxQueueSize = clampMaxQueueSize(it) },
                range = MIN_MAX_QUEUE_SIZE..MAX_MAX_QUEUE_SIZE,
            )
            BoundedIntStepper(
                label = stringResource(Res.string.songrequests_config_max_per_user),
                value = maxPerUser,
                onValueChange = { maxPerUser = clampMaxRequestsPerUser(it) },
                range = MIN_MAX_REQUESTS_PER_USER..MAX_MAX_REQUESTS_PER_USER,
            )

            // Minimum trust level
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
                Text(
                    text = stringResource(Res.string.songrequests_config_trust),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
                TabsList {
                    trustLevels.forEach { level ->
                        ManageGate(decision = configure) { enabled ->
                            TabsTrigger(
                                selected = minTrustLevel == level,
                                onClick = { minTrustLevel = level },
                                enabled = enabled,
                            ) {
                                Text(level, maxLines = 1)
                            }
                        }
                    }
                }
            }

            ManageGate(decision = configure) { enabled ->
                TextButton(
                    onClick = {
                        onUpdate(
                            UpdateMusicConfigBody(
                                preferredProvider = preferredProvider,
                                maxQueueSize = maxQueueSize,
                                maxRequestsPerUser = maxPerUser,
                                minTrustLevel = minTrustLevel,
                            )
                        )
                    },
                    enabled = enabled,
                ) {
                    Text(text = stringResource(Res.string.songrequests_config_save), color = tokens.primary)
                }
            }
        }
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
            )
        }
    }
}

/**
 * A bounded integer stepper: a label + current-value readout above a [Slider] clamped to [range]. Mirrors
 * the numeric-field pattern already used for widget settings (`WidgetSettingsForms.kt`'s `"number"` field
 * type) — a Slider over an integer range rather than a free-text field, so the value can never leave the
 * bound the backend enforces.
 */
@Composable
private fun BoundedIntStepper(
    label: String,
    value: Int,
    onValueChange: (Int) -> Unit,
    range: IntRange,
) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
        Text(
            text = "$label: $value",
            style = typography.sm,
            color = tokens.mutedForeground,
        )
        Slider(
            value = value.toFloat(),
            onValueChange = { onValueChange(it.roundToInt()) },
            valueRange = range.first.toFloat()..range.last.toFloat(),
            steps = (range.last - range.first - 1).coerceAtLeast(0),
            modifier = Modifier.fillMaxWidth(),
        )
    }
}

// The channel's SR-page shareable link: the pretty, say-it-on-stream link (`/sr/@name`) offered first with a
// one-click copy when known, the literal token-backed link (`/sr/{token}`) always resolvable once the backend
// origin is known, and the bare token as a last-resort fallback. Ported from the Music page (S-OBS-04) — this
// is now the ONE place the SR-page link is shown; Music links over here instead of re-rendering it.
@Composable
private fun SrTokenSection(
    token: String,
    shareLink: String?,
    tokenUrl: String?,
    configure: ManageDecision,
    onRotate: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .padding(spacing.s4),
            verticalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            Text(
                text = stringResource(Res.string.songrequests_token_title),
                style = typography.base.copy(fontWeight = FontWeight.SemiBold),
                color = tokens.cardForeground,
            )
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
            if (!tokenUrl.isNullOrBlank()) {
                Text(
                    text = stringResource(Res.string.music_token_link_value, tokenUrl),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                    maxLines = 2,
                    overflow = TextOverflow.Ellipsis,
                )
                CopyLinkButton(
                    url = tokenUrl,
                    copyLabel = stringResource(Res.string.music_token_link_copy),
                    copiedLabel = stringResource(Res.string.music_share_link_copied),
                )
            } else {
                Text(
                    text = stringResource(Res.string.music_token_value, token),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                    maxLines = 2,
                    overflow = TextOverflow.Ellipsis,
                )
            }
            ManageGate(decision = configure) { enabled ->
                GlyphButton(
                    icon = RefreshGlyph,
                    label = stringResource(Res.string.songrequests_token_rotate),
                    onClick = onRotate,
                    enabled = enabled,
                    tint = tokens.destructive,
                )
            }
        }
    }
}

// ── Add to queue (a manual/DJ addition — moved from the Music page, S-OBS-04) ────────────────────────────

@Composable
private fun AddToQueueSection(moderate: ManageDecision, onAdd: (query: String, requestedBy: String) -> Unit) {
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
        ManageGate(decision = moderate) { enabled ->
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

// ── Blocked songs (the legacy `!bansong` list — moved from the Music page, S-OBS-04) ───────────────────────

// The channel's blocked song-request list: a paged table of every banned track (title, provider, URI, reason,
// blocked date) with a per-row unblock (confirmed in the shared dialog) and a small block-a-track form. All
// mutations sit behind the same moderation gate as the rest of the queue (ban/promote/remove) — a caller
// below the floor sees the list but every unblock/block control renders disabled with the gate's tooltip.
@Composable
private fun BlockedTracksSection(
    blockedTracks: List<BlockedTrack>,
    blockedPage: Int,
    blockedTotal: Int,
    blockedHasMore: Boolean,
    moderate: ManageDecision,
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
                        BlockedTrackRow(track = track, moderate = moderate, onUnblock = { onUnblock(track) })
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

        BlockTrackForm(moderate = moderate, onBlock = onBlockTrack)
    }
}

@Composable
private fun BlockedTrackRow(track: BlockedTrack, moderate: ManageDecision, onUnblock: () -> Unit) {
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
                maxLines = 2,
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
        ManageGate(decision = moderate) { enabled ->
            GlyphButton(
                icon = TrashGlyph,
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
    moderate: ManageDecision,
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
        "spotify" to stringResource(Res.string.music_block_provider_spotify),
        "youtube" to stringResource(Res.string.music_block_provider_youtube),
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
                    ManageGate(decision = moderate) { gateEnabled ->
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
        ManageGate(decision = moderate) { enabled ->
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
private fun QueueRow(
    song: QueuedSong,
    moderate: ManageDecision,
    onRemove: () -> Unit,
    onPromote: () -> Unit,
    onBan: () -> Unit,
) {
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
    val promoteLabel: String = stringResource(Res.string.songrequests_promote_action, title)
    val banLabel: String = stringResource(Res.string.songrequests_ban_action, title)
    val paidBadgeLabel: String = stringResource(Res.string.songrequests_paid_badge)

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
        if (song.cost > 0) {
            Badge(
                label = paidBadgeLabel,
                background = tokens.primary,
                foreground = tokens.primaryForeground,
            )
        }
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
                maxLines = 2,
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
            GlyphButton(
                icon = ArrowUpGlyph,
                label = promoteLabel,
                onClick = onPromote,
                enabled = enabled,
                tint = tokens.primary,
            )
        }
        ManageGate(decision = moderate) { enabled ->
            GlyphButton(
                icon = RemoveGlyph,
                label = banLabel,
                onClick = onBan,
                enabled = enabled,
                tint = tokens.destructive,
            )
        }
        ManageGate(decision = moderate) { enabled ->
            GlyphButton(
                icon = TrashGlyph,
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
