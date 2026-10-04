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
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
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
// Beside the title the band keeps a 96dp minimum height so the header is identical on every page:
// the title sits at the same vertical position and the divider lands at the same Y regardless of
// whether the page has a subtitle or a trailing button.
//
// The trailing slot follows the width this header is given, not the window class (see InfoActionsRow):
// beside the title when it fits next to a readable title column, otherwise under the title with the
// actions wrapping, and the band grows to fit. A long localized label never wraps in place. Each action
// in the slot is its own item; a gate that wraps several actions in one Box keeps them as one item.
@Composable
fun PageHeader(
    title: String,
    modifier: Modifier = Modifier,
    subtitle: String? = null,
    trailing: (@Composable FlowRowScope.() -> Unit)? = null,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    Column(modifier = modifier.fillMaxWidth()) {
        Box(modifier = Modifier.fillMaxWidth().heightIn(min = spacing.s24), contentAlignment = Alignment.CenterStart) {
            if (trailing != null) {
                InfoActionsRow(
                    info = { infoModifier -> PageHeaderTitle(title = title, subtitle = subtitle, modifier = infoModifier) },
                    modifier = Modifier.fillMaxWidth().padding(vertical = spacing.s3),
                    actions = trailing,
                )
            } else {
                PageHeaderTitle(title = title, subtitle = subtitle, modifier = Modifier.fillMaxWidth())
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
