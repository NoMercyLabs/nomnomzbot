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
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import bot.nomnomz.dashboard.core.designsystem.component.BarChart
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenu
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenuItem
import bot.nomnomz.dashboard.core.designsystem.component.LineChart
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
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextAlign
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.AnalyticsSummary
import bot.nomnomz.dashboard.core.network.DailyMetricRow
import bot.nomnomz.dashboard.core.network.StreamAnalytics
import bot.nomnomz.dashboard.core.network.StreamListItem
import bot.nomnomz.dashboard.core.network.TopViewerEntry
import bot.nomnomz.dashboard.feature.analytics.state.AnalyticsController
import bot.nomnomz.dashboard.feature.analytics.state.AnalyticsState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.analytics_col_chatters
import nomnomzbot.composeapp.generated.resources.analytics_col_date
import nomnomzbot.composeapp.generated.resources.analytics_col_followers
import nomnomzbot.composeapp.generated.resources.analytics_col_messages
import nomnomzbot.composeapp.generated.resources.analytics_col_peak
import nomnomzbot.composeapp.generated.resources.analytics_chart_caption
import nomnomzbot.composeapp.generated.resources.analytics_chart_chatters
import nomnomzbot.composeapp.generated.resources.analytics_chart_empty
import nomnomzbot.composeapp.generated.resources.analytics_chart_followers
import nomnomzbot.composeapp.generated.resources.analytics_chart_messages
import nomnomzbot.composeapp.generated.resources.analytics_chart_watch_hours
import nomnomzbot.composeapp.generated.resources.analytics_charts_title
import nomnomzbot.composeapp.generated.resources.analytics_daily_empty
import nomnomzbot.composeapp.generated.resources.analytics_daily_title
import nomnomzbot.composeapp.generated.resources.analytics_error
import nomnomzbot.composeapp.generated.resources.analytics_loading
import nomnomzbot.composeapp.generated.resources.analytics_peak_offline
import nomnomzbot.composeapp.generated.resources.analytics_retry
import nomnomzbot.composeapp.generated.resources.analytics_stat_bits
import nomnomzbot.composeapp.generated.resources.analytics_stat_chatters
import nomnomzbot.composeapp.generated.resources.analytics_stat_cheers
import nomnomzbot.composeapp.generated.resources.analytics_stat_commands
import nomnomzbot.composeapp.generated.resources.analytics_stat_currency_earned
import nomnomzbot.composeapp.generated.resources.analytics_stat_currency_spent
import nomnomzbot.composeapp.generated.resources.analytics_stat_followers
import nomnomzbot.composeapp.generated.resources.analytics_stat_messages
import nomnomzbot.composeapp.generated.resources.analytics_stat_peak_viewers
import nomnomzbot.composeapp.generated.resources.analytics_stat_redemptions
import nomnomzbot.composeapp.generated.resources.analytics_stat_duration
import nomnomzbot.composeapp.generated.resources.analytics_stat_song_requests
import nomnomzbot.composeapp.generated.resources.analytics_stat_subscribers
import nomnomzbot.composeapp.generated.resources.analytics_stream_error
import nomnomzbot.composeapp.generated.resources.analytics_streams_all_time
import nomnomzbot.composeapp.generated.resources.analytics_streams_label
import nomnomzbot.composeapp.generated.resources.analytics_streams_live
import nomnomzbot.composeapp.generated.resources.analytics_streams_picker
import nomnomzbot.composeapp.generated.resources.analytics_top_viewers_empty
import nomnomzbot.composeapp.generated.resources.analytics_top_viewers_rank
import nomnomzbot.composeapp.generated.resources.analytics_top_viewers_title
import nomnomzbot.composeapp.generated.resources.shell_nav_analytics
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
                    ready = current,
                    onSelectStream = { streamId -> scope.launch { controller.selectStream(streamId) } },
                )
        }
    }
}

