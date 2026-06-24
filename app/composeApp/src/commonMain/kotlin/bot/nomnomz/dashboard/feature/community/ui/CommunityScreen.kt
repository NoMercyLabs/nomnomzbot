// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.community.ui

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
import bot.nomnomz.dashboard.core.network.CommunityMember
import bot.nomnomz.dashboard.feature.community.state.CommunityController
import bot.nomnomz.dashboard.feature.community.state.CommunityState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.community_banned
import nomnomzbot.composeapp.generated.resources.community_empty
import nomnomzbot.composeapp.generated.resources.community_error
import nomnomzbot.composeapp.generated.resources.community_loading
import nomnomzbot.composeapp.generated.resources.community_retry
import nomnomzbot.composeapp.generated.resources.community_row_description
import nomnomzbot.composeapp.generated.resources.community_trust_moderator
import nomnomzbot.composeapp.generated.resources.community_trust_subscriber
import nomnomzbot.composeapp.generated.resources.community_trust_viewer
import nomnomzbot.composeapp.generated.resources.community_trust_vip
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource

// The Community page (frontend-ia.md §3): the channel's viewers — every member is real data from
// [CommunityController] (the backend sources it from the Twitch API + chat history). The screen is a pure
// projection of the controller's state; it loads on first composition and offers a retry on failure.
@Composable
fun CommunityScreen(controller: CommunityController) {
    val state: CommunityState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    LaunchedEffect(Unit) { controller.load() }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: CommunityState = state) {
            is CommunityState.Loading -> CenteredMessage(stringResource(Res.string.community_loading))
            is CommunityState.Empty -> CenteredMessage(stringResource(Res.string.community_empty))
            is CommunityState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is CommunityState.Ready -> MemberList(members = current.members)
        }
    }
}

@Composable
private fun MemberList(members: List<CommunityMember>) {
    val spacing = LocalSpacing.current

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        items(items = members, key = { member -> member.id }) { member ->
            MemberRow(member = member)
        }
    }
}

@Composable
private fun MemberRow(member: CommunityMember) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val name: String = member.displayName.takeIf { it.isNotBlank() }
        ?: member.username.takeIf { it.isNotBlank() }
        ?: member.id
    val standingLabel: String = stringResource(trustLabel(member.trustLevel))
    val rowDescription: String =
        stringResource(Res.string.community_row_description, name, standingLabel)

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
            .padding(horizontal = spacing.s4, vertical = spacing.s3)
            // One node for screen readers: "Stoney_Eagle, Moderator" rather than scattered texts.
            .clearAndSetSemantics { contentDescription = rowDescription },
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Text(
            text = name,
            style = typography.base,
            color = tokens.cardForeground,
            modifier = Modifier.weight(1f),
        )
        if (member.isBanned) {
            Badge(
                label = stringResource(Res.string.community_banned),
                background = tokens.destructive,
                foreground = tokens.destructiveForeground,
            )
        }
        Badge(
            label = standingLabel,
            background = tokens.secondary,
            foreground = tokens.secondaryForeground,
        )
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
                text = stringResource(Res.string.community_error, detail),
                style = typography.base,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.community_retry)) }
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

/** Map a backend `trustLevel` to its localized badge label, falling back to the viewer label. */
private fun trustLabel(trustLevel: String): StringResource =
    when (trustLevel.lowercase()) {
        "moderator" -> Res.string.community_trust_moderator
        "vip" -> Res.string.community_trust_vip
        "subscriber" -> Res.string.community_trust_subscriber
        else -> Res.string.community_trust_viewer
    }
