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
import nomnomzbot.composeapp.generated.resources.watch_time_hours_minutes
import nomnomzbot.composeapp.generated.resources.watch_time_minutes
import org.jetbrains.compose.resources.stringResource
import kotlin.math.roundToLong

/** One minute-resolution label for every displayed viewer watch total. */
@Composable
fun watchTimeText(seconds: Long): String {
    val minutes: Long = seconds.coerceAtLeast(0) / 60
    val hours: Long = minutes / 60
    return if (hours > 0) {
        stringResource(Res.string.watch_time_hours_minutes, hours, minutes % 60)
    } else {
        stringResource(Res.string.watch_time_minutes, minutes)
    }
}

@Composable
fun watchHoursText(hours: Double): String = watchTimeText((hours.coerceAtLeast(0.0) * 3600).roundToLong())
