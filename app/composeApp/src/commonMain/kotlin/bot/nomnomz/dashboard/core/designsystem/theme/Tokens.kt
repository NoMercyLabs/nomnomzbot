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
import kotlin.math.max
import kotlin.math.min
import kotlin.math.pow

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
    // Semantic status token — online / success indicator (no shadcn equivalent; added for the
    // live-channel status dot used throughout the shell).
    val success: Color,
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

/** The friendly radius scale: inputs 16dp, raised surfaces 20dp, and cards 24dp. */
@Immutable
data class Radii(
    val sm: Dp = 16.dp,
    val md: Dp = 16.dp,
    val lg: Dp = 20.dp,
    val xl: Dp = 24.dp,
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
    success = oklch(0.627, 0.194, 142.5),
    // Sidebar sits a shade DARKER than the app canvas (background = 1.0) for a distinct panel.
    sidebar = oklch(0.96, 0.0, 0.0),
    sidebarForeground = oklch(0.145, 0.0, 0.0),
    sidebarPrimary = oklch(0.205, 0.0, 0.0),
    sidebarPrimaryForeground = oklch(0.985, 0.0, 0.0),
    sidebarAccent = oklch(0.97, 0.0, 0.0),
    sidebarAccentForeground = oklch(0.205, 0.0, 0.0),
    sidebarBorder = oklch(0.922, 0.0, 0.0),
    sidebarRing = oklch(0.708, 0.0, 0.0),
    radius = Radii(),
)

// Ube dark palette, mapped onto the existing shadcn semantic contract. Keeping the
// contract intact means every dashboard surface adopts the warmer near-black depth scale
// and aubergine accent without component-level color overrides. Muted foreground uses
// gray-1100 (rather than its lower-contrast gray-1000) so small dashboard copy remains
// WCAG AA readable; foregrounds on the lavender action colors are likewise contrast-led.
internal val DarkTokens: Tokens = Tokens(
    background = oklch(0.177638, 0.0, 0.0), // gray-100  #111111
    foreground = oklch(0.949119, 0.0, 0.0), // gray-1200 #eeeeee
    card = oklch(0.213423, 0.0, 0.0), // gray-200  #191919
    cardForeground = oklch(0.949119, 0.0, 0.0),
    popover = oklch(0.213423, 0.0, 0.0),
    popoverForeground = oklch(0.949119, 0.0, 0.0),
    primary = oklch(0.692953, 0.114282, 309.045), // Ube-400 #b188d2
    primaryForeground = oklch(0.0, 0.0, 0.0),
    secondary = oklch(0.399069, 0.101692, 304.522), // Ube-800 #543773
    secondaryForeground = oklch(0.914771, 0.037602, 313.610), // Ube-100 #ecdcf5
    muted = oklch(0.251965, 0.0, 0.0), // gray-300 #222222
    mutedForeground = oklch(0.773104, 0.0, 0.0), // gray-1100 #b5b5b5
    accent = oklch(0.692953, 0.114282, 309.045),
    accentForeground = oklch(0.0, 0.0, 0.0),
    destructive = oklch(0.710627, 0.166148, 22.216), // dark destructive #f87171
    destructiveForeground = oklch(0.0, 0.0, 0.0),
    border = oklch(0.239292, 0.0, 0.0), // base-border #1f1f1f
    input = oklch(0.239292, 0.0, 0.0),
    // The brighter Ube tone keeps keyboard focus visible against the near-black canvas.
    ring = oklch(0.692953, 0.114282, 309.045),
    success = oklch(0.795095, 0.234563, 145.534), // mint-500 #29e14f
    // The rail uses the deepest base surface so depth remains sidebar < canvas < card.
    sidebar = oklch(0.168416, 0.0, 0.0), // base-el-primary #0f0f0f
    sidebarForeground = oklch(0.949119, 0.0, 0.0),
    // Overridden per-user by withAccent() once the streamer's Twitch chat color is known.
    sidebarPrimary = oklch(0.692953, 0.114282, 309.045),
    sidebarPrimaryForeground = oklch(0.0, 0.0, 0.0),
    sidebarAccent = oklch(0.232614, 0.043340, 304.159), // Ube-950 #22182e
    sidebarAccentForeground = oklch(0.949119, 0.0, 0.0),
    sidebarBorder = oklch(0.239292, 0.0, 0.0),
    sidebarRing = oklch(0.692953, 0.114282, 309.045),
    radius = Radii(),
)

// ─── Dynamic accent ───────────────────────────────────────────────────────────

