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
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp

// The fixed 4dp-based spacing scale (frontend-design-system.md §1.3). Components use
// `Space.*` only — never a raw `.dp` literal. Resolved dp: 0,2,4,6,8,12,16,24,32,48,64,96.
@Immutable
data class Spacing(
    val s0: Dp = 0.dp,
    val s0_5: Dp = 2.dp,
    val s1: Dp = 4.dp,
    val s1_5: Dp = 6.dp,
    val s2: Dp = 8.dp,
    val s3: Dp = 12.dp,
    val s4: Dp = 16.dp,
    val s6: Dp = 24.dp,
    val s8: Dp = 32.dp,
    val s12: Dp = 48.dp,
    val s16: Dp = 64.dp,
    val s24: Dp = 96.dp,
)

internal val DefaultSpacing: Spacing = Spacing()
