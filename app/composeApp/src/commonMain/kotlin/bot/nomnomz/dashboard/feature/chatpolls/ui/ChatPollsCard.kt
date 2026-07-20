// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.chatpolls.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.style.TextOverflow
import bot.nomnomz.dashboard.core.designsystem.component.Badge
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.ChatPoll
import bot.nomnomz.dashboard.core.network.ChatPollOption
import bot.nomnomz.dashboard.feature.chatpolls.state.ChatPollsController
import bot.nomnomz.dashboard.feature.chatpolls.state.ChatPollsState
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.chat_poll_close
import nomnomzbot.composeapp.generated.resources.chat_poll_history_title
import nomnomzbot.composeapp.generated.resources.chat_poll_idle_hint
import nomnomzbot.composeapp.generated.resources.chat_poll_open_tag
import nomnomzbot.composeapp.generated.resources.chat_poll_subtitle
import nomnomzbot.composeapp.generated.resources.chat_poll_title
import nomnomzbot.composeapp.generated.resources.chat_poll_total_votes
import nomnomzbot.composeapp.generated.resources.chat_poll_votes
import org.jetbrains.compose.resources.stringResource

// Poll interval for the open poll's live tallies (poll the GET so the bars move as chatters type numbers).
private const val PollIntervalMillis: Long = 4_000L

// The "Chat poll" surface — bot-run polls where viewers vote by typing an option number in ANY platform's chat.
// Labeled "Chat poll" to distinguish it from the Twitch-native live-ops poll on the same page. It renders the
// open poll (live per-option bars + a Close button), a new-poll form (question + 2–10 options + optional auto-
// close + announce), and a short closed-poll history. Self-contained: drives its own [ChatPollsController], which
// re-loads on a tick so the tallies stay live.
@Composable
fun ChatPollsCard(controller: ChatPollsController, modifier: Modifier = Modifier) {
    val state: ChatPollsState by controller.state.collectAsState()
    val scope = rememberCoroutineScope()
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    // Load on first composition and poll while mounted so an open poll's bars move without a manual refresh.
    LaunchedEffect(Unit) {
        controller.load()
        while (true) {
            delay(PollIntervalMillis)
            controller.load()
        }
    }

    Column(
        modifier = modifier.fillMaxWidth(),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(spacing.s2),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = stringResource(Res.string.chat_poll_title),
                    style = typography.base,
                    color = tokens.cardForeground,
                    maxLines = 1,
                )
                Text(
                    text = stringResource(Res.string.chat_poll_subtitle),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                )
            }
        }

        when (val current: ChatPollsState = state) {
            is ChatPollsState.Loading -> Unit
            is ChatPollsState.Error ->
                Text(text = current.detail, style = typography.sm, color = tokens.destructive)
            is ChatPollsState.Ready -> {
                current.actionError?.let { detail ->
                    Text(text = detail, style = typography.sm, color = tokens.destructive)
                }
                // Starting a poll lives in the single "Start poll" modal on Home — this card is the live view of
                // the running chat poll (tallies + close) and the recent history, not a second start form.
                if (current.openPoll != null) {
                    OpenPollCard(
                        poll = current.openPoll,
                        onClose = { scope.launch { controller.close(current.openPoll.id) } },
                    )
                } else {
                    Text(
                        text = stringResource(Res.string.chat_poll_idle_hint),
                        style = typography.xs,
                        color = tokens.mutedForeground,
                    )
                }
                if (current.history.isNotEmpty()) {
                    HistorySection(history = current.history)
                }
            }
        }
    }
}

// The open poll: question + per-option live bars (fill proportional to the leading option), total votes, Close.
@Composable
private fun OpenPollCard(poll: ChatPoll, onClose: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val maxVotes: Int = poll.options.maxOfOrNull { it.votes }?.coerceAtLeast(1) ?: 1

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
            .padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(spacing.s2),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(
                text = poll.question,
                style = typography.base,
                color = tokens.cardForeground,
                modifier = Modifier.weight(1f),
            )
            Badge(selected = true) { Text(stringResource(Res.string.chat_poll_open_tag), maxLines = 1) }
        }
        poll.options.forEach { option -> PollOptionBar(option = option, maxVotes = maxVotes) }
        Text(
            text = stringResource(Res.string.chat_poll_total_votes, poll.totalVotes),
            style = typography.xs,
            color = tokens.mutedForeground,
        )
        Button(onClick = onClose) { Text(stringResource(Res.string.chat_poll_close)) }
    }
}

// One option's live bar: the number viewers type + label, a proportional fill, and the vote count.
@Composable
private fun PollOptionBar(option: ChatPollOption, maxVotes: Int) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val fraction: Float = (option.votes.toFloat() / maxVotes.toFloat()).coerceIn(0f, 1f)

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(spacing.s2),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(
                text = "${option.index}. ${option.label}",
                style = typography.sm,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.weight(1f),
            )
            Text(
                text = stringResource(Res.string.chat_poll_votes, option.votes),
                style = typography.xs,
                color = tokens.mutedForeground,
                maxLines = 1,
            )
        }
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .height(spacing.s2)
                .clip(RoundedCornerShape(tokens.radius.sm))
                .background(tokens.muted),
        ) {
            Box(
                modifier = Modifier
                    .fillMaxWidth(fraction)
                    .height(spacing.s2)
                    .clip(RoundedCornerShape(tokens.radius.sm))
                    .background(tokens.primary),
            )
        }
    }
}

// A compact list of the most recent closed polls: the question + its winning option (highest votes).
@Composable
private fun HistorySection(history: List<ChatPoll>) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
        Text(
            text = stringResource(Res.string.chat_poll_history_title),
            style = typography.xs,
            color = tokens.mutedForeground,
        )
        history.take(5).forEach { poll ->
            val winner: ChatPollOption? = poll.options.maxByOrNull { it.votes }
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .clip(RoundedCornerShape(tokens.radius.md))
                    .background(tokens.card)
                    .padding(horizontal = spacing.s3, vertical = spacing.s2),
                verticalArrangement = Arrangement.spacedBy(spacing.s1),
            ) {
                Text(
                    text = poll.question,
                    style = typography.sm,
                    color = tokens.cardForeground,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                if (winner != null) {
                    Text(
                        text = stringResource(
                            Res.string.chat_poll_votes,
                            winner.votes,
                        ) + " · " + winner.label,
                        style = typography.xs,
                        color = tokens.mutedForeground,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                }
                Separator()
            }
        }
    }
}
