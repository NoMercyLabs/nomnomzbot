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

using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.LiveEvents;

/// <summary>The goal namespace's active follower and subscription goals.</summary>
internal static class GoalSurfaceFixture
{
    public static void Configure(ServiceCollection services, Guid channel)
    {
        ITwitchGoalsApi goals = Substitute.For<ITwitchGoalsApi>();
        goals
            .GetCreatorGoalsAsync(channel, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<TwitchCreatorGoal>>([
                    Goal("goal-followers", "follower", "Road to 1.5k", 1234, 1500),
                    Goal("goal-subs", "subscription", "Sub goal", 80, 100),
                    Goal("goal-new-subs", "new_subscription", "New subs today", 6, 20),
                ])
            );
        services.AddSingleton(goals);
    }

    private static TwitchCreatorGoal Goal(
        string id,
        string type,
        string description,
        int current,
        int target
    ) =>
        new(
            id,
            "555001",
            "SampleStreamer",
            "samplestreamer",
            type,
            description,
            current,
            target,
            new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero)
        );
}
