// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.designsystem.component

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.ResourceUsage
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.limits_approaching_notice
import nomnomzbot.composeapp.generated.resources.limits_at_limit_reason
import org.jetbrains.compose.resources.stringResource

/**
 * S-BUDGETS-b3 — the ONE place "approaching a limit" is defined. Every limited-resource create surface
 * (commands, timers, event responses) reads this single definition instead of inventing its own threshold.
 *
 * A resource counts as [Approaching] when less than [ApproachingThresholdFraction] of its total limit remains
 * (floored at [ApproachingThresholdFloor] so a small limit still warns with real headroom left). [Unknown] covers
 * the moment before the usage endpoint has answered — the create affordance must never block on missing data, so
 * an unknown banding behaves exactly like [Normal].
 */
sealed interface LimitBanding {
    data object Unknown : LimitBanding

    data object Unlimited : LimitBanding

    data object Normal : LimitBanding

    data class Approaching(val remaining: Long, val limit: Long) : LimitBanding

    data class AtLimit(val usage: ResourceUsage) : LimitBanding
}

private const val ApproachingThresholdFraction: Double = 0.1
private const val ApproachingThresholdFloor: Long = 3

/** Classifies [this] (null when the usage endpoint hasn't answered yet, or the resource carries no report). */
fun ResourceUsage?.limitBanding(): LimitBanding {
    if (this == null) return LimitBanding.Unknown
    if (limit < 0) return LimitBanding.Unlimited
    val remaining: Long = (limit - currentCount).coerceAtLeast(0)
    if (remaining <= 0) return LimitBanding.AtLimit(this)
    val threshold: Long = maxOf(ApproachingThresholdFloor, (limit * ApproachingThresholdFraction).toLong())
    return if (remaining <= threshold) LimitBanding.Approaching(remaining = remaining, limit = limit) else LimitBanding.Normal
}

/**
 * Wraps a create [content] (typically a [Button]) with the S-BUDGETS-b3 warn-before-refuse behavior driven by
 * [usage] — the channel's real [ResourceUsage] for the resource this surface creates, straight from
 * `GET .../billing/limits`, never client-computed or estimated:
 * - At the limit: [content] receives `enabled = false` and the disabled reason renders underneath it, naming the
 *   limit in plain language — the create affordance is never silently missing, never enabled-then-failing.
 * - Approaching the limit: a non-alarming notice with the real remaining count renders above [content].
 * - [usage] is NEAR_FREE only (commands/timers/event-responses are all safety-baseline resources) — the reason
 *   and the notice are ABUSE-GUARD sentences and MUST NEVER carry upgrade/upsell/tier-comparison copy.
 */
@Composable
fun LimitedCreateAction(
    usage: ResourceUsage?,
    modifier: Modifier = Modifier,
    content: @Composable (enabled: Boolean) -> Unit,
) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    val spacing = LocalSpacing.current

    when (val banding: LimitBanding = usage.limitBanding()) {
        is LimitBanding.Approaching -> {
            Column(modifier = modifier, verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
                Text(
                    text = stringResource(Res.string.limits_approaching_notice, banding.remaining.toInt(), banding.limit.toInt()),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                )
                content(true)
            }
        }

        is LimitBanding.AtLimit -> {
            Column(modifier = modifier, verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
                content(false)
                Text(
                    text = stringResource(Res.string.limits_at_limit_reason, banding.usage.displayName),
                    style = typography.xs,
                    color = tokens.destructive,
                )
            }
        }

        LimitBanding.Normal, LimitBanding.Unlimited, LimitBanding.Unknown -> content(true)
    }
}
