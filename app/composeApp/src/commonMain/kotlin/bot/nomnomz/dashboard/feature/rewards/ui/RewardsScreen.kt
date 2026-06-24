// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.rewards.ui

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
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.text.style.TextAlign
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.RewardSummary
import bot.nomnomz.dashboard.feature.rewards.state.RewardsController
import bot.nomnomz.dashboard.feature.rewards.state.RewardsState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.rewards_cost
import nomnomzbot.composeapp.generated.resources.rewards_disabled
import nomnomzbot.composeapp.generated.resources.rewards_empty
import nomnomzbot.composeapp.generated.resources.rewards_enabled
import nomnomzbot.composeapp.generated.resources.rewards_error
import nomnomzbot.composeapp.generated.resources.rewards_loading
import nomnomzbot.composeapp.generated.resources.rewards_retry
import nomnomzbot.composeapp.generated.resources.rewards_row_description
import org.jetbrains.compose.resources.stringResource

// The Rewards page (frontend-ia.md §3): the channel's channel-point rewards — every reward is real data from
// [RewardsController] (the backend sources it from Twitch's Helix Custom Rewards endpoint). The screen is a pure
// projection of the controller's state; it loads on first composition, offers a retry on failure, and is
// read-only this slice (no create/edit/delete).
@Composable
fun RewardsScreen(controller: RewardsController) {
    val state: RewardsState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    LaunchedEffect(Unit) { controller.load() }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: RewardsState = state) {
            is RewardsState.Loading -> CenteredMessage(stringResource(Res.string.rewards_loading))
            is RewardsState.Empty -> CenteredMessage(stringResource(Res.string.rewards_empty))
            is RewardsState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is RewardsState.Ready -> RewardList(rewards = current.rewards)
        }
    }
}

@Composable
private fun RewardList(rewards: List<RewardSummary>) {
    val spacing = LocalSpacing.current

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        items(items = rewards, key = { reward -> reward.id }) { reward ->
            RewardRow(reward = reward)
        }
    }
}

@Composable
private fun RewardRow(reward: RewardSummary) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val costLabel: String = stringResource(Res.string.rewards_cost, reward.cost)
    val statusLabel: String =
        if (reward.isEnabled) stringResource(Res.string.rewards_enabled)
        else stringResource(Res.string.rewards_disabled)
    val rowDescription: String =
        stringResource(Res.string.rewards_row_description, reward.title, costLabel, statusLabel)

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
            .padding(horizontal = spacing.s4, vertical = spacing.s3)
            // One node for screen readers: "Hydrate!, 500 points, Enabled" rather than scattered texts.
            .clearAndSetSemantics { contentDescription = rowDescription },
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Column(
            modifier = Modifier.weight(1f),
            verticalArrangement = Arrangement.spacedBy(spacing.s1),
        ) {
            Text(
                text = reward.title,
                style = typography.base,
                color = tokens.cardForeground,
            )
            Text(
                text = costLabel,
                style = typography.xs,
                color = tokens.mutedForeground,
            )
        }
        if (reward.isEnabled) {
            Badge(
                label = statusLabel,
                background = tokens.primary,
                foreground = tokens.primaryForeground,
            )
        } else {
            Badge(
                label = statusLabel,
                background = tokens.secondary,
                foreground = tokens.secondaryForeground,
            )
        }
    }
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
        Text(text = label, style = typography.xs, color = foreground)
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
                text = stringResource(Res.string.rewards_error, detail),
                style = typography.base,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.rewards_retry)) }
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
