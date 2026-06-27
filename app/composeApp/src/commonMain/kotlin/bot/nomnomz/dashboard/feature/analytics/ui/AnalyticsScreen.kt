// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.analytics.ui

import androidx.compose.foundation.background
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
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
import bot.nomnomz.dashboard.core.network.AnalyticsSummary
import bot.nomnomz.dashboard.core.network.DailyMetricRow
import bot.nomnomz.dashboard.core.network.TopViewerEntry
import bot.nomnomz.dashboard.feature.analytics.state.AnalyticsController
import bot.nomnomz.dashboard.feature.analytics.state.AnalyticsState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.analytics_daily_empty
import nomnomzbot.composeapp.generated.resources.analytics_daily_title
import nomnomzbot.composeapp.generated.resources.analytics_error
import nomnomzbot.composeapp.generated.resources.analytics_loading
import nomnomzbot.composeapp.generated.resources.analytics_peak_offline
import nomnomzbot.composeapp.generated.resources.analytics_retry
import nomnomzbot.composeapp.generated.resources.analytics_stat_bits
import nomnomzbot.composeapp.generated.resources.analytics_stat_commands
import nomnomzbot.composeapp.generated.resources.analytics_stat_currency_earned
import nomnomzbot.composeapp.generated.resources.analytics_stat_currency_spent
import nomnomzbot.composeapp.generated.resources.analytics_stat_followers
import nomnomzbot.composeapp.generated.resources.analytics_stat_messages
import nomnomzbot.composeapp.generated.resources.analytics_stat_peak_viewers
import nomnomzbot.composeapp.generated.resources.analytics_stat_redemptions
import nomnomzbot.composeapp.generated.resources.analytics_stat_song_requests
import nomnomzbot.composeapp.generated.resources.analytics_stat_subscribers
import nomnomzbot.composeapp.generated.resources.analytics_top_viewers_empty
import nomnomzbot.composeapp.generated.resources.analytics_top_viewers_rank
import nomnomzbot.composeapp.generated.resources.analytics_top_viewers_title
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource

// The Analytics page (analytics.md §4): headline totals, daily trend rows, and top viewers over a trailing
// window — all real data from [AnalyticsController]. Loads on first composition; retries on failure.
@Composable
fun AnalyticsScreen(controller: AnalyticsController) {
    val state: AnalyticsState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    LaunchedEffect(Unit) { controller.load() }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: AnalyticsState = state) {
            is AnalyticsState.Loading -> CenteredMessage(stringResource(Res.string.analytics_loading))
            is AnalyticsState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is AnalyticsState.Ready ->
                ReadyContent(
                    summary = current.summary,
                    daily = current.daily,
                    topViewers = current.topViewers,
                )
        }
    }
}

