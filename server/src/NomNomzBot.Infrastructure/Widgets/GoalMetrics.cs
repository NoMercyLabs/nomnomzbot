// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Widgets;

/// <summary>
/// Twitch's creator-goal <c>type</c> field (<c>channel.goal.*</c> and Get Creator Goals) to the widget library's
/// `metric` vocabulary (`goal_bar.vue`/`labels.vue`: <c>followers</c>/<c>subs</c>). The live handler and the seed
/// provider both call this, so a goal gets the same metric on both paths. An unmapped Twitch goal type maps to
/// nothing rather than a guess.
/// </summary>
public static class GoalMetrics
{
    public static string? FromTwitchGoalType(string twitchGoalType) =>
        twitchGoalType switch
        {
            "follower" or "followers" => "followers",
            "subscription"
            or "subscription_count"
            or "new_subscription"
            or "new_subscription_count" => "subs",
            _ => null,
        };
}
