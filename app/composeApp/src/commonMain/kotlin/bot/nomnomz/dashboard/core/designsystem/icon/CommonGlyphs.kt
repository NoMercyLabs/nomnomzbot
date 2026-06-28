// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.designsystem.icon

import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.graphics.vector.addPathNodes
import androidx.compose.ui.unit.dp

// Action icons sourced from the designer's icon pack (Line style, 24 × 24 viewport, 1.5px stroke).
// Callers tint with Icon(tint = <token>). Stroke colour in the SVG source is ignored.

private const val ICON_SIZE: Float = 24f
private const val SW: Float = 1.5f
private const val DOT_SW: Float = 2.5f

private fun icon(name: String, build: ImageVector.Builder.() -> Unit): ImageVector =
    ImageVector.Builder(
        name = name,
        defaultWidth = ICON_SIZE.dp,
        defaultHeight = ICON_SIZE.dp,
        viewportWidth = ICON_SIZE,
        viewportHeight = ICON_SIZE,
    ).apply(build).build()

private fun ImageVector.Builder.strokePath(d: String) =
    addPath(
        pathData = addPathNodes(d),
        stroke = SolidColor(Color.Black),
        strokeLineWidth = SW,
        strokeLineCap = StrokeCap.Round,
        strokeLineJoin = StrokeJoin.Round,
        fill = SolidColor(Color.Transparent),
    )

// Dot rendered as a zero-length segment with round caps — same technique Lucide uses.
private fun ImageVector.Builder.dotPath(d: String) =
    addPath(
        pathData = addPathNodes(d),
        stroke = SolidColor(Color.Black),
        strokeLineWidth = DOT_SW,
        strokeLineCap = StrokeCap.Round,
        fill = SolidColor(Color.Transparent),
    )

/** Plus/cross — System/add */
val AddGlyph: ImageVector = icon("Add") {
    strokePath("M12 12V5M12 12V19M12 12H19M12 12H5")
}

/** Pencil — System/edit */
val EditGlyph: ImageVector = icon("Edit") {
    strokePath("M5 19V16L17 4L20 7L8 19H5Z")
}

/** Pencil with underline — System/edit-line */
val EditLineGlyph: ImageVector = icon("EditLine") {
    strokePath("M4 19H20M8 16V13L17 4L20 7L11 16H8Z")
}

/** Trash bin — System/trash */
val TrashGlyph: ImageVector = icon("Trash") {
    strokePath(
        "M5 6H9M19 6H15M9 6H15M9 6C9 4.34315 10.3431 3 12 3C13.6569 3 15 4.34315 15 6" +
            "M6.3569 12.9259L6.55354 15.0889C6.74105 17.1515 6.8348 18.1828 7.41205 18.8927" +
            "C7.59916 19.1228 7.81935 19.3239 8.06544 19.4894C8.82468 20 9.86024 20 11.9314 20" +
            "H12.0686C14.1398 20 15.1753 20 15.9346 19.4894C16.1806 19.3239 16.4008 19.1228" +
            " 16.5879 18.8927C17.1652 18.1828 17.259 17.1515 17.4465 15.0889L17.6431 12.9259" +
            "C17.7905 11.3045 17.8642 10.4937 17.4895 9.91414C17.3689 9.72768 17.2182 9.56259" +
            " 17.0434 9.42565C16.5001 9 15.686 9 14.0579 9H9.94212C8.31397 9 7.49989 9" +
            " 6.9566 9.42565C6.78182 9.56259 6.63106 9.72768 6.51051 9.91414C6.13579 10.4937" +
            " 6.2095 11.3045 6.3569 12.9259Z",
    )
}

/** Horizontal minus — System/remove */
val RemoveGlyph: ImageVector = icon("Remove") {
    strokePath("M19 12H5")
}

/** Checkmark — System/check */
val CheckGlyph: ImageVector = icon("Check") {
    strokePath("M4 13L8.29289 17.2929C8.68342 17.6834 9.31658 17.6834 9.70711 17.2929L20 7")
}

/** Check inside circle — System/check-circle */
val CheckCircleGlyph: ImageVector = icon("CheckCircle") {
    strokePath(
        "M16 10L10.5 15.5L8 13M20 12C20 16.4183 16.4183 20 12 20" +
            "C7.58172 20 4 16.4183 4 12C4 7.58172 7.58172 4 12 4C16.4183 4 20 7.58172 20 12Z",
    )
}

/** Power button — System/power */
val PowerGlyph: ImageVector = icon("Power") {
    strokePath(
        "M17.6571 6.34315C20.7813 9.46734 20.7813 14.5327 17.6571 17.6569" +
            "C14.5329 20.781 9.46757 20.781 6.34338 17.6569C3.21918 14.5327 3.21918 9.46734 6.34338 6.34315" +
            "M12.0002 3L12.0002 12",
    )
}