@Composable
private fun ReadyContent(
    ready: AnalyticsState.Ready,
    onSelectStream: (String?) -> Unit,
) {
    val spacing = LocalSpacing.current

    LazyColumn(verticalArrangement = Arrangement.spacedBy(spacing.s6)) {
        item { PageHeader(title = stringResource(Res.string.shell_nav_analytics)) }
        if (ready.streams.isNotEmpty()) {
            item {
                StreamPicker(
                    streams = ready.streams,
                    selectedStreamId = ready.selectedStreamId,
                    onSelectStream = onSelectStream,
                )
            }
        }
        ready.streamError?.let { detail ->
            item {
                Text(
                    text = stringResource(Res.string.analytics_stream_error, detail),
                    style = LocalTypography.current.sm,
                    color = LocalTokens.current.destructive,
                )
            }
        }
        // The stat tiles reflect the selection: a chosen stream shows its own folded numbers; otherwise the
        // all-time summary over the trailing window.
        item {
            if (ready.streamDetail != null && ready.selectedStreamId != null) {
                StreamStatTiles(detail = ready.streamDetail)
            } else {
                SummaryStatTiles(summary = ready.summary)
            }
        }
        // The daily trend charts + top viewers stay on the trailing window regardless of the stream selection.
        item { ChartsSection(daily = ready.daily) }
        item { DailyTrendsSection(daily = ready.daily) }
        item { TopViewersSection(topViewers = ready.topViewers) }
    }
}

// The per-stream selector: All-time + one entry per recorded stream. Selecting swaps the stat tiles to that
// stream's own numbers (or back to all-time). A closed dropdown keeps the picker compact next to the tiles.
@Composable
private fun StreamPicker(
    streams: List<StreamListItem>,
    selectedStreamId: String?,
    onSelectStream: (String?) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var expanded: Boolean by remember { mutableStateOf(false) }
    val allTimeLabel: String = stringResource(Res.string.analytics_streams_all_time)
    val selected: StreamListItem? = streams.firstOrNull { it.streamId == selectedStreamId }
    val activeLabel: String = selected?.let { streamLabel(it) } ?: allTimeLabel
    // Resolved here (a @Composable call) so the semantics lambda only reads the plain string.
    val pickerDesc: String = pickerDescription(activeLabel)

    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
        Text(
            text = stringResource(Res.string.analytics_streams_label),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
        Box {
            TextButton(
                onClick = { expanded = true },
                modifier = Modifier.semantics {
                    contentDescription = pickerDesc
                },
            ) {
                Text(text = activeLabel, style = typography.sm, color = tokens.primary, maxLines = 1)
            }
            DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
                DropdownMenuItem(
                    text = { Text(text = allTimeLabel, style = typography.sm, color = tokens.popoverForeground) },
                    onClick = {
                        expanded = false
                        onSelectStream(null)
                    },
                )
                streams.forEach { stream ->
                    val label: String = streamLabel(stream)
                    DropdownMenuItem(
                        text = { Text(text = label, style = typography.sm, color = tokens.popoverForeground) },
                        onClick = {
                            expanded = false
                            onSelectStream(stream.streamId)
                        },
                    )
                }
            }
        }
    }
}

@Composable
private fun SummaryStatTiles(summary: AnalyticsSummary) {
    StatGrid(
        listOf(
            StatTileData(stringResource(Res.string.analytics_stat_messages), summary.totalMessages.toString()),
            StatTileData(stringResource(Res.string.analytics_stat_followers), summary.newFollowers.toString()),
            StatTileData(stringResource(Res.string.analytics_stat_subscribers), summary.newSubscribers.toString()),
            StatTileData(stringResource(Res.string.analytics_stat_bits), summary.bitsCheered.toString()),
            StatTileData(stringResource(Res.string.analytics_stat_commands), summary.commandsRun.toString()),
            StatTileData(stringResource(Res.string.analytics_stat_redemptions), summary.redemptionsCount.toString()),
            StatTileData(stringResource(Res.string.analytics_stat_song_requests), summary.songRequests.toString()),
            StatTileData(stringResource(Res.string.analytics_stat_currency_earned), summary.currencyEarnedTotal.toString()),
            StatTileData(stringResource(Res.string.analytics_stat_currency_spent), summary.currencySpentTotal.toString()),
            StatTileData(stringResource(Res.string.analytics_stat_peak_viewers), peakLabel(summary.peakViewers)),
        )
    )
}

