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
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.text.style.TextAlign
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.QueuedSong
import bot.nomnomz.dashboard.feature.songrequests.state.SongRequestsController
import bot.nomnomz.dashboard.feature.songrequests.state.SongRequestsState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.songrequests_empty
import nomnomzbot.composeapp.generated.resources.songrequests_error
import nomnomzbot.composeapp.generated.resources.songrequests_loading
import nomnomzbot.composeapp.generated.resources.songrequests_position
import nomnomzbot.composeapp.generated.resources.songrequests_requested_by
import nomnomzbot.composeapp.generated.resources.songrequests_retry
import nomnomzbot.composeapp.generated.resources.songrequests_row_description
import nomnomzbot.composeapp.generated.resources.songrequests_unknown_requester
import org.jetbrains.compose.resources.stringResource

// The Song Requests page: the channel's live music queue — every track is real data from
// [SongRequestsController] (the backend sources it from the connected music provider). The screen is a pure
// projection of the controller's state; it loads on first composition and offers a retry on failure. Read-only
// this slice (no skip/remove actions).
@Composable
fun SongRequestsScreen(controller: SongRequestsController) {
    val state: SongRequestsState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    LaunchedEffect(Unit) { controller.load() }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: SongRequestsState = state) {
            is SongRequestsState.Loading ->
                CenteredMessage(stringResource(Res.string.songrequests_loading))
            is SongRequestsState.Empty ->
                CenteredMessage(stringResource(Res.string.songrequests_empty))
            is SongRequestsState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is SongRequestsState.Ready -> QueueList(queue = current.queue)
        }
    }
}

@Composable
private fun QueueList(queue: List<QueuedSong>) {
    val spacing = LocalSpacing.current

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        items(items = queue, key = { song -> song.position }) { song -> QueueRow(song = song) }
    }
}

@Composable
private fun QueueRow(song: QueuedSong) {
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

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
            .padding(horizontal = spacing.s4, vertical = spacing.s3)
            // One node for screen readers: "1, Track Title, requested by Stoney_Eagle".
            .clearAndSetSemantics { contentDescription = rowDescription },
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Badge(
            label = positionLabel,
            background = tokens.secondary,
            foreground = tokens.secondaryForeground,
        )
        Column(
            modifier = Modifier.weight(1f),
            verticalArrangement = Arrangement.spacedBy(spacing.s1),
        ) {
            Text(text = title, style = typography.base, color = tokens.cardForeground)
            if (song.artist.isNotBlank()) {
                Text(text = song.artist, style = typography.sm, color = tokens.mutedForeground)
            }
        }
        Text(text = requestedLabel, style = typography.xs, color = tokens.mutedForeground)
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
        Text(text = label, style = typography.xs, color = foreground)
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
