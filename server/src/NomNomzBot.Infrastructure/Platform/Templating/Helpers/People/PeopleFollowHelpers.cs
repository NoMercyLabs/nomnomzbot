// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Moderation.SpamDefense;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;

/// <summary>
/// The follow record of the person (<c>IFollowStateService</c>). When the platform cannot say, the date rows are
/// empty and the follower state reads as not following; it never invents a follow date.
/// </summary>
internal sealed class PeopleFollowHelpers()
    : PeopleHelperGroup(
        PeopleRow.Live(
            "isFollower",
            ViewerSides.Target,
            "true",
            "follow",
            PeopleRow.Map(
                d => d.FollowAsync(),
                (f, _) => PeopleText.Flag(f.State == FollowState.Following)
            )
        ),
        PeopleRow.Live(
            "followedAt",
            ViewerSides.User,
            "2025-09-10",
            "follow",
            PeopleRow.Map(
                d => d.FollowAsync(),
                (f, call) =>
                    f.FollowedAt is { } at ? PeopleText.Date(at, call.Scope.Timezone) : string.Empty
            )
        ),
        PeopleRow.Live(
            "followAge.days",
            ViewerSides.User,
            "411",
            "follow",
            PeopleRow.Map(
                d => d.FollowAsync(),
                (f, call) =>
                    f.FollowedAt is { } at
                        ? PeopleText.Raw(PeopleText.Days(at, call.Now))
                        : string.Empty
            )
        )
    );
