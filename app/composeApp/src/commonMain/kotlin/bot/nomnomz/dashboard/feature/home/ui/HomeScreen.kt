// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.home.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
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
import bot.nomnomz.dashboard.core.network.DashboardStats
import bot.nomnomz.dashboard.feature.home.state.HomeController
import bot.nomnomz.dashboard.feature.home.state.HomeState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.home_error
import nomnomzbot.composeapp.generated.resources.home_game_label
import nomnomzbot.composeapp.generated.resources.home_loading
import nomnomzbot.composeapp.generated.resources.home_no_title
import nomnomzbot.composeapp.generated.resources.home_retry
import nomnomzbot.composeapp.generated.resources.home_stat_commands
import nomnomzbot.composeapp.generated.resources.home_stat_followers
import nomnomzbot.composeapp.generated.resources.home_stat_messages
import nomnomzbot.composeapp.generated.resources.home_stat_uptime
import nomnomzbot.composeapp.generated.resources.home_stat_viewers
import nomnomzbot.composeapp.generated.resources.home_status_live
import nomnomzbot.composeapp.generated.resources.home_status_offline
import nomnomzbot.composeapp.generated.resources.home_uptime_format
import nomnomzbot.composeapp.generated.resources.home_uptime_offline
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource

// The Home page (frontend-ia.md §3): the live channel landing — current stream state + the headline counters,
// all real data from [HomeController]. The screen is a pure projection of the controller's state; it loads on
// first composition and offers a retry on failure.
@Composable
fun HomeScreen(controller: HomeController) {
    val state: HomeState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    LaunchedEffect(Unit) { controller.load() }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: HomeState = state) {
            is HomeState.Loading -> CenteredMessage(stringResource(Res.string.home_loading))
            is HomeState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is HomeState.Ready -> ReadyContent(stats = current.stats)
        }
    }
}

@Composable
private fun ReadyContent(stats: DashboardStats) {
    val spacing = LocalSpacing.current

    Column(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        LiveBanner(stats = stats)
        StatTiles(stats = stats)
    }
}

@Composable
private fun LiveBanner(stats: DashboardStats) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
            .padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Row(
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            Box(
                modifier = Modifier
                    .size(spacing.s2)
                    .clip(CircleShape)
                    .background(if (stats.isLive) tokens.primary else tokens.mutedForeground),
            )
            Text(
                text =
                    stringResource(
                        if (stats.isLive) Res.string.home_status_live else Res.string.home_status_offline
                    ),
                style = typography.sm,
                color = tokens.mutedForeground,
            )
        }
        Text(
            text = stats.streamTitle?.takeIf { it.isNotBlank() }
                ?: stringResource(Res.string.home_no_title),
            style = typography.xl,
            color = tokens.cardForeground,
        )
        stats.gameName?.takeIf { it.isNotBlank() }?.let { game ->
            Text(
                text = stringResource(Res.string.home_game_label, game),
                style = typography.sm,
                color = tokens.mutedForeground,
            )
        }
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun StatTiles(stats: DashboardStats) {
    val spacing = LocalSpacing.current

    FlowRow(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        StatTile(Res.string.home_stat_viewers, stats.viewerCount.toString())
        StatTile(Res.string.home_stat_followers, stats.followerCount.toString())
        StatTile(Res.string.home_stat_commands, stats.commandsUsed.toString())
        StatTile(Res.string.home_stat_messages, stats.messagesCount.toString())
        StatTile(Res.string.home_stat_uptime, uptimeLabel(stats.uptime))
    }
}

@Composable
private fun StatTile(labelRes: StringResource, value: String) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val label: String = stringResource(labelRes)

    Column(
        modifier = Modifier
            .width(spacing.s24 * 1.6f)
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
            .padding(spacing.s4)
            // One node for screen readers: "Viewers: 42" rather than two disconnected texts.
            .clearAndSetSemantics { contentDescription = "$label: $value" },
        verticalArrangement = Arrangement.spacedBy(spacing.s1),
    ) {
        Text(text = value, style = typography.xl2, color = tokens.cardForeground)
        Text(text = label, style = typography.sm, color = tokens.mutedForeground)
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
                text = stringResource(Res.string.home_error, detail),
                style = typography.base,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.home_retry)) }
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

/** Render uptime seconds as "Xh Ym", or the offline placeholder when there is no live stream. */
@Composable
private fun uptimeLabel(seconds: Long?): String =
    if (seconds == null) {
        stringResource(Res.string.home_uptime_offline)
    } else {
        stringResource(
            Res.string.home_uptime_format,
            (seconds / 3600).toInt(),
            ((seconds % 3600) / 60).toInt(),
        )
    }