@Composable
private fun StreamStatTiles(detail: StreamAnalytics) {
    StatGrid(
        listOf(
            StatTileData(stringResource(Res.string.analytics_stat_messages), detail.totalMessages.toString()),
            StatTileData(stringResource(Res.string.analytics_stat_chatters), detail.uniqueChatters.toString()),
            StatTileData(stringResource(Res.string.analytics_stat_followers), detail.newFollowers.toString()),
            StatTileData(stringResource(Res.string.analytics_stat_subscribers), detail.newSubscribers.toString()),
            StatTileData(stringResource(Res.string.analytics_stat_cheers), detail.cheersCount.toString()),
            StatTileData(stringResource(Res.string.analytics_stat_commands), detail.commandsRun.toString()),
            StatTileData(stringResource(Res.string.analytics_stat_redemptions), detail.redemptionsCount.toString()),
            StatTileData(stringResource(Res.string.analytics_stat_peak_viewers), peakLabel(detail.peakViewers)),
            StatTileData(stringResource(Res.string.analytics_stat_duration), durationLabel(detail.durationSeconds)),
        )
    )
}

/** A resolved stat tile (label already localized, value already formatted). */
private data class StatTileData(val label: String, val value: String)

// A balanced stat-card grid (designer review: "the metrics row should balance itself"): tiles are laid out in
// equal-width columns and the final row is padded so every card keeps the same width instead of a ragged wrap.
@Composable
private fun StatGrid(tiles: List<StatTileData>, columns: Int = 5) {
    val spacing = LocalSpacing.current

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
        tiles.chunked(columns).forEach { rowTiles ->
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(spacing.s3),
            ) {
                rowTiles.forEach { tile ->
                    StatTile(label = tile.label, value = tile.value, modifier = Modifier.weight(1f))
                }
                // Pad the last row so trailing tiles stay the same width as a full row's.
                repeat(columns - rowTiles.size) { Spacer(modifier = Modifier.weight(1f)) }
            }
        }
    }
}

// The daily trend charts — lightweight line/bar over the trailing window (frontend-ia.md §3). Each chart reads
// one metric off the daily series; an empty series shows a single placeholder rather than four empty frames.
@Composable
private fun ChartsSection(daily: List<DailyMetricRow>) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        modifier = Modifier.fillMaxWidth(),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Text(
            text = stringResource(Res.string.analytics_charts_title),
            style = typography.sm.copy(fontWeight = FontWeight.SemiBold),
            color = tokens.cardForeground,
        )

        if (daily.isEmpty()) {
            Text(
                text = stringResource(Res.string.analytics_chart_empty),
                style = typography.sm,
                color = tokens.mutedForeground,
            )
            return
        }

        val ordered: List<DailyMetricRow> = daily.sortedBy { it.activityDate }
        val from: String = ordered.first().activityDate.take(10)
        val to: String = ordered.last().activityDate.take(10)

        ChartCard(
            title = stringResource(Res.string.analytics_chart_messages),
            values = ordered.map { it.totalMessages.toFloat() },
            fromDate = from,
            toDate = to,
            color = tokens.sidebarPrimary,
            bars = false,
        )
        ChartCard(
            title = stringResource(Res.string.analytics_chart_followers),
            values = ordered.map { it.newFollowers.toFloat() },
            fromDate = from,
            toDate = to,
            color = tokens.success,
            bars = true,
        )
        ChartCard(
            title = stringResource(Res.string.analytics_chart_watch_hours),
            values = ordered.map { it.totalWatchSeconds.toFloat() / 3600f },
            fromDate = from,
            toDate = to,
            color = tokens.primary,
            bars = false,
        )
        ChartCard(
            title = stringResource(Res.string.analytics_chart_chatters),
            values = ordered.map { it.uniqueChatters.toFloat() },
            fromDate = from,
            toDate = to,
            color = tokens.sidebarPrimary,
            bars = true,
        )
    }
}

