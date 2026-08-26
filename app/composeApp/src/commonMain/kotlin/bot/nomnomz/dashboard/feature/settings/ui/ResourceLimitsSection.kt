// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.settings.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import bot.nomnomz.dashboard.core.designsystem.resolveRowLabel
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.ResourceClass
import bot.nomnomz.dashboard.core.network.ResourceUsage
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.settings_resource_limits_cost_driving_title
import nomnomzbot.composeapp.generated.resources.settings_resource_limits_empty
import nomnomzbot.composeapp.generated.resources.settings_resource_limits_error
import nomnomzbot.composeapp.generated.resources.settings_resource_limits_error_detail
import nomnomzbot.composeapp.generated.resources.settings_resource_limits_near_free_detail
import nomnomzbot.composeapp.generated.resources.settings_resource_limits_near_free_title
import nomnomzbot.composeapp.generated.resources.settings_resource_limits_of
import nomnomzbot.composeapp.generated.resources.settings_resource_limits_subtitle
import nomnomzbot.composeapp.generated.resources.settings_resource_limits_tier
import nomnomzbot.composeapp.generated.resources.settings_resource_limits_title
import nomnomzbot.composeapp.generated.resources.settings_resource_limits_unlimited
import org.jetbrains.compose.resources.stringResource

/**
 * S-BUDGETS-b2 — a settings-section reading the channel's truthful resource-limit report
 * ([ResourceUsage], `GET .../billing/limits`). Every number on screen is exactly what the endpoint returned;
 * nothing here estimates or recomputes a count or a limit.
 *
 * The two [ResourceClass] groups render as visually and textually distinct: NEAR_FREE items ("Safety limits")
 * are a uniform abuse floor and MUST NEVER carry upgrade/upsell/tier-comparison copy — the owner's binding
 * intent is that these limits recover real cost, never manufacture upsell pressure. COST_DRIVING items
 * ("Usage-based limits") map to a real bill and may name the active tier.
 *
 * [loadFailed] renders a distinct "could not load" state, separate from a legitimately empty [items] list
 * (nothing declared as limited) — the two must never look the same.
 */
@Composable
fun ResourceLimitsSection(
    items: List<ResourceUsage>,
    loadFailed: Boolean,
    isSelfHost: Boolean,
    tierDisplayName: String,
    modifier: Modifier = Modifier,
) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    val spacing = LocalSpacing.current

    Column(modifier = modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
        Text(
            text = stringResource(Res.string.settings_resource_limits_title),
            style = typography.sm.copy(fontWeight = FontWeight.SemiBold),
            color = tokens.cardForeground,
        )
        Text(
            text = stringResource(Res.string.settings_resource_limits_subtitle),
            style = typography.xs,
            color = tokens.mutedForeground,
        )

        when {
            loadFailed -> {
                Column(verticalArrangement = Arrangement.spacedBy(spacing.s0_5)) {
                    Text(
                        text = stringResource(Res.string.settings_resource_limits_error),
                        style = typography.sm.copy(fontWeight = FontWeight.SemiBold),
                        color = tokens.destructive,
                    )
                    Text(
                        text = stringResource(Res.string.settings_resource_limits_error_detail),
                        style = typography.xs,
                        color = tokens.mutedForeground,
                    )
                }
            }

            items.isEmpty() -> {
                Text(
                    text = stringResource(Res.string.settings_resource_limits_empty),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                )
            }

            else -> {
                val nearFree: List<ResourceUsage> = items.filter { it.resourceClass == ResourceClass.NearFree }
                val costDriving: List<ResourceUsage> = items.filter { it.resourceClass == ResourceClass.CostDriving }

                if (nearFree.isNotEmpty()) {
                    Column(verticalArrangement = Arrangement.spacedBy(spacing.s1_5)) {
                        Text(
                            text = stringResource(Res.string.settings_resource_limits_near_free_title),
                            style = typography.xs,
                            color = tokens.mutedForeground,
                        )
                        Text(
                            text = stringResource(Res.string.settings_resource_limits_near_free_detail),
                            style = typography.xs,
                            color = tokens.mutedForeground,
                        )
                        nearFree.forEach { resource -> ResourceUsageRow(resource) }
                    }
                }

                if (costDriving.isNotEmpty()) {
                    Column(verticalArrangement = Arrangement.spacedBy(spacing.s1_5)) {
                        Text(
                            text = stringResource(Res.string.settings_resource_limits_cost_driving_title),
                            style = typography.xs,
                            color = tokens.mutedForeground,
                        )
                        // Self-host never shows a commercial ceiling / tier affordance for cost-driving limits —
                        // its limit already resolves to unlimited (-1) on the backend, so no tier name is shown.
                        if (!isSelfHost && tierDisplayName.isNotBlank()) {
                            Text(
                                text = stringResource(Res.string.settings_resource_limits_tier, tierDisplayName),
                                style = typography.xs,
                                color = tokens.mutedForeground,
                            )
                        }
                        costDriving.forEach { resource -> ResourceUsageRow(resource) }
                    }
                }
            }
        }
    }
}

@Composable
private fun ResourceUsageRow(resource: ResourceUsage) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    Row(
        horizontalArrangement = Arrangement.SpaceBetween,
        modifier = Modifier.fillMaxWidth(),
    ) {
        val label: String =
            resolveRowLabel(
                primary = resource.displayName,
                typeLabel = "Resource",
                discriminatorSource = resource.limitKey,
            )
        Text(
            text = label,
            style = typography.xs,
            color = tokens.mutedForeground,
        )
        Text(
            text = if (resource.limit < 0) {
                stringResource(Res.string.settings_resource_limits_unlimited, resource.currentCount.toInt())
            } else {
                stringResource(
                    Res.string.settings_resource_limits_of,
                    resource.currentCount.toInt(),
                    resource.limit.toInt(),
                )
            },
            style = typography.xs,
            color = tokens.cardForeground,
        )
    }
}
