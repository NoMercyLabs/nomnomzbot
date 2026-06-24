// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.games.ui

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
import bot.nomnomz.dashboard.core.network.GameSummary
import bot.nomnomz.dashboard.feature.games.state.GamesController
import bot.nomnomz.dashboard.feature.games.state.GamesState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.games_18plus
import nomnomzbot.composeapp.generated.resources.games_cooldown
import nomnomzbot.composeapp.generated.resources.games_disabled
import nomnomzbot.composeapp.generated.resources.games_empty
import nomnomzbot.composeapp.generated.resources.games_enabled
import nomnomzbot.composeapp.generated.resources.games_error
import nomnomzbot.composeapp.generated.resources.games_loading
import nomnomzbot.composeapp.generated.resources.games_row_description
import nomnomzbot.composeapp.generated.resources.games_retry
import org.jetbrains.compose.resources.stringResource

// The Games page (economy.md §3.5): the channel's configured mini-games — every game is real config from
// [GamesController] (the backend sources it from the channel's game config; no fabricated games). The screen is a
// pure read-only projection of the controller's state; it loads on first composition and offers a retry on
// failure.
@Composable
fun GamesScreen(controller: GamesController) {
    val state: GamesState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    LaunchedEffect(Unit) { controller.load() }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: GamesState = state) {
            is GamesState.Loading -> CenteredMessage(stringResource(Res.string.games_loading))
            is GamesState.Empty -> CenteredMessage(stringResource(Res.string.games_empty))
            is GamesState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is GamesState.Ready -> GameList(games = current.games)
        }
    }
}

@Composable
private fun GameList(games: List<GameSummary>) {
    val spacing = LocalSpacing.current

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        items(items = games, key = { game -> game.id }) { game -> GameRow(game = game) }
    }
}

@Composable
private fun GameRow(game: GameSummary) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val statusLabel: String =
        if (game.isEnabled) stringResource(Res.string.games_enabled)
        else stringResource(Res.string.games_disabled)
    val cooldownLabel: String? =
        if (game.cooldownSeconds > 0)
            stringResource(Res.string.games_cooldown, game.cooldownSeconds)
        else null
    // One node for screen readers: "coinflip, Enabled, 30s cooldown" rather than scattered texts.
    val rowDescription: String =
        stringResource(
            Res.string.games_row_description,
            game.gameType,
            statusLabel,
            cooldownLabel ?: "",
        )

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
            .padding(horizontal = spacing.s4, vertical = spacing.s3)
            .clearAndSetSemantics { contentDescription = rowDescription },
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Column(
            modifier = Modifier.weight(1f),
            verticalArrangement = Arrangement.spacedBy(spacing.s1),
        ) {
            Text(text = game.gameType, style = typography.base, color = tokens.cardForeground)
            if (game.category.isNotBlank()) {
                Text(text = game.category, style = typography.xs, color = tokens.mutedForeground)
            }
            if (cooldownLabel != null) {
                Text(text = cooldownLabel, style = typography.xs, color = tokens.mutedForeground)
            }
        }
        if (game.requires18Plus) {
            Badge(
                label = stringResource(Res.string.games_18plus),
                background = tokens.destructive,
                foreground = tokens.destructiveForeground,
            )
        }
        Badge(
            label = statusLabel,
            background = if (game.isEnabled) tokens.primary else tokens.secondary,
            foreground = if (game.isEnabled) tokens.primaryForeground else tokens.secondaryForeground,
        )
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
                text = stringResource(Res.string.games_error, detail),
                style = typography.base,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.games_retry)) }
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
