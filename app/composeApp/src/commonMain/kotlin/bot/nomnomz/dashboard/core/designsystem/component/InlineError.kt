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

import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography

// A section-scoped load/preview failure, shown persistently in place next to the content it describes (a tab's
// list failed to load, a blast-radius preview couldn't be computed) — distinct from a general write-outcome,
// which announces on the shell-level Feedback toast instead (see core/feedback/FeedbackHost.kt). Unlike the old
// ActionErrorBanner this carries no auto-hide timer: a load failure must stay visible until the section reloads,
// not vanish after 10 seconds while the section is still broken.
@Composable
fun InlineError(message: String, modifier: Modifier = Modifier) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    Text(text = message, style = typography.sm, color = tokens.destructive, modifier = modifier)
}