/** File copy — Files/file-copy */
val CopyGlyph: ImageVector = icon("Copy") {
    strokePath(
        "M8 7H11L15 11V17M11 7C10.6319 8.84028 10.4479 9.76043 10.7851 10.3922" +
            "C10.9717 10.7419 11.2581 11.0283 11.6078 11.2149C12.2396 11.5521 13.1597 11.3681 15 11" +
            "M8 7V6.6C8 5.10011 8 4.35016 8.38197 3.82443C8.50533 3.65464 8.65464 3.50533 8.82443 3.38197" +
            "C9.35016 3 10.1001 3 11.6 3H15L19 7V13.4C19 14.8999 19 15.6498 18.618 16.1756" +
            "C18.4947 16.3454 18.3454 16.4947 18.1756 16.618C17.6498 17 16.8999 17 15.4 17H15" +
            "M15 3C14.6319 4.84028 14.4479 5.76043 14.7851 6.39216C14.9717 6.7419 15.2581 7.02828 15.6078 7.21493" +
            "C16.2396 7.55209 17.1597 7.36806 19 7" +
            "M15 17V17.4C15 18.8999 15 19.6498 14.618 20.1756C14.4947 20.3454 14.3454 20.4947 14.1756 20.618" +
            "C13.6498 21 12.8999 21 11.4 21H7.6C6.10011 21 5.35016 21 4.82443 20.618" +
            "C4.65464 20.4947 4.50533 20.3454 4.38197 20.1756C4 19.6498 4 18.8999 4 17.4V10.6" +
            "C4 9.10011 4 8.35016 4.38197 7.82443C4.50533 7.65464 4.65464 7.50533 4.82443 7.38197" +
            "C5.35016 7 6.10011 7 7.6 7H8",
    )
}

/** Play inside circle — Player/play-circle */
val PlayCircleGlyph: ImageVector = icon("PlayCircle") {
    strokePath(
        "M9 12V13.9409C9 15.1415 9 15.7418 9.32598 16.0392C9.42868 16.1329 9.54979 16.2042" +
            " 9.68158 16.2484C10.0999 16.3889 10.6246 16.0974 11.6742 15.5144L15.1677 13.5735" +
            "C16.271 12.9605 16.8227 12.6541 16.9183 12.2111C16.9484 12.072 16.9484 11.928 16.9183 11.7889" +
            "C16.8227 11.3459 16.271 11.0394 15.1677 10.4265L11.6742 8.48564C10.6246 7.90258 10.0999 7.61105" +
            " 9.68158 7.75156C9.54979 7.79583 9.42868 7.8671 9.32598 7.9608C9 8.25823 9 8.85853 9 10.0591V11.99" +
            "M20 12C20 16.4183 16.4183 20 12 20C7.58172 20 4 16.4183 4 12C4 7.58172 7.58172 4 12 4" +
            "C16.4183 4 20 7.58172 20 12Z",
    )
}

/** Arrow pointing up — Arrows/arrow-up */
val ArrowUpGlyph: ImageVector = icon("ArrowUp") {
    strokePath("M12 20V4M17 10L12 4L7 10")
}

/** Arrow pointing down — Arrows/arrow-down */
val ArrowDownGlyph: ImageVector = icon("ArrowDown") {
    strokePath("M12 4V20M17 14L12 20L7 14")
}

/** Circular refresh arrows — Arrows/arrow-refresh-horizontal */
val RefreshGlyph: ImageVector = icon("Refresh") {
    strokePath(
        "M3.99989 14.0016C5.3124 17.3833 8.21675 20 11.9804 20C15.5343 20 18.5483 17.6939 19.6007 14.5" +
            "M8.12494 15.5004L3.99989 14.0016L3.62494 18.5004" +
            "M20.0002 9.99836C18.6877 6.61664 15.7834 4 12.0198 4C8.46585 4 5.45186 6.30606 4.39948 9.49999" +
            "M20.3752 5.49958L20.0002 9.99836L15.8752 8.49958",
    )
}

/** Three horizontal dots — Navigation/more-horizontal */
val DotsHorizontalGlyph: ImageVector = icon("DotsHorizontal") {
    dotPath("M5 12h.01")
    dotPath("M12 12h.01")
    dotPath("M19 12h.01")
}

/** Three vertical dots — Navigation/more-vertical */
val DotsVerticalGlyph: ImageVector = icon("DotsVertical") {
    dotPath("M12 5v.01")
    dotPath("M12 12v.01")
    dotPath("M12 19v.01")
}

/** Chevron pointing down — Arrows/chevron-down */
val ChevronDownGlyph: ImageVector = icon("ChevronDown") {
    strokePath("M6 9L12 15L18 9")
}
