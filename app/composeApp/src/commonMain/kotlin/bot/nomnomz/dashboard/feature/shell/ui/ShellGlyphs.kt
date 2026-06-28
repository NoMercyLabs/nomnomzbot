// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.shell.ui

import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.graphics.vector.path
import androidx.compose.ui.unit.dp

// Lucide stroke icons used by the sidebar. Each is the canonical Lucide path on a 24-unit viewport
// with a 2px round-capped stroke — tinted by the caller via Icon(tint = …).

private const val GLYPH_SIZE: Float = 24f

/**
 * Lucide `chevron-down` — used as the accordion-closed indicator on sidebar group labels.
 * Path: M6 9l6 6 6-6
 */
val ChevronDownGlyph: ImageVector =
    ImageVector.Builder(
        name = "ChevronDown",
        defaultWidth = 24.dp,
        defaultHeight = 24.dp,
        viewportWidth = GLYPH_SIZE,
        viewportHeight = GLYPH_SIZE,
    ).apply {
        path(
            stroke = SolidColor(Color.Black),
            strokeLineWidth = 2f,
            strokeLineCap = StrokeCap.Round,
            strokeLineJoin = StrokeJoin.Round,
        ) {
            moveTo(6f, 9f)
            lineTo(12f, 15f)
            lineTo(18f, 9f)
        }
    }.build()

/**
 * Lucide `chevron-up` — used as the accordion-open indicator on sidebar group labels.
 * Path: M18 15l-6-6-6 6
 */
val ChevronUpGlyph: ImageVector =
    ImageVector.Builder(
        name = "ChevronUp",
        defaultWidth = 24.dp,
        defaultHeight = 24.dp,
        viewportWidth = GLYPH_SIZE,
        viewportHeight = GLYPH_SIZE,
    ).apply {
        path(
            stroke = SolidColor(Color.Black),
            strokeLineWidth = 2f,
            strokeLineCap = StrokeCap.Round,
            strokeLineJoin = StrokeJoin.Round,
        ) {
            moveTo(18f, 15f)
            lineTo(12f, 9f)
            lineTo(6f, 15f)
        }
    }.build()
