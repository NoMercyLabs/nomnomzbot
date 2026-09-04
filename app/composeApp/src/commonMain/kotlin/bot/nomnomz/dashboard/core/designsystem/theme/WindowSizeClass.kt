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

import androidx.compose.runtime.Composable
import androidx.compose.runtime.ProvidableCompositionLocal
import androidx.compose.runtime.ReadOnlyComposable
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.unit.Dp

/**
 * How much room a screen actually has, at Material's standard widths (`frontend-design-system.md` §6).
 *
 * A screen picks a layout per class; the components inside are unchanged. That is what makes the mobile
 * build a matter of adding `Compact` layouts rather than rewriting components.
 */
enum class WindowSizeClass {
    /** Phone-width, or a very narrow desktop window: one column, nothing side-by-side. */
    Compact,

    /** Tablet-width: two columns where it genuinely helps, but no three-pane layouts. */
    Medium,

    /** Desktop-width: the full multi-column layout a screen was designed for. */
    Expanded;

    /** True when this is [Compact] — the common "stack it" branch, read at the call site. */
    val isCompact: Boolean
        get() = this == Compact

    /** True at [Medium] or wider — "there is room for a second column". */
    val isAtLeastMedium: Boolean
        get() = this != Compact

    /** True only at [Expanded] — "there is room for the full layout". */
    val isExpanded: Boolean
        get() = this == Expanded

    companion object {
        /** Material's standard width breakpoints. */
        val MediumMinWidth: Dp = Breakpoints.MediumMinWidth

        /** Material's standard width breakpoints. */
        val ExpandedMinWidth: Dp = Breakpoints.ExpandedMinWidth

        /**
         * The class for [width].
         *
         * Pass the width of the space the SCREEN was given, not the window's — the shell already spends
         * a fixed-width sidebar out of the window, so a 900 dp window can leave a screen 660 dp of content.
         * Branching on the window there would lay a screen out for room it does not have.
         */
        fun of(width: Dp): WindowSizeClass =
            when {
                width < MediumMinWidth -> Compact
                width < ExpandedMinWidth -> Medium
                else -> Expanded
            }
    }
}

/**
 * The size class of the space the current screen occupies, provided once by the shell.
 *
 * Defaults to [WindowSizeClass.Expanded] so a composable previewed or tested outside the shell renders its
 * full layout rather than silently falling back to the phone one.
 */
val LocalWindowSizeClass: ProvidableCompositionLocal<WindowSizeClass> =
    staticCompositionLocalOf { WindowSizeClass.Expanded }

/** Shorthand for `LocalWindowSizeClass.current`, matching the `LocalTokens` / `LocalSpacing` house style. */
val windowSize: WindowSizeClass
    @Composable @ReadOnlyComposable get() = LocalWindowSizeClass.current
