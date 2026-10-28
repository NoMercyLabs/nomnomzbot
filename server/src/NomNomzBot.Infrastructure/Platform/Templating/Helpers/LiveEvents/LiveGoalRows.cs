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

/// <summary>A kind of creator goal: the name it has in a list and the Twitch goal types that make it.</summary>
internal sealed record GoalKind(string Label, params string[] Types)
{
    public static readonly GoalKind Followers = new("followers", "follower");

    public static readonly GoalKind Subs = new("subs", "subscription", "subscription_count");

    public static readonly GoalKind NewSubs = new(
        "new subs",
        "new_subscription",
        "new_subscription_count"
    );

    /// <summary>The kinds a <c>goal.list</c> prints, in this order of precedence per goal.</summary>
    public static readonly IReadOnlyList<GoalKind> All = [Followers, Subs, NewSubs];

    public bool Matches(TwitchCreatorGoal goal) => Types.Contains(goal.Type);
}

/// <summary>The row factories of the <c>goal</c> namespace.</summary>
internal static class LiveGoalRows
{
    private const string Ns = "goal";

    /// <summary>A row over the channel's goal of one kind; it prints nothing when the channel has none.</summary>
    public static LiveEventRow Of(
        string key,
        string sample,
        GoalKind kind,
        Func<TwitchCreatorGoal, LiveEventCall, string?> print
    ) =>
        Row(
            key,
            sample,
            async call =>
                call.Args.Count == 0 && await Data(call).OfKindAsync(kind) is { } goal
                    ? print(goal, call)
                    : null
        );

    /// <summary>
    /// The five progress rows of one goal kind (<c>goal.&lt;name&gt;.current</c>, <c>.target</c>, <c>.percent</c>,
    /// <c>.remaining</c>, <c>.bar</c>); <paramref name="samples"/> holds their samples in that order.
    /// </summary>
    public static LiveEventRow[] Progress(string name, GoalKind kind, string[] samples) =>
        [
            Of(
                $"goal.{name}.current",
                samples[0],
                kind,
                (goal, call) => LiveText.Count(goal.CurrentAmount, call.Culture)
            ),
            Of(
                $"goal.{name}.target",
                samples[1],
                kind,
                (goal, call) => LiveText.Count(goal.TargetAmount, call.Culture)
            ),
            Of(
                $"goal.{name}.percent",
                samples[2],
                kind,
                (goal, _) => LiveText.Percent(goal.CurrentAmount, goal.TargetAmount)
            ),
            Of(
                $"goal.{name}.remaining",
                samples[3],
                kind,
                (goal, call) =>
                    LiveText.Count(
                        LiveText.Remaining(goal.CurrentAmount, goal.TargetAmount),
                        call.Culture
                    )
            ),
            Of(
                $"goal.{name}.bar",
                samples[4],
                kind,
                (goal, _) => LiveText.Bar(goal.CurrentAmount, goal.TargetAmount)
            ),
        ];

    /// <summary>A row over every active goal.</summary>
    public static LiveEventRow All(
        string key,
        string sample,
        Func<IReadOnlyList<TwitchCreatorGoal>, string?> print
    ) =>
        Row(
            key,
            sample,
            async call => call.Args.Count == 0 ? print(await Data(call).GoalsAsync()) : null
        );

    private static LiveEventRow Row(
        string key,
        string sample,
        Func<LiveEventCall, Task<string?>> render
    ) => new(key, sample, Ns, data => ((LiveGoalData)data).GoalsAsync(), render);

    private static LiveGoalData Data(LiveEventCall call) => (LiveGoalData)call.Data;
}
