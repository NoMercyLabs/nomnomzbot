// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.time

import androidx.compose.runtime.Composable
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.time_ago_days
import nomnomzbot.composeapp.generated.resources.time_ago_hours
import nomnomzbot.composeapp.generated.resources.time_ago_just_now
import nomnomzbot.composeapp.generated.resources.time_ago_minutes
import nomnomzbot.composeapp.generated.resources.time_ago_seconds
import org.jetbrains.compose.resources.stringResource

/**
 * The translated "how long ago" wording for a bucketed age, shared by every screen that shows one
 * ("Last ran ...", "Held ...", the attention row age). The unit is chosen in [RelativeTime]; only the
 * wording lives here. An age past the day buckets is the calendar date itself, which needs no translation.
 */
@Composable
fun elapsedText(elapsed: Elapsed): String =
    when (elapsed) {
        is Elapsed.JustNow -> stringResource(Res.string.time_ago_just_now)
        is Elapsed.Seconds -> stringResource(Res.string.time_ago_seconds, elapsed.value)
        is Elapsed.Minutes -> stringResource(Res.string.time_ago_minutes, elapsed.value)
        is Elapsed.Hours -> stringResource(Res.string.time_ago_hours, elapsed.value)
        is Elapsed.Days -> stringResource(Res.string.time_ago_days, elapsed.value)
        is Elapsed.Date -> elapsed.date.toString()
    }
