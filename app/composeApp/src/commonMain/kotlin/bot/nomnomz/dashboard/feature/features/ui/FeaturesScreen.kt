// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.features.ui

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
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
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
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.FeatureStatus
import bot.nomnomz.dashboard.feature.features.state.FeaturesController
import bot.nomnomz.dashboard.feature.features.state.FeaturesState
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.features_action_error
import nomnomzbot.composeapp.generated.resources.features_empty
import nomnomzbot.composeapp.generated.resources.features_error
import nomnomzbot.composeapp.generated.resources.features_loading
import nomnomzbot.composeapp.generated.resources.features_retry
import nomnomzbot.composeapp.generated.resources.features_enabled_at
import nomnomzbot.composeapp.generated.resources.features_scopes_label
import nomnomzbot.composeapp.generated.resources.features_subtitle
import nomnomzbot.composeapp.generated.resources.shell_nav_features
import nomnomzbot.composeapp.generated.resources.features_toggle_action
import org.jetbrains.compose.resources.stringResource

// The Features page: the channel's opt-in feature flags, all real data from [FeaturesController]. Each flag
// is shown with its key, whether it is on, when it was enabled, and which Twitch OAuth scopes it needs. The
// broadcaster can flip any flag via the switch; the backend validates and the page reloads on success.
@Composable
fun FeaturesScreen(controller: FeaturesController, role: ManagementRole?) {
    val state: FeaturesState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val manage: ManageDecision = rememberManageDecision(role, ShellRoute.Features)

    LaunchedEffect(Unit) { controller.load() }

    Column(
        modifier = Modifier.fillMaxSize().background(tokens.background).padding(spacing.s6),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        PageHeader(
            title = stringResource(Res.string.shell_nav_features),
            subtitle = stringResource(Res.string.features_subtitle),
        )

        when (val current: FeaturesState = state) {
            is FeaturesState.Loading -> CenteredMessage(stringResource(Res.string.features_loading))
            is FeaturesState.Empty -> CenteredMessage(stringResource(Res.string.features_empty))
            is FeaturesState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is FeaturesState.Ready -> {
                current.actionError?.let { detail ->
                    Text(
                        text = stringResource(Res.string.features_action_error, detail),
                        style = typography.sm,
                        color = tokens.destructiveForeground,
                        modifier = Modifier
                            .fillMaxWidth()
                            .clip(RoundedCornerShape(tokens.radius.md))
                            .background(tokens.destructive)
                            .padding(horizontal = spacing.s3, vertical = spacing.s2),
                    )
                }
                LazyColumn(
                    modifier = Modifier.fillMaxSize(),
                    verticalArrangement = Arrangement.spacedBy(spacing.s3),
                ) {
                    items(items = current.features, key = { it.featureKey }) { feature ->
                        FeatureRow(
                            feature = feature,
                            manage = manage,
                            onToggle = { scope.launch { controller.toggle(feature.featureKey) } },
                        )
                    }
                }
            }
        }
    }
}

@Composable
private fun FeatureRow(
    feature: FeatureStatus,
    manage: ManageDecision,
    onToggle: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val toggleLabel: String = stringResource(Res.string.features_toggle_action, feature.featureKey)
    val scopesLabel: String = stringResource(Res.string.features_scopes_label)

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
            .padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            Column(
                modifier = Modifier
                    .weight(1f)
                    .clearAndSetSemantics {
                        contentDescription = "${feature.label.ifEmpty { feature.featureKey }}, ${if (feature.isEnabled) "enabled" else "disabled"}."
                    },
                verticalArrangement = Arrangement.spacedBy(spacing.s1),
            ) {
                Text(
                    text = feature.label.ifEmpty { feature.featureKey },
                    style = typography.base,
                    color = tokens.cardForeground,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                if (feature.description.isNotEmpty()) {
                    Text(
                        text = feature.description,
                        style = typography.xs,
                        color = tokens.mutedForeground,
                        maxLines = 2,
                        overflow = TextOverflow.Ellipsis,
                    )
                }
                feature.enabledAt?.let { at ->
                    Text(
                        text = stringResource(Res.string.features_enabled_at, at.substringBefore('T')),
                        style = typography.xs,
                        color = tokens.mutedForeground,
                        maxLines = 1,
                    )
                }
            }
            ManageGate(decision = manage) { enabled ->
                Switch(
                    checked = feature.isEnabled,
                    onCheckedChange = { onToggle() },
                    enabled = enabled,
                    colors = SwitchDefaults.colors(
                        checkedThumbColor = tokens.primaryForeground,
                        checkedTrackColor = tokens.primary,
                        uncheckedThumbColor = tokens.mutedForeground,
                        uncheckedTrackColor = tokens.muted,
                        uncheckedBorderColor = tokens.border,
                    ),
                    modifier = Modifier.semantics { contentDescription = toggleLabel },
                )
            }
        }
        if (feature.requiredScopes.isNotEmpty()) {
            Text(
                text = "$scopesLabel: ${feature.requiredScopes.joinToString(", ")}",
                style = typography.xs,
                color = tokens.mutedForeground,
                maxLines = 2,
                overflow = TextOverflow.Ellipsis,
            )
        }
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
                text = stringResource(Res.string.features_error, detail),
                style = typography.base,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.features_retry)) }
        }
    }
}

@Composable
private fun CenteredMessage(text: String) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    Box(modifier = Modifier.fillMaxWidth(), contentAlignment = Alignment.Center) {
        Text(text = text, style = typography.base, color = tokens.mutedForeground)
    }
}
