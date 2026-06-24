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
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TextFieldColors
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
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.designsystem.theme.Tokens
import bot.nomnomz.dashboard.core.network.GameSummary
import bot.nomnomz.dashboard.feature.games.state.GamesController
import bot.nomnomz.dashboard.feature.games.state.GamesState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.games_18plus
import nomnomzbot.composeapp.generated.resources.games_action_error
import nomnomzbot.composeapp.generated.resources.games_cooldown
import nomnomzbot.composeapp.generated.resources.games_dialog_18plus_label
import nomnomzbot.composeapp.generated.resources.games_dialog_cancel
import nomnomzbot.composeapp.generated.resources.games_dialog_cooldown_label
import nomnomzbot.composeapp.generated.resources.games_dialog_max_bet_label
import nomnomzbot.composeapp.generated.resources.games_dialog_min_bet_label
import nomnomzbot.composeapp.generated.resources.games_dialog_save
import nomnomzbot.composeapp.generated.resources.games_dialog_title
import nomnomzbot.composeapp.generated.resources.games_disabled
import nomnomzbot.composeapp.generated.resources.games_edit_action
import nomnomzbot.composeapp.generated.resources.games_edit_action_short
import nomnomzbot.composeapp.generated.resources.games_empty
import nomnomzbot.composeapp.generated.resources.games_enabled
import nomnomzbot.composeapp.generated.resources.games_error
import nomnomzbot.composeapp.generated.resources.games_loading
import nomnomzbot.composeapp.generated.resources.games_retry
import nomnomzbot.composeapp.generated.resources.games_row_description
import nomnomzbot.composeapp.generated.resources.games_toggle_action
import org.jetbrains.compose.resources.stringResource

// The Games page (economy.md §3.5): the channel's configured mini-games — every game is real config from
// [GamesController] (the backend sources it from the channel's game config; no fabricated games). Games are a
// fixed catalog of built-in types, so this is a MANAGEMENT (not create/delete) surface: each row toggles its
// enabled flag inline and opens a config dialog (bet limits, cooldown, 18+ gate). Every write routes back through
// the controller, which re-lists after each success so the page reflects the backend. The screen loads on first
// composition and offers a retry on failure.
@Composable
fun GamesScreen(controller: GamesController) {
    val state: GamesState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    // The config dialog target: null = closed, a game = open and editing that game's config.
    var editing: GameSummary? by remember { mutableStateOf(null) }

    LaunchedEffect(Unit) { controller.load() }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: GamesState = state) {
            is GamesState.Loading -> CenteredMessage(stringResource(Res.string.games_loading))
            is GamesState.Empty -> CenteredMessage(stringResource(Res.string.games_empty))
            is GamesState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is GamesState.Ready ->
                ManagedContent(
                    games = current.games,
                    actionError = current.actionError,
                    onToggle = { game, enabled ->
                        scope.launch { controller.toggleGame(game, enabled) }
                    },
                    onEdit = { game -> editing = game },
                )
        }
    }

    editing?.let { game ->
        GameConfigDialog(
            game = game,
            onDismiss = { editing = null },
            onSave = { minBet, maxBet, cooldownSeconds, requires18Plus ->
                editing = null
                scope.launch {
                    controller.updateGameConfig(game, minBet, maxBet, cooldownSeconds, requires18Plus)
                }
            },
        )
    }
}

// The list with an optional write-failure banner above it. The header is omitted (no create action — the catalog
// is fixed); the banner surfaces a failed toggle/edit without clearing the rows.
@Composable
private fun ManagedContent(
    games: List<GameSummary>,
    actionError: String?,
    onToggle: (GameSummary, Boolean) -> Unit,
    onEdit: (GameSummary) -> Unit,
) {
    val spacing = LocalSpacing.current

    Column(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        actionError?.let { ActionErrorBanner(detail = it) }
        GameList(games = games, onToggle = onToggle, onEdit = onEdit)
    }
}