@Composable
private fun ReadyContent(
    summary: AnalyticsSummary,
    daily: List<DailyMetricRow>,
    topViewers: List<TopViewerEntry>,
) {
    val spacing = LocalSpacing.current

    LazyColumn(verticalArrangement = Arrangement.spacedBy(spacing.s6)) {
        item { StatTiles(summary = summary) }
        item { DailyTrendsSection(daily = daily) }
        item { TopViewersSection(topViewers = topViewers) }
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun StatTiles(summary: AnalyticsSummary) {
    val spacing = LocalSpacing.current

    FlowRow(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        StatTile(Res.string.analytics_stat_messages, summary.totalMessages.toString())
        StatTile(Res.string.analytics_stat_followers, summary.newFollowers.toString())
        StatTile(Res.string.analytics_stat_subscribers, summary.newSubscribers.toString())
        StatTile(Res.string.analytics_stat_bits, summary.bitsCheered.toString())
        StatTile(Res.string.analytics_stat_commands, summary.commandsRun.toString())
        StatTile(Res.string.analytics_stat_redemptions, summary.redemptionsCount.toString())
        StatTile(Res.string.analytics_stat_song_requests, summary.songRequests.toString())
        StatTile(Res.string.analytics_stat_currency_earned, summary.currencyEarnedTotal.toString())
        StatTile(Res.string.analytics_stat_currency_spent, summary.currencySpentTotal.toString())
        StatTile(Res.string.analytics_stat_peak_viewers, peakLabel(summary.peakViewers))
    }
}

@Composable
private fun DailyTrendsSection(daily: List<DailyMetricRow>) {
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
        Text(
            text = stringResource(Res.string.analytics_daily_title),
            style = typography.sm.copy(fontWeight = FontWeight.SemiBold),
            color = tokens.cardForeground,
        )

        if (daily.isEmpty()) {
            Text(
                text = stringResource(Res.string.analytics_daily_empty),
                style = typography.sm,
                color = tokens.mutedForeground,
            )
        } else {
            // Header row
            DailyRow(
                date = "Date",
                chatters = "Chatters",
                messages = "Messages",
                followers = "Followers",
                peak = "Peak",
                isHeader = true,
            )
            daily.reversed().forEach { row: DailyMetricRow ->
                DailyRow(
                    date = row.activityDate.take(10),
                    chatters = row.uniqueChatters.toString(),
                    messages = row.totalMessages.toString(),
                    followers = row.newFollowers.toString(),
                    peak = row.peakViewers?.toString() ?: "—",
                    isHeader = false,
                )
            }
        }
    }
}

@Composable
private fun DailyRow(
    date: String,
    chatters: String,
    messages: String,
    followers: String,
    peak: String,
    isHeader: Boolean,
) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    val style = if (isHeader) typography.xs.copy(fontWeight = FontWeight.SemiBold) else typography.xs
    val color = if (isHeader) tokens.mutedForeground else tokens.cardForeground

    Row(modifier = Modifier.fillMaxWidth()) {
        Text(text = date, style = style, color = color, modifier = Modifier.weight(2f))
        Text(text = chatters, style = style, color = color, modifier = Modifier.weight(1.2f), textAlign = TextAlign.End)
        Text(text = messages, style = style, color = color, modifier = Modifier.weight(1.5f), textAlign = TextAlign.End)
        Text(text = followers, style = style, color = color, modifier = Modifier.weight(1.2f), textAlign = TextAlign.End)
        Text(text = peak, style = style, color = color, modifier = Modifier.weight(1f), textAlign = TextAlign.End)
    }
}

@Composable
private fun TopViewersSection(topViewers: List<TopViewerEntry>) {
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
        Text(
            text = stringResource(Res.string.analytics_top_viewers_title),
            style = typography.sm.copy(fontWeight = FontWeight.SemiBold),
            color = tokens.cardForeground,
        )

        if (topViewers.isEmpty()) {
            Text(
                text = stringResource(Res.string.analytics_top_viewers_empty),
                style = typography.sm,
                color = tokens.mutedForeground,
            )
        } else {
            topViewers.forEachIndexed { index: Int, entry: TopViewerEntry ->
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(spacing.s3),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Text(
                        text = stringResource(Res.string.analytics_top_viewers_rank, index + 1),
                        style = typography.xs.copy(fontWeight = FontWeight.SemiBold),
                        color = tokens.mutedForeground,
                        modifier = Modifier.width(spacing.s8),
                    )
                    Text(
                        text = entry.displayName ?: entry.viewerUserId,
                        style = typography.sm,
                        color = tokens.cardForeground,
                        modifier = Modifier.weight(1f),
                    )
                    Spacer(modifier = Modifier.width(spacing.s2))
                    Text(
                        text = entry.metricValue.toString(),
                        style = typography.sm.copy(fontWeight = FontWeight.SemiBold),
                        color = tokens.cardForeground,
                    )
                }
            }
        }
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
            // One node for screen readers: "Messages: 1200" rather than two disconnected texts.
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
                text = stringResource(Res.string.analytics_error, detail),
                style = typography.base,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.analytics_retry)) }
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

/** Render peak viewers, or the placeholder when the window has no recorded days. */
@Composable
private fun peakLabel(peakViewers: Int?): String =
    peakViewers?.toString() ?: stringResource(Res.string.analytics_peak_offline)
