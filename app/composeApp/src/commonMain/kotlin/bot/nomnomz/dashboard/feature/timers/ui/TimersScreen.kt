// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.timers.ui

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
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.text.style.TextAlign
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.TimerSummary
import bot.nomnomz.dashboard.feature.timers.state.TimersController
import bot.nomnomz.dashboard.feature.timers.state.TimersState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.timers_disabled
import nomnomzbot.composeapp.generated.resources.timers_empty
import nomnomzbot.composeapp.generated.resources.timers_enabled
import nomnomzbot.composeapp.generated.resources.timers_error
import nomnomzbot.composeapp.generated.resources.timers_interval
import nomnomzbot.composeapp.generated.resources.timers_loading
import nomnomzbot.composeapp.generated.resources.timers_message_count
import nomnomzbot.composeapp.generated.resources.timers_retry
import org.jetbrains.compose.resources.stringResource

// The Timers page: the channel's scheduled chat timers — real rows from [TimersController], rendered read-only.
// The screen is a pure projection of the controller's state; it loads on first composition and offers a retry
// on failure.
@Composable
fun TimersScreen(controller: TimersController) {
    val state: TimersState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    LaunchedEffect(Unit) { controller.load() }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: TimersState = state) {
            is TimersState.Loading -> CenteredMessage(stringResource(Res.string.timers_loading))
            is TimersState.Empty -> CenteredMessage(stringResource(Res.string.timers_empty))
            is TimersState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is TimersState.Ready -> TimerList(timers = current.timers)
        }
    }
}

@Composable
private fun TimerList(timers: List<TimerSummary>) {
    val spacing = LocalSpacing.current

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        items(items = timers, key = { it.id }) { timer -> TimerRow(timer = timer) }
    }
}

@Composable
private fun TimerRow(timer: TimerSummary) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val interval: String = stringResource(Res.string.timers_interval, timer.intervalMinutes)
    val messages: String = stringResource(Res.string.timers_message_count, timer.messageCount)
    val statusLabel: String =
        stringResource(if (timer.isEnabled) Res.string.timers_enabled else Res.string.timers_disabled)

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
            .padding(spacing.s4)
            // One node for screen readers: "Welcome, every 10m, 3 messages, On".
            .clearAndSetSemantics {
                contentDescription =
                    "${timer.name}, $interval, $messages, $statusLabel"
            },
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        Column(
            modifier = Modifier.weight(1f),
            verticalArrangement = Arrangement.spacedBy(spacing.s1),
        ) {
            Text(text = timer.name, style = typography.lg, color = tokens.cardForeground)
            Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                Text(text = interval, style = typography.sm, color = tokens.mutedForeground)
                Text(text = messages, style = typography.sm, color = tokens.mutedForeground)
            }
        }
        StatusBadge(enabled = timer.isEnabled, label = statusLabel)
    }
}

@Composable
private fun StatusBadge(enabled: Boolean, label: String) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Box(
        modifier = Modifier
            .clip(RoundedCornerShape(tokens.radius.sm))
            .background(if (enabled) tokens.primary else tokens.muted)
            .padding(horizontal = spacing.s2, vertical = spacing.s1),
    ) {
        Text(
            text = label,
            style = typography.xs,
            color = if (enabled) tokens.primaryForeground else tokens.mutedForeground,
        )
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
                text = stringResource(Res.string.timers_error, detail),
                style = typography.base,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.timers_retry)) }
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
