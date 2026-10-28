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
