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
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.ChannelAppearance
import bot.nomnomz.dashboard.core.network.UserActivity
import bot.nomnomz.dashboard.core.network.UserProfile
import bot.nomnomz.dashboard.feature.participant.state.MeState
import bot.nomnomz.dashboard.feature.participant.state.ParticipantController
import bot.nomnomz.dashboard.feature.shell.nav.ParticipantStanding
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.participant_loading
import nomnomzbot.composeapp.generated.resources.participant_me_channels_empty
import nomnomzbot.composeapp.generated.resources.participant_me_channels_row
import nomnomzbot.composeapp.generated.resources.participant_me_channels_title
import nomnomzbot.composeapp.generated.resources.participant_me_profile_title
import nomnomzbot.composeapp.generated.resources.participant_me_pronoun_none
import nomnomzbot.composeapp.generated.resources.participant_me_pronoun_readonly
import nomnomzbot.composeapp.generated.resources.participant_stat_channels
import nomnomzbot.composeapp.generated.resources.participant_stat_commands
import nomnomzbot.composeapp.generated.resources.participant_stat_messages
import nomnomzbot.composeapp.generated.resources.participant_stat_watch_hours
import org.jetbrains.compose.resources.stringResource

// Me: the caller's OWN data — their profile (display name, standing, pronouns), their activity summary, and their
// participation footprint (the channels they appear in). Self-service + read-only: pronouns are READ-ONLY here
// because the backend's profile update accepts no pronoun field (flagged gap), so the screen shows the pronoun and
// states it can't be edited yet rather than offering a broken editor.
@Composable
fun MeScreen(controller: ParticipantController) {
    val state: MeState by controller.me.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    LaunchedEffect(Unit) { controller.loadMe() }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: MeState = state) {
            is MeState.Loading -> ParticipantMessage(stringResource(Res.string.participant_loading))
            is MeState.Error ->
                ParticipantError(detail = current.detail, onRetry = { scope.launch { controller.loadMe() } })
            is MeState.Ready -> Ready(state = current)
        }
    }
}

@Composable
private fun Ready(state: MeState.Ready) {
    val spacing = LocalSpacing.current

    Column(
        modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        ProfileCard(profile = state.profile, standing = state.standing)
        ActivityTiles(activity = state.activity)
        ChannelsCard(channels = state.channels)
    }
}

@Composable
private fun ProfileCard(profile: UserProfile, standing: ParticipantStanding) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val name: String = profile.displayName.takeIf { it.isNotBlank() } ?: profile.username
    val pronoun: String =
        profile.pronoun?.takeIf { it.isNotBlank() } ?: stringResource(Res.string.participant_me_pronoun_none)

    SectionCard(title = stringResource(Res.string.participant_me_profile_title)) {
        Text(text = name, style = typography.base, color = tokens.cardForeground)
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            ParticipantBadge(label = stringResource(standing.labelResource()))
            Text(text = pronoun, style = typography.sm, color = tokens.mutedForeground)
        }
        // Pronouns are read-only (no backend write) — say so plainly rather than offer a broken editor.
        Text(
            text = stringResource(Res.string.participant_me_pronoun_readonly),
            style = typography.xs,
            color = tokens.mutedForeground,
        )
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

@Composable
private fun ChannelsCard(channels: List<ChannelAppearance>) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    SectionCard(title = stringResource(Res.string.participant_me_channels_title)) {
        if (channels.isEmpty()) {
            Text(
                text = stringResource(Res.string.participant_me_channels_empty),
                style = typography.sm,
                color = tokens.mutedForeground,
            )
        } else {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
                channels.forEach { appearance ->
                    Text(
                        text =
                            stringResource(
                                Res.string.participant_me_channels_row,
                                appearance.channelName,
                                appearance.messages,
                                appearance.watchTime,
                            ),
                        style = typography.sm,
                        color = tokens.cardForeground,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                }
            }
        }
    }
}
