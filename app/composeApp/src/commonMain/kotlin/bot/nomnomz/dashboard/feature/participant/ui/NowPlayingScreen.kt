// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.participant.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.OutlinedTextField
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
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.MusicTrack
import bot.nomnomz.dashboard.core.network.NowPlaying
import bot.nomnomz.dashboard.feature.participant.state.NowPlayingState
import bot.nomnomz.dashboard.feature.participant.state.ParticipantController
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.participant_loading
import nomnomzbot.composeapp.generated.resources.participant_sr_limit
import nomnomzbot.composeapp.generated.resources.participant_sr_now_playing
import nomnomzbot.composeapp.generated.resources.participant_sr_nothing_playing
import nomnomzbot.composeapp.generated.resources.participant_sr_queue_empty
import nomnomzbot.composeapp.generated.resources.participant_sr_queue_title
import nomnomzbot.composeapp.generated.resources.participant_sr_requested_by
import nomnomzbot.composeapp.generated.resources.participant_sr_submit
import nomnomzbot.composeapp.generated.resources.participant_sr_submit_placeholder
import nomnomzbot.composeapp.generated.resources.participant_sr_sub_lane
import nomnomzbot.composeapp.generated.resources.participant_sr_unknown_requester
import nomnomzbot.composeapp.generated.resources.participant_sr_unknown_track
import org.jetbrains.compose.resources.stringResource

// Now Playing / Queue: the live now-playing track and the upcoming queue (read), plus the caller's own
// self-service song-request submission (music:request:submit — every standing). A sub/VIP sees the sub-only lane
// marker and a higher pending-request allowance, surfaced from their community standing.
@Composable
fun NowPlayingScreen(controller: ParticipantController) {
    val state: NowPlayingState by controller.nowPlaying.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    LaunchedEffect(Unit) { controller.loadNowPlaying() }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: NowPlayingState = state) {
            is NowPlayingState.Loading -> ParticipantMessage(stringResource(Res.string.participant_loading))
            is NowPlayingState.Error ->
                ParticipantError(detail = current.detail, onRetry = { scope.launch { controller.loadNowPlaying() } })
            is NowPlayingState.Ready ->
                Ready(
                    state = current,
                    onSubmit = { query -> scope.launch { controller.submitSongRequest(query, null) } },
                )
        }
    }
}

@Composable
private fun Ready(state: NowPlayingState.Ready, onSubmit: (String) -> Unit) {
    val spacing = LocalSpacing.current

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s4), modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState())) {
        state.actionError?.let { ActionErrorBanner(detail = it) }
        NowPlayingCard(track = state.snapshot.nowPlaying)
        SubmitCard(
            pendingLimit = state.pendingLimit,
            subLaneUnlocked = state.subscriberLaneUnlocked,
            onSubmit = onSubmit,
        )
        QueueCard(queue = state.snapshot.queue)
    }
}

@Composable
private fun NowPlayingCard(track: NowPlaying?) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    SectionCard(title = stringResource(Res.string.participant_sr_now_playing)) {
        if (track == null) {
            Text(
                text = stringResource(Res.string.participant_sr_nothing_playing),
                style = typography.sm,
                color = tokens.mutedForeground,
            )
        } else {
            val name: String =
                track.trackName?.takeIf { it.isNotBlank() }
                    ?: stringResource(Res.string.participant_sr_unknown_track)
            Text(text = name, style = typography.base, color = tokens.cardForeground)
            track.artist?.takeIf { it.isNotBlank() }?.let { artist ->
                Text(text = artist, style = typography.sm, color = tokens.mutedForeground)
            }
            RequestedBy(track.requestedBy)
        }
    }
}

@Composable
private fun SubmitCard(pendingLimit: Int, subLaneUnlocked: Boolean, onSubmit: (String) -> Unit) {
    val spacing = LocalSpacing.current
    var query: String by remember { mutableStateOf("") }

    fun submit() {
        if (query.isNotBlank()) {
            onSubmit(query)
            query = ""
        }
    }

    SectionCard(title = stringResource(Res.string.participant_sr_submit)) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            ParticipantBadge(label = stringResource(Res.string.participant_sr_limit, pendingLimit))
            if (subLaneUnlocked) ParticipantBadge(label = stringResource(Res.string.participant_sr_sub_lane))
        }
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            OutlinedTextField(
                value = query,
                onValueChange = { query = it },
                placeholder = { Text(text = stringResource(Res.string.participant_sr_submit_placeholder)) },
                singleLine = true,
                modifier = Modifier.weight(1f),
                keyboardOptions = KeyboardOptions(imeAction = ImeAction.Send),
                keyboardActions = KeyboardActions(onSend = { submit() }),
            )
            TextButton(onClick = { submit() }, enabled = query.isNotBlank()) {
                Text(text = stringResource(Res.string.participant_sr_submit))
            }
        }
    }
}

@Composable
private fun QueueCard(queue: List<MusicTrack>) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    SectionCard(title = stringResource(Res.string.participant_sr_queue_title)) {
        if (queue.isEmpty()) {
            Text(
                text = stringResource(Res.string.participant_sr_queue_empty),
                style = typography.sm,
                color = tokens.mutedForeground,
            )
        } else {
            Column(
                modifier = Modifier.fillMaxWidth(),
                verticalArrangement = Arrangement.spacedBy(spacing.s2),
            ) {
                queue.forEach { track -> QueueRow(track = track) }
            }
        }
    }
}

@Composable
private fun QueueRow(track: MusicTrack) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.md))
            .padding(vertical = spacing.s1),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Column(modifier = Modifier.weight(1f)) {
            Text(
                text = track.trackName.takeIf { it.isNotBlank() }
                    ?: stringResource(Res.string.participant_sr_unknown_track),
                style = typography.sm,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            RequestedBy(track.requestedBy)
        }
    }
}

@Composable
private fun RequestedBy(requestedBy: String?) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    val who: String =
        requestedBy?.takeIf { it.isNotBlank() }
            ?: stringResource(Res.string.participant_sr_unknown_requester)
    Text(
        text = stringResource(Res.string.participant_sr_requested_by, who),
        style = typography.xs,
        color = tokens.mutedForeground,
    )
}
