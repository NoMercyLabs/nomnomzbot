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

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.graphics.Color

// The single theme root (frontend-design-system.md §3). It builds the active `Tokens`
// from the scheme and provides LocalTokens/LocalScheme/LocalSpacing/LocalTypography.
// Screens never build a theme and never read MaterialTheme.* directly — they read
// LocalTokens.current. A Material3 ColorScheme is derived from our tokens so wrapped
// Material components inherit them (spec §3.2).
//
// FOUNDATION-slice note: the dynamic chat-color accent (spec §2) and the System/Light/
// Dark persisted switch (spec §3.2) wire in with the session + settings slices; here the
// scheme is a plain parameter so the spine renders a real, on-token light/dark theme.

enum class Scheme { Light, Dark }

val LocalTokens = staticCompositionLocalOf<Tokens> {
    error("LocalTokens not provided — wrap content in NomNomzTheme { }")
}

val LocalScheme = staticCompositionLocalOf { Scheme.Dark }

val LocalSpacing = staticCompositionLocalOf { DefaultSpacing }

val LocalTypography = staticCompositionLocalOf { DefaultTypography }

@Composable
fun NomNomzTheme(
    scheme: Scheme = Scheme.Dark,
    /** The streamer's Twitch chat color as `#RRGGBB`. When non-null, overrides the accent tokens. */
    accentHex: String? = null,
    content: @Composable () -> Unit,
) {
    val base: Tokens = if (scheme == Scheme.Dark) DarkTokens else LightTokens
    val tokens: Tokens = if (accentHex != null) base.withAccent(accentHex) else base

    val colorScheme = if (scheme == Scheme.Dark) {
        darkColorScheme(
            primary = tokens.primary,
            onPrimary = tokens.primaryForeground,
            secondary = tokens.secondary,
            onSecondary = tokens.secondaryForeground,
            background = tokens.background,
            onBackground = tokens.foreground,
            surface = tokens.card,
            onSurface = tokens.cardForeground,
            error = tokens.destructive,
            onError = tokens.destructiveForeground,
            outline = tokens.border,
        )
    } else {
        lightColorScheme(
            primary = tokens.primary,
            onPrimary = tokens.primaryForeground,
            secondary = tokens.secondary,
            onSecondary = tokens.secondaryForeground,
            background = tokens.background,
            onBackground = tokens.foreground,
            surface = tokens.card,
            onSurface = tokens.cardForeground,
            error = tokens.destructive,
            onError = tokens.destructiveForeground,
            outline = tokens.border,
        )
    }

    CompositionLocalProvider(
        LocalTokens provides tokens,
        LocalScheme provides scheme,
        LocalSpacing provides DefaultSpacing,
        LocalTypography provides DefaultTypography,
    ) {
        MaterialTheme(colorScheme = colorScheme, content = content)
    }
}
