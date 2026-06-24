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
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.OutlinedTextField
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
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.GamePlay
import bot.nomnomz.dashboard.core.network.GamePlayResult
import bot.nomnomz.dashboard.core.network.GameSummary
import bot.nomnomz.dashboard.feature.participant.state.ParticipantController
import bot.nomnomz.dashboard.feature.participant.state.ParticipantGamesState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.participant_games_18plus
import nomnomzbot.composeapp.generated.resources.participant_games_bet
import nomnomzbot.composeapp.generated.resources.participant_games_empty
import nomnomzbot.composeapp.generated.resources.participant_games_history_empty
import nomnomzbot.composeapp.generated.resources.participant_games_history_row
import nomnomzbot.composeapp.generated.resources.participant_games_history_title
import nomnomzbot.composeapp.generated.resources.participant_games_outcome
import nomnomzbot.composeapp.generated.resources.participant_games_play
import nomnomzbot.composeapp.generated.resources.participant_games_title
import nomnomzbot.composeapp.generated.resources.participant_loading
import org.jetbrains.compose.resources.stringResource

// Games: the channel's playable games (read), the caller's own self-service play (economy:games:play — bet bound
// to the caller server-side), and their OWN play history. The last settled outcome is surfaced after a play so the
// player sees what happened; only enabled games are offered.
@Composable
fun ParticipantGamesScreen(controller: ParticipantController) {
    val state: ParticipantGamesState by controller.games.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    LaunchedEffect(Unit) { controller.loadGames() }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: ParticipantGamesState = state) {
            is ParticipantGamesState.Loading -> ParticipantMessage(stringResource(Res.string.participant_loading))
            is ParticipantGamesState.Error ->
                ParticipantError(detail = current.detail, onRetry = { scope.launch { controller.loadGames() } })
            is ParticipantGamesState.Ready ->
                Ready(
                    state = current,
                    onPlay = { gameId, bet -> scope.launch { controller.playGame(gameId, bet) } },
                )
        }
    }
}

@Composable
private fun Ready(state: ParticipantGamesState.Ready, onPlay: (String, Long) -> Unit) {
    val spacing = LocalSpacing.current

    Column(
        modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        state.actionError?.let { ActionErrorBanner(detail = it) }
        state.lastOutcome?.let { OutcomeCard(outcome = it) }
        GamesCard(games = state.games, onPlay = onPlay)
        HistoryCard(history = state.history)
    }
}

@Composable
private fun OutcomeCard(outcome: GamePlayResult) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    SectionCard(title = stringResource(Res.string.participant_games_outcome)) {
        Text(
            text =
                stringResource(
                    Res.string.participant_games_history_row,
                    outcome.gameType,
                    outcome.outcome,
                    outcome.netResult,
                ),
            style = typography.sm,
            color = if (outcome.netResult >= 0) tokens.cardForeground else tokens.destructive,
        )
    }
}

@Composable
private fun GamesCard(games: List<GameSummary>, onPlay: (String, Long) -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    SectionCard(title = stringResource(Res.string.participant_games_title)) {
        if (games.isEmpty()) {
            Text(
                text = stringResource(Res.string.participant_games_empty),
                style = typography.sm,
                color = tokens.mutedForeground,
            )
        } else {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                games.forEach { game -> GameRow(game = game, onPlay = onPlay) }
            }
        }
    }
}

@Composable
private fun GameRow(game: GameSummary, onPlay: (String, Long) -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var bet: String by remember { mutableStateOf("") }
    val parsedBet: Long? = bet.toLongOrNull()
    val enabled: Boolean = parsedBet != null && parsedBet > 0
    val playLabel: String = stringResource(Res.string.participant_games_play, game.gameType)

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            Text(
                text = game.gameType,
                style = typography.sm,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.weight(1f),
            )
            if (game.requires18Plus) ParticipantBadge(label = stringResource(Res.string.participant_games_18plus))
        }
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            OutlinedTextField(
                value = bet,
                onValueChange = { bet = it.filter { ch -> ch.isDigit() } },
                label = { Text(text = stringResource(Res.string.participant_games_bet)) },
                singleLine = true,
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                modifier = Modifier.weight(1f),
            )
            TextButton(
                onClick = {
                    val value: Long = parsedBet ?: return@TextButton
                    onPlay(game.id, value)
                    bet = ""
                },
                enabled = enabled,
                modifier = Modifier.semantics { contentDescription = playLabel },
            ) {
                Text(text = stringResource(Res.string.participant_games_play, game.gameType), maxLines = 1)
            }
        }
    }
}

@Composable
private fun HistoryCard(history: List<GamePlay>) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    SectionCard(title = stringResource(Res.string.participant_games_history_title)) {
        if (history.isEmpty()) {
            Text(
                text = stringResource(Res.string.participant_games_history_empty),
                style = typography.sm,
                color = tokens.mutedForeground,
            )
        } else {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
                history.forEach { play ->
                    Text(
                        text =
                            stringResource(
                                Res.string.participant_games_history_row,
                                play.outcome,
                                play.betAmount.toString(),
                                play.netResult,
                            ),
                        style = typography.xs,
                        color = if (play.netResult >= 0) tokens.mutedForeground else tokens.destructive,
                    )
                }
            }
        }
    }
}