/**
 * Returns a copy of this [Tokens] with the complete accent family derived from [hexColor].
 * The user's Twitch color drives primary actions, links, toggles, segmented selections,
 * focus rings, secondary actions, and the sidebar. Colors that are too close to the canvas
 * are moved toward the scheme foreground just enough to keep UI text and focus affordances
 * readable while preserving the user's hue.
 *
 * Falls back silently to `this` (no-op) when [hexColor] is malformed.
 */
internal fun Tokens.withAccent(hexColor: String): Tokens {
    val raw: Color = parseHexColor(hexColor) ?: return this
    val accessibleAccent: Color = raw.ensureContrastAgainst(background, foreground, 4.5)
    val onAccent: Color = accessibleAccent.bestForeground()
    val sidebarForeground: Color = oklch(0.985, 0.0, 0.0)
    val sidebarAccentColor: Color =
        raw.ensureContrastWith(sidebarForeground, oklch(0.205, 0.0, 0.0), 4.5)
    val secondaryTint: Color = background.blend(accessibleAccent, 0.32f)
    val hoverTint: Color = muted.blend(accessibleAccent, 0.20f)
    val sidebarHoverTint: Color = sidebar.blend(accessibleAccent, 0.18f)
    return copy(
        primary = accessibleAccent,
        primaryForeground = onAccent,
        secondary = secondaryTint,
        secondaryForeground = foreground,
        accent = hoverTint,
        accentForeground = foreground,
        ring = accessibleAccent,
        // The sidebar is a separate contrast context: selected navigation always carries
        // white content, so its user-color fill is darkened only when white would fail AA.
        sidebarPrimary = sidebarAccentColor,
        sidebarPrimaryForeground = sidebarForeground,
        sidebarAccent = sidebarHoverTint,
        sidebarAccentForeground = foreground,
        sidebarRing = accessibleAccent,
    )
}

private fun Color.ensureContrastWith(other: Color, toward: Color, minimum: Double): Color {
    var candidate: Color = this
    repeat(30) {
        if (candidate.contrastAgainst(other) >= minimum) return candidate
        candidate = candidate.blend(toward, 0.08f)
    }
    return candidate
}

private fun Color.ensureContrastAgainst(background: Color, toward: Color, minimum: Double): Color {
    var candidate: Color = this
    repeat(20) {
        val onCandidate: Color = candidate.bestForeground()
        if (
            candidate.contrastAgainst(background) >= minimum &&
                candidate.contrastAgainst(onCandidate) >= minimum
        ) {
            return candidate
        }
        candidate = candidate.blend(toward, 0.08f)
    }
    return candidate
}

private fun Color.bestForeground(): Color {
    val nearWhite: Color = oklch(0.985, 0.0, 0.0)
    val nearBlack: Color = oklch(0.205, 0.0, 0.0)
    return if (contrastAgainst(nearWhite) >= contrastAgainst(nearBlack)) nearWhite else nearBlack
}

private fun Color.contrastAgainst(other: Color): Double {
    val first: Double = relativeLuminance()
    val second: Double = other.relativeLuminance()
    return (max(first, second) + 0.05) / (min(first, second) + 0.05)
}

private fun Color.relativeLuminance(): Double =
    0.2126 * red.linearized() + 0.7152 * green.linearized() + 0.0722 * blue.linearized()

private fun Float.linearized(): Double {
    val channel: Double = toDouble()
    return if (channel <= 0.04045) channel / 12.92 else ((channel + 0.055) / 1.055).pow(2.4)
}

private fun Color.blend(other: Color, fraction: Float): Color =
    Color(
        red = red + (other.red - red) * fraction,
        green = green + (other.green - green) * fraction,
        blue = blue + (other.blue - blue) * fraction,
        alpha = 1f,
    )

/**
 * Parses a `#RRGGBB` (or `#RGB`) hex string into a [Color]. Returns null when the string is
 * absent or malformed rather than throwing, so callers can fall through to the default.
 */
private fun parseHexColor(hex: String): Color? {
    val stripped: String = hex.trimStart('#')
    val value: Long = stripped.toLongOrNull(16) ?: return null
    return when (stripped.length) {
        6 -> Color(
            red = ((value shr 16) and 0xFF).toInt() / 255f,
            green = ((value shr 8) and 0xFF).toInt() / 255f,
            blue = (value and 0xFF).toInt() / 255f,
        )
        3 -> Color(
            red = (((value shr 8) and 0xF) * 17).toInt() / 255f,
            green = (((value shr 4) and 0xF) * 17).toInt() / 255f,
            blue = ((value and 0xF) * 17).toInt() / 255f,
        )
        else -> null
    }
}
