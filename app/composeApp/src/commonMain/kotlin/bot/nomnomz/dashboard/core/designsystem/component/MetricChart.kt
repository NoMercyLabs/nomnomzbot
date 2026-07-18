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

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.unit.Dp
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens

// Lightweight, token-driven Compose charts for the analytics trend surface (frontend-ia.md §3). A single
// Canvas pass over the series — no heavy plotting dependency, no raw hex/dp: every colour arrives from the
// caller as a [LocalTokens] value and every geometry constant is derived from [LocalSpacing] via the density.
//
// Each chart is a pure projection of its [values] list (one point per day in the analytics case). The value
// domain is auto-scaled to `[0, max]` so a flat-zero series renders as a baseline rather than dividing by zero.
// The x-axis is index-based (evenly spaced points); date labels live beside the chart in the calling section,
// keeping each chart focused on drawing one series.

/**
 * A filled line chart of [values] (left→right, one point each), scaled so the tallest point reaches the top and
 * `0` sits on the baseline. [lineColor] draws the stroke; a translucent copy fills beneath it. Fewer than two
 * points renders an empty baseline (nothing to connect).
 */
@Composable
fun LineChart(
    values: List<Float>,
    lineColor: Color,
    modifier: Modifier = Modifier,
    height: Dp = LocalSpacing.current.s24,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val density = LocalDensity.current

    val strokePx: Float = with(density) { spacing.s0_5.toPx() }
    val baselinePx: Float = with(density) { (spacing.s0_5 / 2).toPx() }
    val axisColor: Color = tokens.border
    val fillColor: Color = lineColor.copy(alpha = 0.12f)
    val max: Float = values.maxOrNull()?.takeIf { it > 0f } ?: 1f

    Canvas(modifier = modifier.fillMaxWidth().height(height)) {
        drawBaseline(axisColor, baselinePx)
        if (values.size < 2) return@Canvas

        val stepX: Float = size.width / (values.size - 1)
        fun pointFor(index: Int): Offset =
            Offset(x = index * stepX, y = size.height - (values[index] / max) * size.height)

        val line: Path = Path()
        val area: Path = Path()
        values.indices.forEach { index ->
            val point: Offset = pointFor(index)
            if (index == 0) {
                line.moveTo(point.x, point.y)
                area.moveTo(point.x, size.height)
                area.lineTo(point.x, point.y)
            } else {
                line.lineTo(point.x, point.y)
                area.lineTo(point.x, point.y)
            }
        }
        area.lineTo(size.width, size.height)
        area.close()

        drawPath(path = area, color = fillColor)
        drawPath(path = line, color = lineColor, style = Stroke(width = strokePx))
    }
}

/**
 * A vertical bar chart of [values] (left→right, one bar each), scaled so the tallest bar fills the height.
 * [barColor] fills each bar; the baseline uses the border token. An empty list renders just the baseline.
 */
@Composable
fun BarChart(
    values: List<Float>,
    barColor: Color,
    modifier: Modifier = Modifier,
    height: Dp = LocalSpacing.current.s24,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val density = LocalDensity.current

    val baselinePx: Float = with(density) { (spacing.s0_5 / 2).toPx() }
    val gapPx: Float = with(density) { spacing.s0_5.toPx() }
    val axisColor: Color = tokens.border
    val max: Float = values.maxOrNull()?.takeIf { it > 0f } ?: 1f

    Canvas(modifier = modifier.fillMaxWidth().height(height)) {
        drawBaseline(axisColor, baselinePx)
        if (values.isEmpty()) return@Canvas

        val slot: Float = size.width / values.size
        val barWidth: Float = (slot - gapPx).coerceAtLeast(baselinePx)
        values.forEachIndexed { index, value ->
            val barHeight: Float = (value / max) * size.height
            drawRect(
                color = barColor,
                topLeft = Offset(x = index * slot + gapPx / 2f, y = size.height - barHeight),
                size = androidx.compose.ui.geometry.Size(width = barWidth, height = barHeight),
            )
        }
    }
}

/** The bottom axis line shared by both charts. */
private fun DrawScope.drawBaseline(color: Color, strokeWidth: Float) {
    drawLine(
        color = color,
        start = Offset(x = 0f, y = size.height),
        end = Offset(x = size.width, y = size.height),
        strokeWidth = strokeWidth,
    )
}
