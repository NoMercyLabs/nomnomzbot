// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Infrastructure.Engagement.EventHandlers;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Engagement;

/// <summary>
/// The bot's own "consecutive streams" milestone and Twitch's watch-streak notification share one response key,
/// "engagement.watch_streak", so one saved message serves both. The milestone supplied only {user},
/// {engagement.streak} and two ids, so a message written with {user.name}, {streak.months} or {streak.message}
/// went to chat with the braces still in it.
/// </summary>
public sealed class WatchStreakTriggerSourceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000d102");

    [Fact]
    public async Task A_milestone_fills_every_placeholder_the_twitch_watch_streak_message_uses()
    {
        IEventResponseExecutor executor = Substitute.For<IEventResponseExecutor>();
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IApplicationDbContext>(AuthTestBuilder.NewContext())
            .AddSingleton(executor)
            .BuildServiceProvider();
        WatchStreakTriggerSource source = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IPipelineEngine>(),
            NullLogger<WatchStreakTriggerSource>.Instance
        );

        await source.HandleAsync(
            new()
            {
                BroadcasterId = Channel,
                ViewerUserId = Guid.NewGuid(),
                ViewerExternalUserId = "777",
                ViewerDisplayName = "CoffeeThenCode",
                StreakCount = 5,
                OccurredAt = DateTimeOffset.UtcNow,
            }
        );

        await executor
            .Received(1)
            .ExecuteAsync(
                Channel,
                "engagement.watch_streak",
                "777",
                "CoffeeThenCode",
                Arg.Is<Dictionary<string, string>>(v =>
                    v["user.name"] == "CoffeeThenCode"
                    && v["streak.months"] == "5"
                    && v["engagement.streak"] == "5"
                    && v["streak.message"] == string.Empty
                    && v.ContainsKey("streak.points")
                    && v.ContainsKey("streak.record")
                    && v.ContainsKey("streak.state")
                ),
                Arg.Any<CancellationToken>()
            );
    }
}
