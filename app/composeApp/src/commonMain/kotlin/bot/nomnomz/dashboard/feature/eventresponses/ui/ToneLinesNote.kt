// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.eventresponses.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.event_responses_tone_lines_note
import org.jetbrains.compose.resources.stringResource

// What a row that follows the platform default will actually say: the bot picks one of these lines each time, in
// the channel's tone (or the platform admin's text alone). Renders nothing for a row with its own text, so the
// note never claims a tone behaviour the row does not have.
@Composable
internal fun ToneLinesNote(lines: List<String>) {
    if (lines.isEmpty()) return

    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
        Text(
            text = stringResource(Res.string.event_responses_tone_lines_note),
            style = typography.xs,
            color = tokens.mutedForeground,
        )
        lines.forEach { line: String ->
            Text(text = line, style = typography.sm, color = tokens.popoverForeground)
        }
    }
}
