// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Broadcast;

/// <summary>The planned break: its window and its last day. Both are empty when no break is planned or it is over.</summary>
internal sealed class ScheduleVacationHelpers()
    : BroadcastHelperGroup<ScheduleData>(
        ScheduleRows.Over(
            "schedule.vacation",
            "Fri 6 Nov - Sun 15 Nov",
            (view, call) =>
                view.Vacation is { } v
                    ? $"{BroadcastText.ShortDate(v.StartTime, call.Timezone, call.Culture)} - "
                        + BroadcastText.ShortDate(v.EndTime, call.Timezone, call.Culture)
                    : string.Empty
        ),
        ScheduleRows.Over(
            "schedule.vacation.until",
            "Sunday 15 November",
            (view, call) =>
                view.Vacation is { } v
                    ? BroadcastText.DayAndDate(v.EndTime, call.Timezone, call.Culture)
                    : string.Empty
        )
    );
