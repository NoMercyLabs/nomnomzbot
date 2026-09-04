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
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.windowSize

/**
 * Two entry fields that belong together — a key and its value, a name and its path, a search box and its
 * type filter — with an optional trailing action such as a remove button.
 *
 * Side by side when there is room, stacked into one column when the screen is Compact. Two half-width fields
 * inside a dialog on a phone leave each one around 150 dp, too narrow to read what you typed or to show a
 * field's error text underneath it — so the pair stops being a pair rather than getting smaller.
 *
 * The trailing [action] stays on the first row when stacked: it acts on the pair as a whole (removing the
 * row, running the search), so parking it under the second field would read as belonging to that field.
 *
 * @param first the leading field, given the modifier that sizes it.
 * @param second the trailing field, likewise.
 * @param action an optional control acting on the pair, at its natural width in both layouts.
 */
@Composable
fun FieldPair(
    first: @Composable (Modifier) -> Unit,
    second: @Composable (Modifier) -> Unit,
    modifier: Modifier = Modifier,
    verticalAlignment: Alignment.Vertical = Alignment.CenterVertically,
    action: @Composable (() -> Unit)? = null,
) {
    val spacing = LocalSpacing.current

    if (!windowSize.isCompact) {
        Row(
            modifier = modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(spacing.s2),
            verticalAlignment = verticalAlignment,
        ) {
            first(Modifier.weight(1f))
            second(Modifier.weight(1f))
            action?.invoke()
        }
        return
    }

    Column(
        modifier = modifier.fillMaxWidth(),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        if (action == null) {
            first(Modifier.fillMaxWidth())
        } else {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(spacing.s2),
                verticalAlignment = verticalAlignment,
            ) {
                first(Modifier.weight(1f))
                action()
            }
        }
        second(Modifier.fillMaxWidth())
    }
}
