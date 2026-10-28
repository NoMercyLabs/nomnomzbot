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

using NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Broadcast;

/// <summary>The last ad break, the snooze allowance and the pre-roll free time.</summary>
internal sealed class AdPastAndSnoozeHelpers()
    : BroadcastHelperGroup<AdData>(
        AdRows.AtMoment(
            "ad.last.ago",
            "48m",
            s => s.LastAdAt,
            (at, call) => BroadcastText.Brief(call.Now - at)
        ),
        AdRows.AtMoment(
            "ad.last.at",
            "14:42",
            s => s.LastAdAt,
            (at, call) => BroadcastText.Clock(at, call.Timezone)
        ),
        AdRows.Over(
            "ad.snoozes.left",
            "2",
            (s, call) => PeopleText.Count(s.SnoozeCount, call.Culture)
        ),
        AdRows.AtMoment(
            "ad.snoozes.refreshIn",
            "34m",
            s => s.SnoozeRefreshAt,
            (at, call) => BroadcastText.Brief(at - call.Now)
        ),
        AdRows.Over(
            "ad.prerollFree",
            "20m",
            (s, _) => BroadcastText.Brief(TimeSpan.FromSeconds(s.PrerollFreeTime))
        )
    );