@Composable
private fun ActionErrorBanner(detail: String) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Text(
        text = stringResource(Res.string.games_action_error, detail),
        style = typography.sm,
        color = tokens.destructiveForeground,
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.md))
            .background(tokens.destructive)
            .padding(horizontal = spacing.s3, vertical = spacing.s2),
    )
}

@Composable
private fun GameList(
    games: List<GameSummary>,
    onToggle: (GameSummary, Boolean) -> Unit,
    onEdit: (GameSummary) -> Unit,
) {
    val spacing = LocalSpacing.current

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(vertical = spacing.s1),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        items(items = games, key = { game -> game.id }) { game ->
            GameRow(
                game = game,
                onToggle = { enabled -> onToggle(game, enabled) },
                onEdit = { onEdit(game) },
            )
        }
    }
}

@Composable
private fun GameRow(
    game: GameSummary,
    onToggle: (Boolean) -> Unit,
    onEdit: () -> Unit,
) {
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
    // One node for screen readers describing the game: "coinflip, Enabled, 30s cooldown".
    val rowDescription: String =
        stringResource(
            Res.string.games_row_description,
            game.gameType,
            statusLabel,
            cooldownLabel ?: "",
        )
    val toggleLabel: String = stringResource(Res.string.games_toggle_action, game.gameType)
    val editLabel: String = stringResource(Res.string.games_edit_action, game.gameType)

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Column(
            modifier = Modifier
                .weight(1f)
                .clearAndSetSemantics { contentDescription = rowDescription },
            verticalArrangement = Arrangement.spacedBy(spacing.s1),
        ) {
            Text(
                text = game.gameType,
                style = typography.base,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            if (game.category.isNotBlank()) {
                Text(
                    text = game.category,
                    style = typography.xs,
                    color = tokens.mutedForeground,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
            if (cooldownLabel != null) {
                Text(
                    text = cooldownLabel,
                    style = typography.xs,
                    color = tokens.mutedForeground,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
        }
        if (game.requires18Plus) {
            Badge(
                label = stringResource(Res.string.games_18plus),
                background = tokens.destructive,
                foreground = tokens.destructiveForeground,
            )
        }
        Switch(
            checked = game.isEnabled,
            onCheckedChange = onToggle,
            colors = switchColors(),
            modifier = Modifier.semantics { contentDescription = toggleLabel },
        )
        TextButton(
            onClick = onEdit,
            modifier = Modifier.semantics { contentDescription = editLabel },
        ) {
            Text(
                text = stringResource(Res.string.games_edit_action_short),
                color = tokens.primary,
                maxLines = 1,
            )
        }
    }
}

// The per-game config editor: bet limits, cooldown, and the 18+ gate. Bet fields accept whole numbers; a blank
// bet means "no limit" (null). The cooldown defaults to the row's current value and falls back to 0 when blank.
// Save is disabled while any numeric field holds non-digits or min bet exceeds max bet, so an invalid config can
// never be sent. The game type, category, odds, permission, and per-stream cap are not edited here — the
// controller carries them back unchanged.
@Composable
private fun GameConfigDialog(
    game: GameSummary,
    onDismiss: () -> Unit,
    onSave: (minBet: Long?, maxBet: Long?, cooldownSeconds: Int, requires18Plus: Boolean) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    var minBet: String by remember { mutableStateOf(game.minBet?.toString().orEmpty()) }
    var maxBet: String by remember { mutableStateOf(game.maxBet?.toString().orEmpty()) }
    var cooldown: String by remember { mutableStateOf(game.cooldownSeconds.toString()) }
    var requires18Plus: Boolean by remember { mutableStateOf(game.requires18Plus) }

    // A blank bet field is a valid "no limit"; a non-blank field must parse to a non-negative whole number.
    val minBetValue: Long? = minBet.toLongOrNull()
    val maxBetValue: Long? = maxBet.toLongOrNull()
    val cooldownValue: Int? = cooldown.ifBlank { "0" }.toIntOrNull()
    val minBetValid: Boolean = minBet.isBlank() || (minBetValue != null && minBetValue >= 0)
    val maxBetValid: Boolean = maxBet.isBlank() || (maxBetValue != null && maxBetValue >= 0)
    val cooldownValid: Boolean = cooldownValue != null && cooldownValue >= 0
    val rangeValid: Boolean = minBetValue == null || maxBetValue == null || minBetValue <= maxBetValue
    val canSave: Boolean = minBetValid && maxBetValid && cooldownValid && rangeValid

    val eighteenPlusLabel: String = stringResource(Res.string.games_dialog_18plus_label)

    AlertDialog(
        onDismissRequest = onDismiss,
        containerColor = tokens.card,
        titleContentColor = tokens.cardForeground,
        textContentColor = tokens.mutedForeground,
        title = { Text(text = stringResource(Res.string.games_dialog_title, game.gameType)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                OutlinedTextField(
                    value = minBet,
                    onValueChange = { minBet = it },
                    isError = !minBetValid || !rangeValid,
                    singleLine = true,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    modifier = Modifier.fillMaxWidth(),
                    label = { Text(stringResource(Res.string.games_dialog_min_bet_label)) },
                    colors = fieldColors(),
                )
                OutlinedTextField(
                    value = maxBet,
                    onValueChange = { maxBet = it },
                    isError = !maxBetValid || !rangeValid,
                    singleLine = true,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    modifier = Modifier.fillMaxWidth(),
                    label = { Text(stringResource(Res.string.games_dialog_max_bet_label)) },
                    colors = fieldColors(),
                )
                OutlinedTextField(
                    value = cooldown,
                    onValueChange = { cooldown = it },
                    isError = !cooldownValid,
                    singleLine = true,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    modifier = Modifier.fillMaxWidth(),
                    label = { Text(stringResource(Res.string.games_dialog_cooldown_label)) },
                    colors = fieldColors(),
                )
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.SpaceBetween,
                ) {
                    Text(text = eighteenPlusLabel, color = tokens.cardForeground)
                    Switch(
                        checked = requires18Plus,
                        onCheckedChange = { requires18Plus = it },
                        colors = switchColors(),
                        modifier = Modifier.semantics { contentDescription = eighteenPlusLabel },
                    )
                }
            }
        },
        confirmButton = {
            TextButton(
                onClick = {
                    onSave(minBetValue, maxBetValue, cooldownValue ?: 0, requires18Plus)
                },
                enabled = canSave,
            ) {
                Text(
                    text = stringResource(Res.string.games_dialog_save),
                    color = if (canSave) tokens.primary else tokens.mutedForeground,
                )
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(
                    text = stringResource(Res.string.games_dialog_cancel),
                    color = tokens.mutedForeground,
                )
            }
        },
    )
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

// The shared switch color set: every slot driven by a token so the control reads on-theme in light + dark.
@Composable
private fun switchColors() =
    SwitchDefaults.colors(
        checkedThumbColor = LocalTokens.current.primaryForeground,
        checkedTrackColor = LocalTokens.current.primary,
        uncheckedThumbColor = LocalTokens.current.mutedForeground,
        uncheckedTrackColor = LocalTokens.current.muted,
        uncheckedBorderColor = LocalTokens.current.border,
    )

// The shared text-field color set: every slot driven by a token so the field reads on-theme in light + dark.
@Composable
private fun fieldColors(): TextFieldColors {
    val tokens: Tokens = LocalTokens.current
    return OutlinedTextFieldDefaults.colors(
        focusedTextColor = tokens.cardForeground,
        unfocusedTextColor = tokens.cardForeground,
        disabledTextColor = tokens.mutedForeground,
        focusedBorderColor = tokens.ring,
        unfocusedBorderColor = tokens.border,
        disabledBorderColor = tokens.border,
        errorBorderColor = tokens.destructive,
        focusedLabelColor = tokens.mutedForeground,
        unfocusedLabelColor = tokens.mutedForeground,
        disabledLabelColor = tokens.mutedForeground,
        errorLabelColor = tokens.destructive,
        cursorColor = tokens.primary,
    )
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
