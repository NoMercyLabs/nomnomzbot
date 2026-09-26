// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.attention.ui

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import bot.nomnomz.dashboard.core.designsystem.component.Badge
import bot.nomnomz.dashboard.core.designsystem.component.BadgeVariant
import bot.nomnomz.dashboard.core.designsystem.resolveRowLabel
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.ActionRequiredItem
import bot.nomnomz.dashboard.core.time.RelativeTime
import bot.nomnomz.dashboard.feature.attention.state.AttentionSeverity
import bot.nomnomz.dashboard.feature.attention.state.groupBySeverity
import kotlinx.datetime.Clock
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.attention_group_label
import nomnomzbot.composeapp.generated.resources.home_action_required_severity_critical
import nomnomzbot.composeapp.generated.resources.home_action_required_severity_info
import nomnomzbot.composeapp.generated.resources.home_action_required_severity_warning
import nomnomzbot.composeapp.generated.resources.home_attention_count_badge
import nomnomzbot.composeapp.generated.resources.home_attention_detected_ago
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource

// The one rendering of an action-required list, shared by the Home inbox and the shell's attention popover so
// both read identically: items grouped by severity (critical first), each group under a severity badge, each
// row its localized title + explanation + age. Rows open the item; a caller adds its own trailing actions.

/**
 * The badge variant for a severity. Only a critical item earns the loud destructive fill; a warning stays an
 * outline and info stays neutral, so the eye goes to what is actually broken (accent is scarce).
 */
fun badgeVariantFor(severity: AttentionSeverity): BadgeVariant =
    when (severity) {
        AttentionSeverity.Critical -> BadgeVariant.Destructive
        AttentionSeverity.Warning -> BadgeVariant.Outline
        AttentionSeverity.Info -> BadgeVariant.Secondary
    }

/** The localized name of a severity. */
fun severityLabelFor(severity: AttentionSeverity): StringResource =
    when (severity) {
        AttentionSeverity.Critical -> Res.string.home_action_required_severity_critical
        AttentionSeverity.Warning -> Res.string.home_action_required_severity_warning
        AttentionSeverity.Info -> Res.string.home_action_required_severity_info
    }

/** [items] grouped by severity, each group under its badge, each row opening via [onOpen]. */
@Composable
fun AttentionGroupedList(
    items: List<ActionRequiredItem>,
    onOpen: (ActionRequiredItem) -> Unit,
    modifier: Modifier = Modifier,
    trailing: (@Composable (ActionRequiredItem) -> Unit)? = null,
) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(modifier = modifier, verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
        groupBySeverity(items).forEach { (severity, group) ->
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
                Badge(variant = badgeVariantFor(severity)) {
                    Text(
                        text = stringResource(
                            Res.string.attention_group_label,
                            stringResource(severityLabelFor(severity)),
                            group.size,
                        ),
                        style = typography.xs,
                    )
                }
                group.forEach { item ->
                    AttentionItemRow(
                        item = item,
                        onOpen = { onOpen(item) },
                        trailing = trailing?.let { slot -> { slot(item) } },
                    )
                }
            }
        }
    }
}

/** One item: its localized title and explanation, how long ago it was detected, and the caller's actions. */
@Composable
fun AttentionItemRow(
    item: ActionRequiredItem,
    onOpen: () -> Unit,
    trailing: (@Composable () -> Unit)? = null,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val now = remember { Clock.System.now() }
    val title: String = attentionTitleOf(item).rememberText()
    val message: String? = attentionMessageOf(item)?.rememberText()

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.md))
            .clickable(onClickLabel = title, onClick = onOpen)
            .padding(vertical = spacing.s2, horizontal = spacing.s1),
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(spacing.s0_5)) {
            Row(
                horizontalArrangement = Arrangement.spacedBy(spacing.s2),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text(
                    // Routed through resolveRowLabel (RowLabelGuardTest): a row never renders without a name.
                    text = resolveRowLabel(
                        primary = title,
                        secondary = message,
                        typeLabel = item.kind,
                        discriminatorSource = item.id,
                    ),
                    style = typography.sm,
                    fontWeight = FontWeight.SemiBold,
                    color = tokens.cardForeground,
                    maxLines = 2,
                    overflow = TextOverflow.Ellipsis,
                    modifier = Modifier.weight(1f, fill = false),
                )
                if (item.count > 1) {
                    Badge(variant = BadgeVariant.Outline) {
                        Text(
                            text = stringResource(Res.string.home_attention_count_badge, item.count),
                            style = typography.xs,
                        )
                    }
                }
            }
            message?.let { text ->
                Text(
                    text = text,
                    style = typography.xs,
                    color = tokens.mutedForeground,
                    maxLines = 3,
                    overflow = TextOverflow.Ellipsis,
                )
            }
            RelativeTime.minutesSince(item.detectedAt, now)?.let { minutesAgo ->
                Text(
                    text = stringResource(
                        Res.string.home_attention_detected_ago,
                        minutesAgo.coerceAtLeast(0).toInt(),
                    ),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                )
            }
        }
        trailing?.invoke()
    }
}
