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
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenu
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenuItem
import bot.nomnomz.dashboard.core.designsystem.component.OutlinedButton
import bot.nomnomz.dashboard.core.designsystem.component.Spinner
import bot.nomnomz.dashboard.core.designsystem.component.Switch
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.ChannelAppearance
import bot.nomnomz.dashboard.core.network.PronounOption
import bot.nomnomz.dashboard.core.network.UserActivity
import bot.nomnomz.dashboard.core.network.UserProfile
import bot.nomnomz.dashboard.core.network.ViewerAnalyticsProfile
import bot.nomnomz.dashboard.core.network.WatchStreak
import bot.nomnomz.dashboard.feature.participant.state.MeState
import bot.nomnomz.dashboard.feature.participant.state.ParticipantController
import bot.nomnomz.dashboard.feature.shell.nav.ParticipantStanding
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.participant_loading
import nomnomzbot.composeapp.generated.resources.participant_me_analytics_opt_in
import nomnomzbot.composeapp.generated.resources.participant_me_analytics_opt_out
import nomnomzbot.composeapp.generated.resources.participant_me_analytics_opted_in_desc
import nomnomzbot.composeapp.generated.resources.participant_me_analytics_opted_out_desc
import nomnomzbot.composeapp.generated.resources.participant_me_analytics_title
import nomnomzbot.composeapp.generated.resources.participant_me_channels_empty
import nomnomzbot.composeapp.generated.resources.participant_me_channels_row
import nomnomzbot.composeapp.generated.resources.participant_me_channels_title
import nomnomzbot.composeapp.generated.resources.participant_me_profile_title
import nomnomzbot.composeapp.generated.resources.participant_me_pronoun_none
import nomnomzbot.composeapp.generated.resources.participant_me_streak_current
import nomnomzbot.composeapp.generated.resources.participant_me_streak_max
import nomnomzbot.composeapp.generated.resources.participant_me_streak_title
import nomnomzbot.composeapp.generated.resources.participant_stat_channels
import nomnomzbot.composeapp.generated.resources.participant_stat_commands
import nomnomzbot.composeapp.generated.resources.participant_stat_messages
import nomnomzbot.composeapp.generated.resources.participant_stat_watch_hours
import org.jetbrains.compose.resources.stringResource

// Me: the caller's OWN data — their profile (display name, standing, pronouns), their activity summary, and their
// participation footprint (the channels they appear in). Profile is self-service: the pronoun picker saves to the
// backend via PUT /users/{id}/profile using the full catalogue from GET /system/pronouns.
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
            is MeState.Ready ->
                Ready(
                    state = current,
                    onPronounSelected = { id -> scope.launch { controller.updatePronoun(id) } },
                    onSetAnalyticsOptOut = { opted -> scope.launch { controller.setAnalyticsOptOut(opted) } },
                )
        }
    }
}

@Composable
private fun Ready(
    state: MeState.Ready,
    onPronounSelected: (Int?) -> Unit,
    onSetAnalyticsOptOut: (Boolean) -> Unit,
) {
    val spacing = LocalSpacing.current

    Column(
        modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        ProfileCard(
            profile = state.profile,
            standing = state.standing,
            pronouns = state.pronouns,
            saving = state.profileSaving,
            onPronounSelected = onPronounSelected,
        )
        state.profileError?.let { err ->
            val tokens = LocalTokens.current
            val typography = LocalTypography.current
            Text(text = err, style = typography.xs, color = tokens.destructive)
        }
        ActivityTiles(activity = state.activity)
        state.watchStreak?.let { streak -> WatchStreakCard(streak = streak) }
        ChannelsCard(channels = state.channels)
        AnalyticsPrivacyCard(
            profile = state.analyticsProfile,
            saving = state.analyticsOptOutSaving,
            error = state.analyticsOptOutError,
            onToggle = onSetAnalyticsOptOut,
        )
    }
}

@Composable
private fun ProfileCard(
    profile: UserProfile,
    standing: ParticipantStanding,
    pronouns: List<PronounOption>,
    saving: Boolean,
    onPronounSelected: (Int?) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    var expanded: Boolean by remember { mutableStateOf(false) }

    val name: String = profile.displayName.takeIf { it.isNotBlank() } ?: profile.username
    val currentPronounName: String =
        profile.pronoun?.takeIf { it.isNotBlank() } ?: stringResource(Res.string.participant_me_pronoun_none)

    SectionCard(title = stringResource(Res.string.participant_me_profile_title)) {
        Text(text = name, style = typography.base, color = tokens.cardForeground)
        Row(
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            ParticipantBadge(label = stringResource(standing.labelResource()))
            if (saving) {
                Spinner(modifier = Modifier.padding(spacing.s1))
            } else if (pronouns.isNotEmpty()) {
                Box {
                    OutlinedButton(onClick = { expanded = true }) {
                        Text(text = currentPronounName, style = typography.sm)
                    }
                    DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
                        DropdownMenuItem(
                            text = {
                                Text(
                                    stringResource(Res.string.participant_me_pronoun_none),
                                    style = typography.sm,
                                )
                            },
                            onClick = {
                                expanded = false
                                onPronounSelected(null)
                            },
                        )
                        pronouns.forEach { option ->
                            DropdownMenuItem(
                                text = { Text(option.name, style = typography.sm) },
                                onClick = {
                                    expanded = false
                                    onPronounSelected(option.id)
                                },
                            )
                        }
                    }
                }
            } else {
                Text(text = currentPronounName, style = typography.sm, color = tokens.mutedForeground)
            }
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

@Composable
private fun WatchStreakCard(streak: WatchStreak) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    val spacing = LocalSpacing.current

    SectionCard(title = stringResource(Res.string.participant_me_streak_title)) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(spacing.s6),
        ) {
            Column {
                Text(
                    text = streak.currentStreak.toString(),
                    style = typography.xl2,
                    color = tokens.cardForeground,
                )
                Text(
                    text = stringResource(Res.string.participant_me_streak_current),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                )
            }
            Column {
                Text(
                    text = streak.maxStreak.toString(),
                    style = typography.xl2,
                    color = tokens.cardForeground,
                )
                Text(
                    text = stringResource(Res.string.participant_me_streak_max),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                )
            }
        }
    }
}

@Composable
private fun AnalyticsPrivacyCard(
    profile: ViewerAnalyticsProfile?,
    saving: Boolean,
    error: String?,
    onToggle: (Boolean) -> Unit,
) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    if (profile == null) return

    val optedOut: Boolean = profile.isAnalyticsOptedOut
    val description: String =
        stringResource(
            if (optedOut) Res.string.participant_me_analytics_opted_out_desc
            else Res.string.participant_me_analytics_opted_in_desc
        )
    val actionLabel: String =
        stringResource(
            if (optedOut) Res.string.participant_me_analytics_opt_in
            else Res.string.participant_me_analytics_opt_out
        )

    SectionCard(title = stringResource(Res.string.participant_me_analytics_title)) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Column(modifier = Modifier.weight(1f)) {
                Text(text = actionLabel, style = typography.sm, color = tokens.cardForeground)
                Text(text = description, style = typography.xs, color = tokens.mutedForeground)
            }
            if (saving) {
                Spinner()
            } else {
                Switch(checked = !optedOut, onCheckedChange = { checked -> onToggle(!checked) })
            }
        }
        error?.let { err ->
            Text(text = err, style = typography.xs, color = tokens.destructive)
        }
    }
}
