// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.designsystem.theme

import androidx.compose.runtime.Immutable
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import bot.nomnomz.dashboard.core.designsystem.color.oklch

// The closed shadcn (new-york) OKLCH token contract (frontend-design-system.md §1).
// Components read these through LocalTokens.current; they never touch a raw hex/dp.
//
// FOUNDATION-slice note: the full neutral set is normally emitted by the
// `generateDesignTokens` Gradle task into a committed TokensNeutral.kt from shadcn's
// canonical CSS (spec §1.2). That generator lands in a later slice; here the neutral
// values are encoded by hand from shadcn's published Neutral theme to give the spine a
// real, on-token theme to render. The accent family (`primary`/`ring`) is shown static
// here; the dynamic chat-color accent (spec §2) wires in with the session/query slice.

@Immutable
data class Tokens(
    val background: Color,
    val foreground: Color,
    val card: Color,
    val cardForeground: Color,
    val popover: Color,
    val popoverForeground: Color,
    val primary: Color,
    val primaryForeground: Color,
    val secondary: Color,
    val secondaryForeground: Color,
    val muted: Color,
    val mutedForeground: Color,
    val accent: Color,
    val accentForeground: Color,
    val destructive: Color,
    val destructiveForeground: Color,
    val border: Color,
    val input: Color,
    val ring: Color,
    val sidebar: Color,
    val sidebarForeground: Color,
    val sidebarPrimary: Color,
    val sidebarPrimaryForeground: Color,
    val sidebarAccent: Color,
    val sidebarAccentForeground: Color,
    val sidebarBorder: Color,
    val sidebarRing: Color,
    val radius: Radii,
)

/** Corner radii derived from shadcn's base `--radius` (10dp): sm -4, md -2, lg +0, xl +4. */
@Immutable
data class Radii(
    val sm: Dp = 6.dp,
    val md: Dp = 8.dp,
    val lg: Dp = 10.dp,
    val xl: Dp = 14.dp,
)

// shadcn Neutral theme (oklch(L 0 0) achromatic) — light scheme.
internal val LightTokens: Tokens = Tokens(
    background = oklch(1.0, 0.0, 0.0),
    foreground = oklch(0.145, 0.0, 0.0),
    card = oklch(1.0, 0.0, 0.0),
    cardForeground = oklch(0.145, 0.0, 0.0),
    popover = oklch(1.0, 0.0, 0.0),
    popoverForeground = oklch(0.145, 0.0, 0.0),
    primary = oklch(0.205, 0.0, 0.0),
    primaryForeground = oklch(0.985, 0.0, 0.0),
    secondary = oklch(0.97, 0.0, 0.0),
    secondaryForeground = oklch(0.205, 0.0, 0.0),
    muted = oklch(0.97, 0.0, 0.0),
    mutedForeground = oklch(0.556, 0.0, 0.0),
    accent = oklch(0.97, 0.0, 0.0),
    accentForeground = oklch(0.205, 0.0, 0.0),
    destructive = oklch(0.577, 0.245, 27.325),
    destructiveForeground = oklch(0.985, 0.0, 0.0),
    border = oklch(0.922, 0.0, 0.0),
    input = oklch(0.922, 0.0, 0.0),
    ring = oklch(0.708, 0.0, 0.0),
    sidebar = oklch(0.985, 0.0, 0.0),
    sidebarForeground = oklch(0.145, 0.0, 0.0),
    sidebarPrimary = oklch(0.205, 0.0, 0.0),
    sidebarPrimaryForeground = oklch(0.985, 0.0, 0.0),
    sidebarAccent = oklch(0.97, 0.0, 0.0),
    sidebarAccentForeground = oklch(0.205, 0.0, 0.0),
    sidebarBorder = oklch(0.922, 0.0, 0.0),
    sidebarRing = oklch(0.708, 0.0, 0.0),
    radius = Radii(),
)

// shadcn Neutral theme — dark scheme.
internal val DarkTokens: Tokens = Tokens(
    background = oklch(0.145, 0.0, 0.0),
    foreground = oklch(0.985, 0.0, 0.0),
    card = oklch(0.205, 0.0, 0.0),
    cardForeground = oklch(0.985, 0.0, 0.0),
    popover = oklch(0.205, 0.0, 0.0),
    popoverForeground = oklch(0.985, 0.0, 0.0),
    primary = oklch(0.922, 0.0, 0.0),
    primaryForeground = oklch(0.205, 0.0, 0.0),
    secondary = oklch(0.269, 0.0, 0.0),
    secondaryForeground = oklch(0.985, 0.0, 0.0),
    muted = oklch(0.269, 0.0, 0.0),
    mutedForeground = oklch(0.708, 0.0, 0.0),
    accent = oklch(0.269, 0.0, 0.0),
    accentForeground = oklch(0.985, 0.0, 0.0),
    destructive = oklch(0.704, 0.191, 22.216),
    destructiveForeground = oklch(0.985, 0.0, 0.0),
    border = oklch(1.0, 0.0, 0.0, 0.1),
    input = oklch(1.0, 0.0, 0.0, 0.15),
    ring = oklch(0.556, 0.0, 0.0),
    sidebar = oklch(0.205, 0.0, 0.0),
    sidebarForeground = oklch(0.985, 0.0, 0.0),
    sidebarPrimary = oklch(0.922, 0.0, 0.0),
    sidebarPrimaryForeground = oklch(0.205, 0.0, 0.0),
    sidebarAccent = oklch(0.269, 0.0, 0.0),
    sidebarAccentForeground = oklch(0.985, 0.0, 0.0),
    sidebarBorder = oklch(1.0, 0.0, 0.0, 0.1),
    sidebarRing = oklch(0.556, 0.0, 0.0),
    radius = Radii(),
)
