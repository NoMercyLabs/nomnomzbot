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

using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Domain.Identity;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;

/// <summary>
/// The channel's profile of the person (<c>IViewerAnalyticsService.GetProfileAsync</c>): sub state, watch time,
/// first and last seen, bits. A person with no profile reads as no sub, zero counts and empty dates.
/// </summary>
internal sealed class PeopleProfileHelpers()
    : PeopleHelperGroup(
        PeopleRow.Live(
            "isFollower",
            ViewerSides.User,
            "true",
            "profile",
            PeopleRow.Map(d => d.ProfileAsync(), (p, _) => PeopleText.Flag(p?.IsFollower ?? false))
        ),
        PeopleRow.Live(
            "isSub",
            ViewerSides.Both,
            "true",
            "profile",
            PeopleRow.Map(
                d => d.ProfileAsync(),
                (p, _) => PeopleText.Flag(p?.IsSubscriber ?? false)
            )
        ) with
        {
            TargetSample = "false",
        },
        PeopleRow.Live(
            "subTier",
            ViewerSides.User,
            "1",
            "profile",
            PeopleRow.Map(d => d.ProfileAsync(), (p, _) => PeopleText.TierNumber(p?.SubTier))
        ),
        PeopleRow.Live(
            "subTier.name",
            ViewerSides.User,
            "Tier 1",
            "profile",
            PeopleRow.Map(d => d.ProfileAsync(), (p, _) => TierName(p?.SubTier))
        ),
        PeopleRow.Live(
            "stats.watchTime.hours",
            ViewerSides.User,
            "12.5",
            "profile",
            PeopleRow.Map(
                d => d.ProfileAsync(),
                (p, _) => PeopleText.Hours(p?.TotalWatchSeconds ?? 0)
            )
        ),
        PeopleRow.Live(
            "stats.watchTime.long",
            ViewerSides.User,
            "12 hours, 30 minutes",
            "profile",
            PeopleRow.Map(
                d => d.ProfileAsync(),
                (p, call) => PeopleText.WatchTime(p?.TotalWatchSeconds ?? 0, call.Culture)
            )
        ),
        PeopleRow.Live(
            "stats.firstSeen.relative",
            ViewerSides.User,
            "one year ago",
            "profile",
            PeopleRow.Map(
                d => d.ProfileAsync(),
                (p, call) =>
                    p?.FirstSeenAt is { } first
                        ? PeopleText.Relative(PeopleText.Utc(first), call.Now, call.Culture)
                        : string.Empty
            )
        ),
        PeopleRow.Live(
            "stats.lastSeen",
            ViewerSides.Both,
            "2026-10-24",
            "profile",
            PeopleRow.Map(
                d => d.ProfileAsync(),
                (p, call) =>
                    p?.LastSeenAt is { } last
                        ? PeopleText.Date(PeopleText.Utc(last), call.Scope.Timezone)
                        : string.Empty
            )
        ) with
        {
            TargetSample = "2026-10-25",
        },
        PeopleRow.Live(
            "stats.lastSeen.relative",
            ViewerSides.User,
            "2 days ago",
            "profile",
            PeopleRow.Map(
                d => d.ProfileAsync(),
                (p, call) =>
                    p?.LastSeenAt is { } last
                        ? PeopleText.Relative(PeopleText.Utc(last), call.Now, call.Culture)
                        : string.Empty
            )
        ),
        PeopleRow.Live(
            "activity.level",
            ViewerSides.Both,
            "veteran",
            "profile",
            PeopleRow.Map(d => d.ProfileAsync(), (p, call) => ActivityLevel(p, call))
        ),
        PeopleRow.Live(
            "stats.bits",
            ViewerSides.Both,
            "2,300",
            "profile",
            PeopleRow.Map(
                d => d.ProfileAsync(),
                (p, call) => PeopleText.Count(p?.TotalBitsCheered ?? 0, call.Culture)
            )
        ) with
        {
            TargetSample = "0",
        }
    )
{
    /// <summary>
    /// The level of the person from the real message count in this channel against the channel's own steps
    /// (defaults when the render has no channel context). A person with no profile has sent no messages: <c>new</c>.
    /// </summary>
    private static string ActivityLevel(ViewerProfileDto? profile, PeopleCall call)
    {
        ChannelContext? channel = call.Scope.Channel;
        return ViewerActivitySteps.Level(
            profile?.TotalMessages ?? 0,
            channel?.ViewerCasualFrom ?? ViewerActivitySteps.DefaultCasualFrom,
            channel?.ViewerRegularFrom ?? ViewerActivitySteps.DefaultRegularFrom,
            channel?.ViewerVeteranFrom ?? ViewerActivitySteps.DefaultVeteranFrom
        );
    }

    private static string TierName(string? tier) =>
        PeopleText.TierNumber(tier) is { Length: > 0 } number ? "Tier " + number : string.Empty;
}
