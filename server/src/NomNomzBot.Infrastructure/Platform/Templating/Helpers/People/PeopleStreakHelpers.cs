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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;

/// <summary>The attendance streak of the person (<c>IViewerAnalyticsService.GetStreakAsync</c>); no streak reads as 0.</summary>
internal sealed class PeopleStreakHelpers()
    : PeopleHelperGroup(
        PeopleRow.Live(
            "stats.streak",
            ViewerSides.Both,
            "5",
            "streak",
            PeopleRow.Map(d => d.StreakAsync(), (s, _) => PeopleText.Raw(s?.CurrentStreak ?? 0))
        ) with
        {
            TargetSample = "2",
        },
        PeopleRow.Live(
            "stats.streak.best",
            ViewerSides.User,
            "9",
            "streak",
            PeopleRow.Map(d => d.StreakAsync(), (s, _) => PeopleText.Raw(s?.MaxStreak ?? 0))
        )
    );
