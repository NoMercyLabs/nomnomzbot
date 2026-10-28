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

using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.LiveEvents;

/// <summary>The follower goal: where it stands, what is left, the bar and the streamer's own words.</summary>
internal sealed class GoalFollowerHelpers()
    : LiveEventHelperGroup(
        "goal",
        [
            .. LiveGoalRows.Progress(
                "followers",
                GoalKind.Followers,
                ["1,234", "1,500", "82%", "266", "████████░░ 82%"]
            ),
            LiveGoalRows.Of(
                "goal.followers.description",
                "Road to 1.5k",
                GoalKind.Followers,
                (goal, _) => string.IsNullOrWhiteSpace(goal.Description) ? null : goal.Description
            ),
        ]
    );

/// <summary>The subscription goal: where it stands, what is left and the bar.</summary>
internal sealed class GoalSubHelpers()
    : LiveEventHelperGroup(
        "goal",
        LiveGoalRows.Progress("subs", GoalKind.Subs, ["80", "100", "80%", "20", "████████░░ 80%"])
    );

/// <summary>The goal of new subscriptions made during the goal: how many and how far.</summary>
internal sealed class GoalNewSubHelpers()
    : LiveEventHelperGroup(
        "goal",
        LiveGoalRows.Of(
            "goal.newSubs.current",
            "6",
            GoalKind.NewSubs,
            (goal, call) => LiveText.Count(goal.CurrentAmount, call.Culture)
        ),
        LiveGoalRows.Of(
            "goal.newSubs.percent",
            "30%",
            GoalKind.NewSubs,
            (goal, _) => LiveText.Percent(goal.CurrentAmount, goal.TargetAmount)
        )
    );

/// <summary>Every active goal in one line.</summary>
internal sealed class GoalListHelpers()
    : LiveEventHelperGroup(
        "goal",
        LiveGoalRows.All("goal.list", "followers 82%, subs 80%", PrintList)
    )
{
    private static string? PrintList(IReadOnlyList<TwitchCreatorGoal> goals)
    {
        List<string> parts = [];
        foreach (TwitchCreatorGoal goal in goals)
        {
            GoalKind? kind = GoalKind.All.FirstOrDefault(k => k.Matches(goal));
            if (
                kind is not null
                && LiveText.Percent(goal.CurrentAmount, goal.TargetAmount) is { } percent
            )
                parts.Add($"{kind.Label} {percent}");
        }
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }
}
