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

using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Stream;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Broadcast;

/// <summary>The next ad break: when it starts, how far away it is, how long it lasts, and whether one is planned.</summary>
internal sealed class AdNextHelpers()
    : BroadcastHelperGroup<AdData>(
        AdRows.AtMoment(
            "ad.next.in",
            "12m",
            s => s.NextAdAt,
            (at, call) => BroadcastText.Brief(at - call.Now)
        ),
        AdRows.AtMoment(
            "ad.next.in.long",
            "12 minutes",
            s => s.NextAdAt,
            (at, call) => BroadcastText.Words(at - call.Now, call.Culture, 2)
        ),
        AdRows.AtMoment(
            "ad.next.at",
            "15:42",
            s => s.NextAdAt,
            (at, call) => BroadcastText.Clock(at, call.Timezone)
        ),
        AdRows.AtMoment(
            "ad.next.seconds",
            "720",
            s => s.NextAdAt,
            (at, call) => StreamText.Plain((long)Math.Max(0, (at - call.Now).TotalSeconds))
        ),
        AdRows.Over(
            "ad.next.duration",
            "1m 30s",
            (s, _) => StreamText.Short(TimeSpan.FromSeconds(s.Duration))
        ),
        BroadcastRow<AdData>.Of(
            "ad.isScheduled",
            "true",
            "ads",
            TemplateHelperFreshness.Cached,
            async call =>
                await call.Data.ScheduleAsync(call.Ct) is { } s
                    ? PeopleText.Flag(s.NextAdAt > 0)
                    : null
        )
    );
