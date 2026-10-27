// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
