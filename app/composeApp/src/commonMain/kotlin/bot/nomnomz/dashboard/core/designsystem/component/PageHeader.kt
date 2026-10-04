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
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.FlowRowScope
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextOverflow
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography

// The single page heading used by every management screen: title (xl2) + optional subtitle (sm,
// muted) + optional trailing action slot, closed by a divider.
//
// The content band is a FIXED 96dp height so the header is identical on every page:
// the title sits at the same vertical position and the divider lands at the same Y regardless of
// whether the page has a subtitle or a trailing button. Without this, subtitled/action-bearing
// headers grow taller than title-only ones and the content start jumps as you move between pages.
// Every variant fits the band — including the 60dp Figma action buttons with 18dp clearance above
// and below — so the band never has to grow and the pinning holds.
@Composable
fun PageHeader(
    title: String,
    modifier: Modifier = Modifier,
    subtitle: String? = null,
    trailing: (@Composable () -> Unit)? = null,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    Column(modifier = modifier.fillMaxWidth()) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .height(spacing.s24),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            PageHeaderTitle(title = title, subtitle = subtitle, modifier = Modifier.weight(1f))
            if (trailing != null) {
                // Keep the action clear of the (ellipsized) title column; the slot is vertically
                // centered against the title line so every page's action lands at the same height.
                Spacer(modifier = Modifier.width(spacing.s3))
                trailing()
            }
        }
        HorizontalDivider(color = tokens.border)
    }
}

// The title block of a page header: the title (xl2) over the optional muted subtitle (sm).
@Composable
private fun PageHeaderTitle(title: String, subtitle: String?, modifier: Modifier = Modifier) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    Column(modifier = modifier, verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
        Text(
            text = title,
            style = typography.xl2,
            color = tokens.foreground,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
        if (subtitle != null) {
            Text(
                text = subtitle,
                style = typography.sm,
                color = tokens.mutedForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
    }
}

/**
 * A [PageHeader] whose action group moves under the title when it does not fit beside a readable title column.
 *
 * Beside the title the band keeps the 96dp height of every other header. Once the actions need the pane's
 * width (a long run of localized labels), the title takes its own line and the actions wrap beneath it, and the
 * band grows to fit. The choice follows the width this header is given, not the window class (see
 * [InfoActionsRow]).
 */
@Composable
fun PageHeaderWithActions(
    title: String,
    modifier: Modifier = Modifier,
    subtitle: String? = null,
    actions: @Composable FlowRowScope.() -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    Column(modifier = modifier.fillMaxWidth()) {
        Box(modifier = Modifier.fillMaxWidth().heightIn(min = spacing.s24), contentAlignment = Alignment.CenterStart) {
            InfoActionsRow(
                info = { infoModifier -> PageHeaderTitle(title = title, subtitle = subtitle, modifier = infoModifier) },
                modifier = Modifier.fillMaxWidth().padding(vertical = spacing.s3),
                actions = actions,
            )
        }
        HorizontalDivider(color = tokens.border)
    }
}
