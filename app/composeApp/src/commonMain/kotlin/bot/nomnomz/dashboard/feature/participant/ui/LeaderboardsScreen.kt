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
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.LeaderboardEntry
import bot.nomnomz.dashboard.feature.participant.state.LeaderboardsState
import bot.nomnomz.dashboard.feature.participant.state.ParticipantController
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.participant_lb_consent_label
import nomnomzbot.composeapp.generated.resources.participant_lb_empty
import nomnomzbot.composeapp.generated.resources.participant_lb_row_description
import nomnomzbot.composeapp.generated.resources.participant_lb_sub_board
import nomnomzbot.composeapp.generated.resources.participant_lb_title
import nomnomzbot.composeapp.generated.resources.participant_loading
import org.jetbrains.compose.resources.stringResource

// Leaderboards: the channel's top-holders ranking (read), plus the caller's OWN opt-in / opt-out toggle
// (economy:leaderboards:opt-in/out) — self-service consent, not management. A sub or above sees the sub-only
// leaderboard marker, surfaced from their community standing.
@Composable
fun LeaderboardsScreen(controller: ParticipantController) {
    val state: LeaderboardsState by controller.leaderboards.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    LaunchedEffect(Unit) { controller.loadLeaderboards() }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: LeaderboardsState = state) {
            is LeaderboardsState.Loading -> ParticipantMessage(stringResource(Res.string.participant_loading))
            is LeaderboardsState.Error ->
                ParticipantError(detail = current.detail, onRetry = { scope.launch { controller.loadLeaderboards() } })
            is LeaderboardsState.Ready ->
                Ready(
                    state = current,
                    onToggleConsent = { optIn ->
                        scope.launch {
                            if (optIn) controller.optInToLeaderboards()
                            else controller.optOutOfLeaderboards()
                        }
                    },
                )
        }
    }
}

@Composable
private fun Ready(state: LeaderboardsState.Ready, onToggleConsent: (Boolean) -> Unit) {
    val spacing = LocalSpacing.current

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s4), modifier = Modifier.fillMaxSize()) {
        state.actionError?.let { ActionErrorBanner(detail = it) }
        ConsentCard(optedIn = state.optedIn, onToggle = onToggleConsent)
        RankingCard(state = state)
    }
}

@Composable
private fun ConsentCard(optedIn: Boolean, onToggle: (Boolean) -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val label: String = stringResource(Res.string.participant_lb_consent_label)

    SectionCard(title = label) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.SpaceBetween,
        ) {
            Text(text = label, style = typography.sm, color = tokens.mutedForeground)
            Switch(
                checked = optedIn,
                onCheckedChange = onToggle,
                modifier = Modifier.clearAndSetSemantics { contentDescription = label },
            )
        }
    }
}

@Composable
private fun RankingCard(state: LeaderboardsState.Ready) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val title: String =
        if (state.subscriberBoardUnlocked) stringResource(Res.string.participant_lb_sub_board)
        else stringResource(Res.string.participant_lb_title)

    SectionCard(title = title) {
        if (state.ranking.isEmpty()) {
            Text(
                text = stringResource(Res.string.participant_lb_empty),
                style = typography.sm,
                color = tokens.mutedForeground,
            )
        } else {
            LazyColumn(
                modifier = Modifier.fillMaxWidth(),
                verticalArrangement = Arrangement.spacedBy(spacing.s2),
            ) {
                items(items = state.ranking, key = { it.rank }) { entry -> RankRow(entry = entry) }
            }
        }
    }
}

@Composable
private fun RankRow(entry: LeaderboardEntry) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val description: String =
        stringResource(
            Res.string.participant_lb_row_description,
            entry.rank,
            entry.displayName,
            entry.points,
        )

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(vertical = spacing.s1)
            .clearAndSetSemantics { contentDescription = description },
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Text(text = "#${entry.rank}", style = typography.sm, color = tokens.mutedForeground)
        Text(
            text = entry.displayName,
            style = typography.sm,
            color = tokens.cardForeground,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.weight(1f),
        )
        Text(text = entry.points.toString(), style = typography.sm, color = tokens.cardForeground)
    }
}
