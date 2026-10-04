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
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.FlowRowScope
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.layout.Layout
import androidx.compose.ui.layout.Measurable
import androidx.compose.ui.layout.Placeable
import androidx.compose.ui.unit.Constraints
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.isSpecified
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing

// Narrowest info column that still reads as text, as a multiple of the largest spacing step.
private const val MinInfoColumnSteps: Int = 3

/**
 * A text block with its controls: beside each other when the controls fit next to a readable [info] column,
 * otherwise [info] takes its own line and the controls wrap beneath it.
 *
 * The choice is made from the width this row is actually given, not from the window class — a Medium or
 * Expanded window still hands a row only the pane beside the sidebar, and a long run of localized action
 * labels can claim all of it.
 */
@Composable
fun InfoActionsRow(
    info: @Composable (Modifier) -> Unit,
    modifier: Modifier = Modifier,
    minInfoWidth: Dp = Dp.Unspecified,
    actions: @Composable FlowRowScope.() -> Unit,
) {
    val spacing = LocalSpacing.current
    val gap: Dp = spacing.s3
    val minInfo: Dp = if (minInfoWidth.isSpecified) minInfoWidth else spacing.s24 * MinInfoColumnSteps
    Layout(
        content = {
            info(Modifier)
            FlowRow(
                horizontalArrangement = Arrangement.spacedBy(gap),
                verticalArrangement = Arrangement.spacedBy(spacing.s1),
                content = actions,
            )
        },
        modifier = modifier,
    ) { measurables, constraints ->
        val infoMeasurable: Measurable = measurables[0]
        val actionsMeasurable: Measurable = measurables[1]
        val gapPx: Int = gap.roundToPx()
        val available: Int = constraints.maxWidth
        val actionsWanted: Int = actionsMeasurable.maxIntrinsicWidth(Constraints.Infinity)
        val sideBySide: Boolean = actionsWanted + gapPx + minInfo.roundToPx() <= available

        if (sideBySide) {
            val actionsPlaceable: Placeable = actionsMeasurable.measure(Constraints(maxWidth = actionsWanted))
            val infoPlaceable: Placeable =
                infoMeasurable.measure(Constraints(maxWidth = available - actionsWanted - gapPx))
            val height: Int = maxOf(infoPlaceable.height, actionsPlaceable.height)
            layout(available, height) {
                infoPlaceable.place(0, (height - infoPlaceable.height) / 2)
                actionsPlaceable.place(available - actionsPlaceable.width, (height - actionsPlaceable.height) / 2)
            }
        } else {
            val infoPlaceable: Placeable = infoMeasurable.measure(Constraints(maxWidth = available))
            val actionsPlaceable: Placeable = actionsMeasurable.measure(Constraints(maxWidth = available))
            val stackGap: Int = spacing.s2.roundToPx()
            layout(available, infoPlaceable.height + stackGap + actionsPlaceable.height) {
                infoPlaceable.place(0, 0)
                actionsPlaceable.place(0, infoPlaceable.height + stackGap)
            }
        }
    }
}
