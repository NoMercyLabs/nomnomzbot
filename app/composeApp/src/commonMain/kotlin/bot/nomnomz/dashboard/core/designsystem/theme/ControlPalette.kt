// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.designsystem.theme

import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp

/**
 * Exact colors used by the NomNomz Figma control library.
 *
 * These values intentionally live beside the semantic app theme: the referenced component frames
 * define fixed neutral/red treatments (including translucent overlays) that must not be recolored by
 * the per-channel accent system.
 */
internal object ControlPalette {
    val Canvas: Color = Color(0xFF111111)
    val Surface: Color = Color(0xFF191919)
    val SurfaceRaised: Color = Color(0xFF222222)
    val SurfaceHover: Color = Color(0xFF2E2E2E)
    val Border: Color = Color(0xFF1F1F1F)
    val BorderHover: Color = Color(0xFF383838)
    val Focus: Color = Color(0xFF62626A)
    val White: Color = Color(0xFFFFFFFF)
    val LilacWhite: Color = Color(0xFFEDEBFF)
    val InactiveOutline: Color = LilacWhite.copy(alpha = 0.32f)
    val Ink: Color = Color(0xFF1B1B1B)
    val Inactive: Color = Color(0xFF606060)
    val ControlOutline: Color = Color(0xFF484848)
    val Helper: Color = Color(0x99FFFFFF)
    val Destructive: Color = Color(0xFFE51A3C)
    val DestructiveContent: Color = Color(0xFFDA1B38)
    val DestructiveTint: Color = Color(0xFFEE1133)
    val PrimaryFocus: Color = Color(0xFFFF0055)
}

/** Exact shared geometry from Figma node 308:1582. */
internal object ControlMetrics {
    val InputHeight: Dp = 54.dp
    val InputRadius: Dp = 16.dp
    val InputFocusStroke: Dp = 3.dp
    val InputHorizontalInset: Dp = 16.dp
    val InputTrailingInset: Dp = 6.dp
    val InputVerticalInset: Dp = 2.dp
}
