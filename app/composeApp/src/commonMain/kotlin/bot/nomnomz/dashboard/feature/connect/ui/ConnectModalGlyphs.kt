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
import androidx.compose.ui.graphics.vector.PathParser
import androidx.compose.ui.graphics.vector.path
import androidx.compose.ui.unit.dp

// The connect modal's provider + app glyphs as [ImageVector]s.
//
// The four PROVIDER marks (Twitch / Spotify / Discord / YouTube) are the REAL, official logos: each is built
// VERBATIM from the canonical simple-icons SVG path data (https://github.com/simple-icons/simple-icons,
// CC0-1.0), parsed straight off the published `d` string via [PathParser] so the geometry is byte-exact and
// the proportions are the brand's own — no hand-sketched approximation. Every provider icon shares the brand
// standard 0 0 24 24 viewBox, so they scale cleanly and align with one another. They carry a single fill
// (each simple-icons mark is one filled path); the caller tints them with the provider brand colour via
// `Icon(tint = …)`, which is the correct, monochrome use of these single-colour brand marks.
//
// The NomNomz APP mark is a clean wordless placeholder (see [NomNomzMarkGlyph]) — there is no shipped brand
// asset in the repo yet (no app/packaging icon, splash, or tray PNG/SVG exists); this stands in until a real
// mark is minted, and is deliberately a simple geometric monogram, not a fake "logo".
//
// The Back arrow is the Lucide `arrow-left` glyph (the design system's icon set — frontend-design-system.md),
// authored as a round-capped stroke path on the same 24-unit viewport.

private const val BRAND_VIEWPORT: Float = 24f

// Build a single-fill [ImageVector] from a canonical SVG path `d` string, parsed verbatim so the brand
// geometry is exact. simple-icons marks use the non-zero fill rule; the fill colour is a placeholder
// (Black) — the caller supplies the real tint via `Icon(tint = …)`.
private fun brandLogo(name: String, pathData: String): ImageVector =
    ImageVector.Builder(
        name = name,
        defaultWidth = 24.dp,
        defaultHeight = 24.dp,
        viewportWidth = BRAND_VIEWPORT,
        viewportHeight = BRAND_VIEWPORT,
    ).apply {
        addPath(
            pathData = PathParser().parsePathString(pathData).toNodes(),
            fill = SolidColor(Color.Black),
            fillAlpha = 1f,
            pathFillType = PathFillType.NonZero,
        )
    }.build()

// The canonical simple-icons `d` strings (viewBox 0 0 24 24), copied verbatim from the published icons so the
// silhouettes are the brands' exact, recognisable artwork. Source: simple-icons (CC0-1.0).
//   twitch.svg / spotify.svg / discord.svg / youtube.svg
private const val TWITCH_PATH: String =
    "M11.571 4.714h1.715v5.143H11.57zm4.715 0H18v5.143h-1.714zM6 0L1.714 4.286v15.428h5.143V24l4.286-4.286h3.428L22.286 12V0zm14.571 11.143l-3.428 3.428h-3.429l-3 3v-3H6.857V1.714h13.714Z"

private const val SPOTIFY_PATH: String =
    "M12 0C5.4 0 0 5.4 0 12s5.4 12 12 12 12-5.4 12-12S18.66 0 12 0zm5.521 17.34c-.24.359-.66.48-1.021.24-2.82-1.74-6.36-2.101-10.561-1.141-.418.122-.779-.179-.899-.539-.12-.421.18-.78.54-.9 4.56-1.021 8.52-.6 11.64 1.32.42.18.479.659.301 1.02zm1.44-3.3c-.301.42-.841.6-1.262.3-3.239-1.98-8.159-2.58-11.939-1.38-.479.12-1.02-.12-1.14-.6-.12-.48.12-1.021.6-1.141C9.6 9.9 15 10.561 18.72 12.84c.361.181.54.78.241 1.2zm.12-3.36C15.24 8.4 8.82 8.16 5.16 9.301c-.6.179-1.2-.181-1.38-.721-.18-.601.18-1.2.72-1.381 4.26-1.26 11.28-1.02 15.721 1.621.539.3.719 1.02.419 1.56-.299.421-1.02.599-1.559.3z"

private const val DISCORD_PATH: String =
    "M20.317 4.3698a19.7913 19.7913 0 00-4.8851-1.5152.0741.0741 0 00-.0785.0371c-.211.3753-.4447.8648-.6083 1.2495-1.8447-.2762-3.68-.2762-5.4868 0-.1636-.3933-.4058-.8742-.6177-1.2495a.077.077 0 00-.0785-.037 19.7363 19.7363 0 00-4.8852 1.515.0699.0699 0 00-.0321.0277C.5334 9.0458-.319 13.5799.0992 18.0578a.0824.0824 0 00.0312.0561c2.0528 1.5076 4.0413 2.4228 5.9929 3.0294a.0777.0777 0 00.0842-.0276c.4616-.6304.8731-1.2952 1.226-1.9942a.076.076 0 00-.0416-.1057c-.6528-.2476-1.2743-.5495-1.8722-.8923a.077.077 0 01-.0076-.1277c.1258-.0943.2517-.1923.3718-.2914a.0743.0743 0 01.0776-.0105c3.9278 1.7933 8.18 1.7933 12.0614 0a.0739.0739 0 01.0785.0095c.1202.099.246.1981.3728.2924a.077.077 0 01-.0066.1276 12.2986 12.2986 0 01-1.873.8914.0766.0766 0 00-.0407.1067c.3604.698.7719 1.3628 1.225 1.9932a.076.076 0 00.0842.0286c1.961-.6067 3.9495-1.5219 6.0023-3.0294a.077.077 0 00.0313-.0552c.5004-5.177-.8382-9.6739-3.5485-13.6604a.061.061 0 00-.0312-.0286zM8.02 15.3312c-1.1825 0-2.1569-1.0857-2.1569-2.419 0-1.3332.9555-2.4189 2.157-2.4189 1.2108 0 2.1757 1.0952 2.1568 2.419 0 1.3332-.9555 2.4189-2.1569 2.4189zm7.9748 0c-1.1825 0-2.1569-1.0857-2.1569-2.419 0-1.3332.9554-2.4189 2.1569-2.4189 1.2108 0 2.1757 1.0952 2.1568 2.419 0 1.3332-.946 2.4189-2.1568 2.4189Z"

