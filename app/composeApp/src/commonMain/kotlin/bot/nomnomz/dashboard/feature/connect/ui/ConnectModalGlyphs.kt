// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.connect.ui

import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.PathFillType
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.graphics.vector.path
import androidx.compose.ui.unit.dp

// The connect modal's brand/app glyphs as inline 24×24 [ImageVector]s. The app carries no icon-pack
// dependency yet (the IconKey/IconSet pack lands in the resources slice — see Stepper's CHECK_GLYPH note),
// so the dual-logo header and the Back arrow are drawn here from vector paths: tintable, crisp at any
// density, and zero-asset. Each glyph is authored on a 24-unit viewport so it scales cleanly to any size.
// Paths simplified to the recognisable brand silhouette (not the full trademarked artwork). Tint is applied
// by the caller via `Icon(tint = …)`, so these are single-colour outlines/fills.

private const val VIEWPORT: Float = 24f

private fun brandVector(name: String, block: ImageVector.Builder.() -> ImageVector.Builder): ImageVector =
    ImageVector.Builder(
        name = name,
        defaultWidth = 24.dp,
        defaultHeight = 24.dp,
        viewportWidth = VIEWPORT,
        viewportHeight = VIEWPORT,
    ).block().build()

private fun ImageVector.Builder.solid(
    block: androidx.compose.ui.graphics.vector.PathBuilder.() -> Unit,
): ImageVector.Builder =
    path(fill = SolidColor(Color.Black), pathFillType = PathFillType.NonZero, pathBuilder = block)

private fun ImageVector.Builder.evenOdd(
    block: androidx.compose.ui.graphics.vector.PathBuilder.() -> Unit,
): ImageVector.Builder =
    path(fill = SolidColor(Color.Black), pathFillType = PathFillType.EvenOdd, pathBuilder = block)

private fun ImageVector.Builder.stroke(
    width: Float,
    block: androidx.compose.ui.graphics.vector.PathBuilder.() -> Unit,
): ImageVector.Builder =
    path(
        stroke = SolidColor(Color.Black),
        strokeLineWidth = width,
        strokeLineCap = StrokeCap.Round,
        strokeLineJoin = StrokeJoin.Round,
        pathBuilder = block,
    )

/**
 * The NomNomz app mark used in the modal header (left logo, tinted in the provider's brand colour).
 * A rounded chat-bubble carrying a play/skip triangle — the "bot that plays your stream's requests"
 * silhouette. Stands in for the future bundled brand asset; recognisable and on-brand.
 */
val NomNomzMarkGlyph: ImageVector =
    brandVector("NomNomzMark") {
        // Rounded speech bubble with a tail at the lower-left.
        evenOdd {
            moveTo(5f, 3f)
            horizontalLineTo(19f)
            curveTo(20.66f, 3f, 22f, 4.34f, 22f, 6f)
            verticalLineTo(15f)
            curveTo(22f, 16.66f, 20.66f, 18f, 19f, 18f)
            horizontalLineTo(9.5f)
            lineTo(5.7f, 21.4f)
            curveTo(4.96f, 22.06f, 3.8f, 21.53f, 3.8f, 20.55f)
            verticalLineTo(18f)
            horizontalLineTo(5f)
            curveTo(3.34f, 18f, 2f, 16.66f, 2f, 15f)
            verticalLineTo(6f)
            curveTo(2f, 4.34f, 3.34f, 3f, 5f, 3f)
            close()
            // Inner play/skip triangle (knocked out, even-odd).
            moveTo(10f, 7.5f)
            verticalLineTo(13.5f)
            lineTo(15f, 10.5f)
            close()
        }
    }

/** The Twitch glitch silhouette (the speech-block + two vertical pips). */
val TwitchLogoGlyph: ImageVector =
    brandVector("TwitchLogo") {
        evenOdd {
            moveTo(4.3f, 3f)
            lineTo(3f, 6.5f)
            verticalLineTo(18f)
            horizontalLineTo(7f)
            verticalLineTo(21f)
            horizontalLineTo(9.5f)
            lineTo(12.5f, 18f)
            horizontalLineTo(17f)
            lineTo(21f, 14f)
            verticalLineTo(3f)
            close()
            // Knockout interior with the two pips.
            moveTo(5.5f, 5f)
            horizontalLineTo(19f)
            verticalLineTo(13f)
            lineTo(16.5f, 15.5f)
            horizontalLineTo(12f)
            lineTo(9.5f, 18f)
            verticalLineTo(15.5f)
            horizontalLineTo(5.5f)
            close()
            moveTo(11f, 7.5f)
            verticalLineTo(12f)
            horizontalLineTo(13f)
            verticalLineTo(7.5f)
            close()
            moveTo(15.5f, 7.5f)
            verticalLineTo(12f)
            horizontalLineTo(17.5f)
            verticalLineTo(7.5f)
            close()
        }
    }

