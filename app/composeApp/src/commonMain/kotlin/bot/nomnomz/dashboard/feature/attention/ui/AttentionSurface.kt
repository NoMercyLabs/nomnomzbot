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

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.layout.onGloballyPositioned
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.IntOffset
import bot.nomnomz.dashboard.core.designsystem.component.Badge
import bot.nomnomz.dashboard.core.designsystem.component.OutlinedButton
import bot.nomnomz.dashboard.core.designsystem.component.Popover
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.ActionRequiredItem
import bot.nomnomz.dashboard.feature.attention.state.AttentionSeverity
import bot.nomnomz.dashboard.feature.attention.state.attentionRouteOf
import bot.nomnomz.dashboard.feature.attention.state.highestSeverity
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.attention_surface_label_many
import nomnomzbot.composeapp.generated.resources.attention_surface_label_one
import nomnomzbot.composeapp.generated.resources.attention_surface_title
import org.jetbrains.compose.resources.stringResource

/** Test tag of the shell's attention trigger. */
const val ATTENTION_SURFACE_TAG: String = "attention-surface"

/**
 * The shell-frame attention surface (plan item A0): lives in the persistent frame, outside the page view, so
 * every page shows that something needs fixing. The trigger carries the count under the badge of the most
 * urgent severity; it opens the grouped list, and each item goes straight to the page where it is fixed.
 * Renders nothing when [items] is empty — no fake "all clear" state.
 */
@Composable
fun AttentionSurface(
    items: List<ActionRequiredItem>,
    onNavigate: (ShellRoute) -> Unit,
    modifier: Modifier = Modifier,
) {
    val severity: AttentionSeverity = highestSeverity(items) ?: return
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    var open: Boolean by remember { mutableStateOf(false) }
    var triggerHeight: Int by remember { mutableIntStateOf(0) }
    val label: String =
        if (items.size == 1) {
            stringResource(Res.string.attention_surface_label_one)
        } else {
            stringResource(Res.string.attention_surface_label_many, items.size)
        }

    Box(modifier = modifier.onGloballyPositioned { triggerHeight = it.size.height }) {
        OutlinedButton(
            onClick = { open = !open },
            modifier = Modifier.fillMaxWidth().testTag(ATTENTION_SURFACE_TAG),
        ) {
            Badge(variant = badgeVariantFor(severity)) {
                Text(text = items.size.toString(), style = typography.xs)
            }
            Text(text = label, modifier = Modifier.weight(1f))
        }

        Popover(
            expanded = open,
            onDismissRequest = { open = false },
            alignment = Alignment.TopStart,
            offset = IntOffset(0, triggerHeight),
            modifier = Modifier.width(spacing.s24 * 4),
        ) {
            Column(
                modifier = Modifier.heightIn(max = spacing.s24 * 5).verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(spacing.s3),
            ) {
                Text(
                    text = stringResource(Res.string.attention_surface_title),
                    style = typography.sm,
                    fontWeight = FontWeight.SemiBold,
                    color = tokens.popoverForeground,
                )
                AttentionGroupedList(
                    items = items,
                    onOpen = { item ->
                        attentionRouteOf(item)?.let { route ->
                            open = false
                            onNavigate(route)
                        }
                    },
                )
            }
        }
    }
}
