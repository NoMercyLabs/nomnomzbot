// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.commands.ui

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
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.CommandSummary
import bot.nomnomz.dashboard.feature.commands.state.CommandsController
import bot.nomnomz.dashboard.feature.commands.state.CommandsState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.commands_badge_disabled
import nomnomzbot.composeapp.generated.resources.commands_badge_enabled
import nomnomzbot.composeapp.generated.resources.commands_empty
import nomnomzbot.composeapp.generated.resources.commands_error
import nomnomzbot.composeapp.generated.resources.commands_loading
import nomnomzbot.composeapp.generated.resources.commands_no_description
import nomnomzbot.composeapp.generated.resources.commands_retry
import org.jetbrains.compose.resources.stringResource

// The Commands page (frontend-ia.md §3, Chat group): the channel's custom chat commands, all real data from
// [CommandsController]. The screen is a pure projection of the controller's state; it loads on first
// composition and offers a retry on failure. Read-only for this slice — create/edit land later.
@Composable
fun CommandsScreen(controller: CommandsController) {
    val state: CommandsState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    LaunchedEffect(Unit) { controller.load() }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: CommandsState = state) {
            is CommandsState.Loading -> CenteredMessage(stringResource(Res.string.commands_loading))
            is CommandsState.Empty -> CenteredMessage(stringResource(Res.string.commands_empty))
            is CommandsState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is CommandsState.Ready -> CommandList(commands = current.commands)
        }
    }
}

@Composable
private fun CommandList(commands: List<CommandSummary>) {
    val spacing = LocalSpacing.current

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(vertical = spacing.s1),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        items(items = commands, key = { command -> command.id }) { command ->
            CommandRow(command = command)
        }
    }
}

@Composable
private fun CommandRow(command: CommandSummary) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val snippet: String =
        command.description?.takeIf { it.isNotBlank() }
            ?: stringResource(Res.string.commands_no_description)
    val stateLabel: String =
        stringResource(
            if (command.isEnabled) Res.string.commands_badge_enabled
            else Res.string.commands_badge_disabled
        )

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
            .padding(spacing.s4)
            // One node for screen readers: "!hello, enabled. <description>".
            .clearAndSetSemantics {
                contentDescription =
                    "${command.name}, $stateLabel. $snippet"
            },
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Column(
            modifier = Modifier.weight(1f),
            verticalArrangement = Arrangement.spacedBy(spacing.s1),
        ) {
            Text(
                text = command.name,
                style = typography.lg,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = snippet,
                style = typography.sm,
                color = tokens.mutedForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
        StateBadge(label = stateLabel, enabled = command.isEnabled)
    }
}

// The badge carries no own semantics: the parent row's `clearAndSetSemantics` already folds the state into
// one screen-reader node ("!hello, enabled. …"), so a separate badge announcement would just be noise.
@Composable
private fun StateBadge(label: String, enabled: Boolean) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Text(
        text = label,
        style = typography.xs,
        color = if (enabled) tokens.primaryForeground else tokens.mutedForeground,
        modifier = Modifier
            .clip(RoundedCornerShape(tokens.radius.sm))
            .background(if (enabled) tokens.primary else tokens.muted)
            .padding(horizontal = spacing.s2, vertical = spacing.s1),
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
                text = stringResource(Res.string.commands_error, detail),
                style = typography.base,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.commands_retry)) }
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
