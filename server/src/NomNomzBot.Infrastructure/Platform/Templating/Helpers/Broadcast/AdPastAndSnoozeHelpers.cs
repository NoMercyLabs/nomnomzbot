// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
