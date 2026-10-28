// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