private const val YOUTUBE_PATH: String =
    "M23.498 6.186a3.016 3.016 0 0 0-2.122-2.136C19.505 3.545 12 3.545 12 3.545s-7.505 0-9.377.505A3.017 3.017 0 0 0 .502 6.186C0 8.07 0 12 0 12s0 3.93.502 5.814a3.016 3.016 0 0 0 2.122 2.136c1.871.505 9.376.505 9.376.505s7.505 0 9.377-.505a3.015 3.015 0 0 0 2.122-2.136C24 15.93 24 12 24 12s0-3.93-.502-5.814zM9.545 15.568V8.432L15.818 12l-6.273 3.568z"

/** The real Twitch logo (simple-icons `twitch.svg`, viewBox 0 0 24 24). Tinted by the caller. */
val TwitchLogoGlyph: ImageVector = brandLogo("TwitchLogo", TWITCH_PATH)

/** The real Spotify logo (simple-icons `spotify.svg`, viewBox 0 0 24 24). Tinted by the caller. */
val SpotifyLogoGlyph: ImageVector = brandLogo("SpotifyLogo", SPOTIFY_PATH)

/** The real Discord logo (simple-icons `discord.svg`, viewBox 0 0 24 24). Tinted by the caller. */
val DiscordLogoGlyph: ImageVector = brandLogo("DiscordLogo", DISCORD_PATH)

/** The real YouTube logo (simple-icons `youtube.svg`, viewBox 0 0 24 24). Tinted by the caller. */
val YouTubeLogoGlyph: ImageVector = brandLogo("YouTubeLogo", YOUTUBE_PATH)

/**
 * The NomNomz app mark used in the modal header (left logo, tinted in the provider's brand colour).
 *
 * PLACEHOLDER — there is no shipped NomNomz brand asset in the repository yet (no desktop packaging icon, no
 * splash, no tray PNG/SVG; `nativeDistributions` sets no `iconFile`). Rather than pass off a hand-drawn
 * sketch as "the logo", this is an honest, simple geometric monogram: a rounded-square badge with an "N"
 * cut through it, on the brand-standard 24-unit viewport. Swap this for the real mark the moment one exists
 * (a single drawable in `composeResources/drawable/` referenced via `vectorResource`).
 */
val NomNomzMarkGlyph: ImageVector =
    ImageVector.Builder(
        name = "NomNomzMark",
        defaultWidth = 24.dp,
        defaultHeight = 24.dp,
        viewportWidth = BRAND_VIEWPORT,
        viewportHeight = BRAND_VIEWPORT,
    ).apply {
        // Rounded-square badge with an "N" monogram knocked out (even-odd), so the caller's tint reads as the
        // badge and the glyph shows through as the background behind it.
        path(
            fill = SolidColor(Color.Black),
            pathFillType = PathFillType.EvenOdd,
        ) {
            // Outer rounded square (radius 5 on a 24 box).
            moveTo(5f, 1f)
            horizontalLineTo(19f)
            arcTo(4f, 4f, 0f, false, true, 23f, 5f)
            verticalLineTo(19f)
            arcTo(4f, 4f, 0f, false, true, 19f, 23f)
            horizontalLineTo(5f)
            arcTo(4f, 4f, 0f, false, true, 1f, 19f)
            verticalLineTo(5f)
            arcTo(4f, 4f, 0f, false, true, 5f, 1f)
            close()
            // "N" monogram, knocked out: left stem, diagonal, right stem.
            moveTo(7.5f, 17f)
            verticalLineTo(7f)
            horizontalLineTo(9.5f)
            lineTo(14.5f, 13.2f)
            verticalLineTo(7f)
            horizontalLineTo(16.5f)
            verticalLineTo(17f)
            horizontalLineTo(14.5f)
            lineTo(9.5f, 10.8f)
            verticalLineTo(17f)
            close()
        }
    }.build()

/**
 * The "Back" arrow for the secondary connect-flow button — Lucide `arrow-left` (the design system's icon
 * set), a round-capped 2px stroke on the 24-unit viewport. Tinted by the caller via `Icon(tint = …)`.
 */
val BackArrowGlyph: ImageVector =
    ImageVector.Builder(
        name = "ArrowLeft",
        defaultWidth = 24.dp,
        defaultHeight = 24.dp,
        viewportWidth = BRAND_VIEWPORT,
        viewportHeight = BRAND_VIEWPORT,
    ).apply {
        path(
            stroke = SolidColor(Color.Black),
            strokeLineWidth = 2f,
            strokeLineCap = StrokeCap.Round,
            strokeLineJoin = StrokeJoin.Round,
        ) {
            // Lucide arrow-left: the shaft, then the chevron head.
            moveTo(19f, 12f)
            horizontalLineTo(5f)
            moveTo(12f, 19f)
            lineTo(5f, 12f)
            lineTo(12f, 5f)
        }
    }.build()
