// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Humanizer;
using NomNomzBot.Application.DTOs.Economy;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;

/// <summary>
/// The channel currency of the person: the wallet balance (<c>ICurrencyAccountService</c>), the currency name
/// (<c>ICurrencyConfigService</c>) and the place on the channel's balance ranking (<c>IEconomyLeaderboardService</c>).
/// No wallet reads as 0 points; no place on the ranking reads as empty.
/// </summary>
internal sealed class PeoplePointsHelpers()
    : PeopleHelperGroup(
        PeopleRow.Live(
            "points",
            ViewerSides.Both,
            "2,500",
            "wallet",
            PeopleRow.Map(
                d => d.BalanceAsync(),
                (balance, call) => PeopleText.Count(balance ?? 0, call.Culture)
            )
        ) with
        {
            TargetSample = "800",
        },
        PeopleRow.Live(
            "points.raw",
            ViewerSides.User,
            "2500",
            "wallet",
            PeopleRow.Map(d => d.BalanceAsync(), (balance, _) => PeopleText.Raw(balance ?? 0))
        ),
        PeopleRow.Live("points.name", ViewerSides.User, "nomz", "wallet", CurrencyNameAsync),
        PeopleRow.Live(
            "points.rank",
            ViewerSides.Both,
            "4",
            "ranking",
            Place((own, _) => PeopleText.Raw(own.Rank))
        ) with
        {
            TargetSample = "17",
        },
        PeopleRow.Live(
            "points.rank.ordinal",
            ViewerSides.User,
            "4th",
            "ranking",
            Place((own, call) => own.Rank.Ordinalize(call.Culture))
        ),
        PeopleRow.Live(
            "points.nextRank.gap",
            ViewerSides.User,
            "300",
            "ranking",
            Ahead((own, next) => PeopleText.Raw(next.Value - own.Value))
        ),
        PeopleRow.Live(
            "points.nextRank.name",
            ViewerSides.User,
            "ViewerThree",
            "ranking",
            Ahead((_, next) => next.DisplayName)
        )
    )
{
    /// <summary>The singular name for exactly one point, the plural name otherwise (when the channel set one).</summary>
    private static async Task<string?> CurrencyNameAsync(PeopleCall call)
    {
        CurrencyConfigDto? config = await call.Data.CurrencyAsync();
        if (config is null)
            return null;

        long balance = await call.Data.BalanceAsync() ?? 0;
        return balance != 1 && !string.IsNullOrWhiteSpace(config.CurrencyNamePlural)
            ? config.CurrencyNamePlural
            : config.CurrencyName;
    }

    /// <summary>A row over the person's own entry of the ranking; empty when the person is not on it.</summary>
    private static Func<PeopleCall, Task<string?>> Place(
        Func<LeaderboardEntryDto, PeopleCall, string> print
    ) =>
        PeopleRow.Map(
            d => d.RankingAsync(),
            (ranking, call) =>
                OwnEntry(ranking, call.Who) is { } own ? print(own, call) : string.Empty
        );

    /// <summary>A row over the entry one place above the person; empty for the leader or an unranked person.</summary>
    private static Func<PeopleCall, Task<string?>> Ahead(
        Func<LeaderboardEntryDto, LeaderboardEntryDto, string> print
    ) =>
        PeopleRow.Map(
            d => d.RankingAsync(),
            (ranking, call) =>
            {
                LeaderboardEntryDto? own = OwnEntry(ranking, call.Who);
                LeaderboardEntryDto? next = own is null
                    ? null
                    : ranking.FirstOrDefault(e => e.Rank == own.Rank - 1);
                return own is not null && next is not null ? print(own, next) : string.Empty;
            }
        );

    private static LeaderboardEntryDto? OwnEntry(
        IReadOnlyList<LeaderboardEntryDto> ranking,
        ViewerRef who
    ) => who.UserId is { } id ? ranking.FirstOrDefault(e => e.SubjectUserId == id) : null;
}
