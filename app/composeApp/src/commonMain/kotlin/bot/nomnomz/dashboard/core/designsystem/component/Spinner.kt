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

import androidx.compose.foundation.layout.size
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.Tokens

// shadcn's Spinner sizes (Loader2 icon): sm 16, default 20, lg 24. Stroke tracks size.
private val SpinnerSmSize: Dp = 16.dp
private val SpinnerDefaultSize: Dp = 20.dp
private val SpinnerLgSize: Dp = 24.dp
private val SpinnerStroke: Dp = 2.dp

/** shadcn Spinner sizes (frontend-design-system.md §4, catalogue row). */
enum class SpinnerSize {
    Sm,
    Default,
    Lg,
}

private fun SpinnerSize.toDp(): Dp =
    when (this) {
        SpinnerSize.Sm -> SpinnerSmSize
        SpinnerSize.Default -> SpinnerDefaultSize
        SpinnerSize.Lg -> SpinnerLgSize
    }

/**
 * shadcn/ui Spinner ported to Compose (frontend-design-system.md §4, catalogue row).
 *
 * A themed wrapper over Material3's `CircularProgressIndicator` (indeterminate) — the a11y-correct
 * primitive — recoloured to [Tokens.primary] and sized from the closed [SpinnerSize] set. Drop-in
 * replacement for `CircularProgressIndicator`; when the caller sizes it with `Modifier.size(...)`,
 * pass that through [modifier] and leave [size] at its default.
 */
@Composable
fun Spinner(
    modifier: Modifier = Modifier,
    size: SpinnerSize = SpinnerSize.Default,
    color: Color? = null,
) {
    val tokens: Tokens = LocalTokens.current
    CircularProgressIndicator(
        modifier = modifier.size(size.toDp()),
        color = color ?: tokens.primary,
        strokeWidth = SpinnerStroke,
    )
}