@Composable
private fun ChartCard(
    title: String,
    values: List<Float>,
    fromDate: String,
    toDate: String,
    color: androidx.compose.ui.graphics.Color,
    bars: Boolean,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val peak: Float = values.maxOrNull() ?: 0f
    val peakLabel: String = if (peak >= 10f) peak.toInt().toString() else ((peak * 10).toInt() / 10.0).toString()

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.fillMaxWidth().padding(spacing.s4),
            verticalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            Text(text = title, style = typography.sm, color = tokens.cardForeground)
            if (bars) {
                BarChart(values = values, barColor = color)
            } else {
                LineChart(values = values, lineColor = color)
            }
            Text(
                text = stringResource(Res.string.analytics_chart_caption, fromDate, toDate, peakLabel),
                style = typography.xs,
                color = tokens.mutedForeground,
            )
        }
    }
}

@Composable
private fun DailyTrendsSection(daily: List<DailyMetricRow>) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        modifier = Modifier.fillMaxWidth(),
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
            Card(modifier = Modifier.fillMaxWidth()) {
                // Header row
                DailyRow(
                    date = stringResource(Res.string.analytics_col_date),
                    chatters = stringResource(Res.string.analytics_col_chatters),
                    messages = stringResource(Res.string.analytics_col_messages),
                    followers = stringResource(Res.string.analytics_col_followers),
                    peak = stringResource(Res.string.analytics_col_peak),
                    isHeader = true,
                )
                daily.reversed().forEach { row: DailyMetricRow ->
                    Separator()
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
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val style = if (isHeader) typography.xs.copy(fontWeight = FontWeight.SemiBold) else typography.xs
    val color = if (isHeader) tokens.mutedForeground else tokens.cardForeground

    Row(modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3)) {
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
        modifier = Modifier.fillMaxWidth(),
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
            Card(modifier = Modifier.fillMaxWidth()) {
                topViewers.forEachIndexed { index: Int, entry: TopViewerEntry ->
                    if (index > 0) {
                        Separator()
                    }
                    Row(
                        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
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
}

@Composable
private fun StatTile(label: String, value: String, modifier: Modifier = Modifier) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        modifier = modifier
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
            .padding(spacing.s4)
            // One node for screen readers: "Messages: 1200" rather than two disconnected texts.
            .clearAndSetSemantics { contentDescription = "$label: $value" },
        verticalArrangement = Arrangement.spacedBy(spacing.s1),
    ) {
        Text(text = value, style = typography.xl2, color = tokens.cardForeground, maxLines = 1)
        Text(text = label, style = typography.sm, color = tokens.mutedForeground, maxLines = 1)
    }
}

/** A stream's picker label: its title (or the game, or the start date) — the streamer's own words first. */
private fun streamLabel(stream: StreamListItem): String =
    stream.title?.takeIf { it.isNotBlank() }
        ?: stream.gameName?.takeIf { it.isNotBlank() }
        ?: stream.startedAt.take(10)

/** Whole-hours/minutes duration label, or the live marker while the stream has no end. */
@Composable
private fun durationLabel(seconds: Long?): String =
    if (seconds == null) {
        stringResource(Res.string.analytics_streams_live)
    } else {
        val hours: Long = seconds / 3600
        val minutes: Long = (seconds % 3600) / 60
        if (hours > 0) "${hours}h ${minutes}m" else "${minutes}m"
    }

/** The stream-picker trigger's screen-reader description, resolved outside the semantics lambda. */
@Composable
private fun pickerDescription(activeLabel: String): String =
    stringResource(Res.string.analytics_streams_picker, activeLabel)

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
