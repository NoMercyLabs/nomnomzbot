// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.chat.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.feature.chat.state.LineMark
import bot.nomnomz.dashboard.feature.chat.state.LineMarkKind
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.chat_mark_banned
import nomnomzbot.composeapp.generated.resources.chat_mark_banned_by
import nomnomzbot.composeapp.generated.resources.chat_mark_cleared
import nomnomzbot.composeapp.generated.resources.chat_mark_deleted
import nomnomzbot.composeapp.generated.resources.chat_mark_deleted_by
import nomnomzbot.composeapp.generated.resources.chat_mark_duration_days
import nomnomzbot.composeapp.generated.resources.chat_mark_duration_hours
import nomnomzbot.composeapp.generated.resources.chat_mark_duration_minutes
import nomnomzbot.composeapp.generated.resources.chat_mark_duration_seconds
import nomnomzbot.composeapp.generated.resources.chat_mark_timed_out
import nomnomzbot.composeapp.generated.resources.chat_mark_timed_out_by
import nomnomzbot.composeapp.generated.resources.chat_mark_timed_out_for
import nomnomzbot.composeapp.generated.resources.chat_mark_timed_out_for_by
import org.jetbrains.compose.resources.stringResource

// The small tag on a moderated chat line ("Deleted by Ana", "Timed out 10 m by Ana"). Muted tokens only: the
// mark is context, never the focal point of the line. Shared by the single-channel and multi-channel feeds.
@Composable
internal fun LineMarkTag(mark: LineMark) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    Text(
        text = lineMarkText(mark),
        style = typography.xs,
        color = tokens.mutedForeground,
        maxLines = 1,
        modifier = Modifier
            .clip(RoundedCornerShape(tokens.radius.sm))
            .background(tokens.muted)
            .padding(horizontal = spacing.s1),
    )
}

@Composable
private fun lineMarkText(mark: LineMark): String {
    val by: String = mark.byDisplayName
    val named: Boolean = by.isNotBlank()
    return when (mark.kind) {
        LineMarkKind.Deleted ->
            if (named) stringResource(Res.string.chat_mark_deleted_by, by) else stringResource(Res.string.chat_mark_deleted)
        LineMarkKind.Banned ->
            if (named) stringResource(Res.string.chat_mark_banned_by, by) else stringResource(Res.string.chat_mark_banned)
        LineMarkKind.Cleared -> stringResource(Res.string.chat_mark_cleared)
        LineMarkKind.TimedOut -> {
            val seconds: Int? = mark.durationSeconds
            if (seconds == null) {
                if (named) stringResource(Res.string.chat_mark_timed_out_by, by) else stringResource(Res.string.chat_mark_timed_out)
            } else {
                val duration: String = durationText(seconds)
                if (named) stringResource(Res.string.chat_mark_timed_out_for_by, duration, by)
                else stringResource(Res.string.chat_mark_timed_out_for, duration)
            }
        }
    }
}

// The largest whole unit: 600 -> "10 m", 90 -> "90 s", 7200 -> "2 h", 172800 -> "2 d".
@Composable
private fun durationText(seconds: Int): String =
    when {
        seconds >= 86_400 && seconds % 86_400 == 0 -> stringResource(Res.string.chat_mark_duration_days, seconds / 86_400)
        seconds >= 3_600 && seconds % 3_600 == 0 -> stringResource(Res.string.chat_mark_duration_hours, seconds / 3_600)
        seconds >= 60 && seconds % 60 == 0 -> stringResource(Res.string.chat_mark_duration_minutes, seconds / 60)
        else -> stringResource(Res.string.chat_mark_duration_seconds, seconds)
    }
