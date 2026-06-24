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
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.DashboardStats
import bot.nomnomz.dashboard.core.network.UserActivity
import bot.nomnomz.dashboard.core.network.UserProfile
import bot.nomnomz.dashboard.feature.participant.state.MyChannelState
import bot.nomnomz.dashboard.feature.participant.state.ParticipantController
import bot.nomnomz.dashboard.feature.shell.nav.ParticipantStanding
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.participant_home_channel_offline
import nomnomzbot.composeapp.generated.resources.participant_home_channel_online
import nomnomzbot.composeapp.generated.resources.participant_home_channel_title
import nomnomzbot.composeapp.generated.resources.participant_home_greeting
import nomnomzbot.composeapp.generated.resources.participant_home_no_title
import nomnomzbot.composeapp.generated.resources.participant_home_playing
import nomnomzbot.composeapp.generated.resources.participant_loading
import nomnomzbot.composeapp.generated.resources.participant_stat_channels
import nomnomzbot.composeapp.generated.resources.participant_stat_commands
import nomnomzbot.composeapp.generated.resources.participant_stat_messages
import nomnomzbot.composeapp.generated.resources.participant_stat_watch_hours
import org.jetbrains.compose.resources.stringResource

// My Channel (participant home): the caller's own identity + standing + their activity footprint beside the
// channel's PUBLIC summary (live state, title, game) — all real data (the DashboardApi public summary + the user's
// own profile/stats). Read-only: a participant sees where they stand, not a management dashboard.
@Composable
fun MyChannelScreen(controller: ParticipantController) {
    val state: MyChannelState by controller.myChannel.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    LaunchedEffect(Unit) { controller.loadMyChannel() }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: MyChannelState = state) {
            is MyChannelState.Loading -> ParticipantMessage(stringResource(Res.string.participant_loading))
            is MyChannelState.Error ->
                ParticipantError(detail = current.detail, onRetry = { scope.launch { controller.loadMyChannel() } })
            is MyChannelState.Ready -> Ready(state = current)
        }
    }
}

@Composable
private fun Ready(state: MyChannelState.Ready) {
    val spacing = LocalSpacing.current

    Column(
        modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        IdentityCard(profile = state.profile, standing = state.standing)
        ChannelSummaryCard(channel = state.channel)
        ActivityTiles(activity = state.activity)
    }
}

@Composable
private fun IdentityCard(profile: UserProfile, standing: ParticipantStanding) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val name: String = profile.displayName.takeIf { it.isNotBlank() } ?: profile.username
    val standingLabel: String = stringResource(standing.labelResource())

    SectionCard(title = stringResource(Res.string.participant_home_greeting, name)) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            ParticipantBadge(label = standingLabel)
            profile.pronoun?.takeIf { it.isNotBlank() }?.let { pronoun ->
                Text(text = pronoun, style = typography.sm, color = tokens.mutedForeground)
            }
        }
    }
}

@Composable
private fun ChannelSummaryCard(channel: DashboardStats) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    SectionCard(title = stringResource(Res.string.participant_home_channel_title)) {
        Text(
            text =
                stringResource(
                    if (channel.isLive) Res.string.participant_home_channel_online
                    else Res.string.participant_home_channel_offline
                ),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
        Text(
            text = channel.streamTitle?.takeIf { it.isNotBlank() }
                ?: stringResource(Res.string.participant_home_no_title),
            style = typography.base,
            color = tokens.cardForeground,
        )
        channel.gameName?.takeIf { it.isNotBlank() }?.let { game ->
            Text(
                text = stringResource(Res.string.participant_home_playing, game),
                style = typography.sm,
                color = tokens.mutedForeground,
            )
        }
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun ActivityTiles(activity: UserActivity) {
    val spacing = LocalSpacing.current

    FlowRow(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        StatTile(stringResource(Res.string.participant_stat_messages), activity.messageCount.toString())
        StatTile(
            stringResource(Res.string.participant_stat_watch_hours),
            ((activity.watchHours * 10).toLong() / 10.0).toString(),
        )
        StatTile(stringResource(Res.string.participant_stat_commands), activity.commandsUsed.toString())
        StatTile(stringResource(Res.string.participant_stat_channels), activity.channelsCount.toString())
    }
}