/** The Spotify circle with three sweeping signal arcs. */
val SpotifyLogoGlyph: ImageVector =
    brandVector("SpotifyLogo") {
        // Outer disc.
        solid {
            moveTo(12f, 2f)
            curveTo(6.48f, 2f, 2f, 6.48f, 2f, 12f)
            curveTo(2f, 17.52f, 6.48f, 22f, 12f, 22f)
            curveTo(17.52f, 22f, 22f, 17.52f, 22f, 12f)
            curveTo(22f, 6.48f, 17.52f, 2f, 12f, 2f)
            close()
        }
        // Three signal arcs knocked out (drawn as light strokes over the disc).
        stroke(width = 1.6f) {
            moveTo(7f, 15.2f)
            curveTo(9.8f, 14f, 13.2f, 14.2f, 16f, 15.8f)
        }
        stroke(width = 1.8f) {
            moveTo(6.4f, 11.8f)
            curveTo(10f, 10.2f, 14.4f, 10.5f, 17.6f, 12.6f)
        }
        stroke(width = 2f) {
            moveTo(6f, 8.2f)
            curveTo(10.2f, 6.4f, 15.4f, 6.8f, 19f, 9.2f)
        }
    }

/** The Discord (Clyde) face silhouette. */
val DiscordLogoGlyph: ImageVector =
    brandVector("DiscordLogo") {
        evenOdd {
            // Rounded shield body.
            moveTo(18.6f, 5.2f)
            curveTo(17.3f, 4.6f, 15.9f, 4.2f, 14.5f, 4f)
            lineTo(14.2f, 4.6f)
            curveTo(15.5f, 4.9f, 16.7f, 5.4f, 17.8f, 6f)
            curveTo(15f, 4.7f, 9f, 4.7f, 6.2f, 6f)
            curveTo(7.3f, 5.4f, 8.5f, 4.9f, 9.8f, 4.6f)
            lineTo(9.5f, 4f)
            curveTo(8.1f, 4.2f, 6.7f, 4.6f, 5.4f, 5.2f)
            curveTo(2.9f, 9f, 2.2f, 12.7f, 2.5f, 16.4f)
            curveTo(4.1f, 17.6f, 5.6f, 18.3f, 7.1f, 18.8f)
            lineTo(7.9f, 17.5f)
            curveTo(7.2f, 17.2f, 6.6f, 16.9f, 6f, 16.5f)
            lineTo(6.4f, 16.2f)
            curveTo(9.1f, 17.5f, 14.9f, 17.5f, 17.6f, 16.2f)
            lineTo(18f, 16.5f)
            curveTo(17.4f, 16.9f, 16.8f, 17.2f, 16.1f, 17.5f)
            lineTo(16.9f, 18.8f)
            curveTo(18.4f, 18.3f, 19.9f, 17.6f, 21.5f, 16.4f)
            curveTo(21.9f, 12.1f, 20.8f, 8.4f, 18.6f, 5.2f)
            close()
            // Left eye knockout.
            moveTo(9f, 14f)
            curveTo(8.1f, 14f, 7.4f, 13.2f, 7.4f, 12.2f)
            curveTo(7.4f, 11.2f, 8.1f, 10.4f, 9f, 10.4f)
            curveTo(9.9f, 10.4f, 10.6f, 11.2f, 10.6f, 12.2f)
            curveTo(10.6f, 13.2f, 9.9f, 14f, 9f, 14f)
            close()
            // Right eye knockout.
            moveTo(15f, 14f)
            curveTo(14.1f, 14f, 13.4f, 13.2f, 13.4f, 12.2f)
            curveTo(13.4f, 11.2f, 14.1f, 10.4f, 15f, 10.4f)
            curveTo(15.9f, 10.4f, 16.6f, 11.2f, 16.6f, 12.2f)
            curveTo(16.6f, 13.2f, 15.9f, 14f, 15f, 14f)
            close()
        }
    }

/** The YouTube rounded-rectangle badge with a play triangle knocked out. */
val YouTubeLogoGlyph: ImageVector =
    brandVector("YouTubeLogo") {
        evenOdd {
            moveTo(21.6f, 7.2f)
            curveTo(21.35f, 6.3f, 20.65f, 5.6f, 19.75f, 5.35f)
            curveTo(18.1f, 4.9f, 12f, 4.9f, 12f, 4.9f)
            curveTo(12f, 4.9f, 5.9f, 4.9f, 4.25f, 5.35f)
            curveTo(3.35f, 5.6f, 2.65f, 6.3f, 2.4f, 7.2f)
            curveTo(2f, 9f, 2f, 12f, 2f, 12f)
            curveTo(2f, 12f, 2f, 15f, 2.4f, 16.8f)
            curveTo(2.65f, 17.7f, 3.35f, 18.4f, 4.25f, 18.65f)
            curveTo(5.9f, 19.1f, 12f, 19.1f, 12f, 19.1f)
            curveTo(12f, 19.1f, 18.1f, 19.1f, 19.75f, 18.65f)
            curveTo(20.65f, 18.4f, 21.35f, 17.7f, 21.6f, 16.8f)
            curveTo(22f, 15f, 22f, 12f, 22f, 12f)
            curveTo(22f, 12f, 22f, 9f, 21.6f, 7.2f)
            close()
            // Play triangle knockout.
            moveTo(10f, 15f)
            verticalLineTo(9f)
            lineTo(15f, 12f)
            close()
        }
    }

/** A simple left-pointing back arrow for the secondary "Back" button. */
val BackArrowGlyph: ImageVector =
    brandVector("BackArrow") {
        stroke(width = 2f) {
            moveTo(15f, 5f)
            lineTo(8f, 12f)
            lineTo(15f, 19f)
        }
    }
