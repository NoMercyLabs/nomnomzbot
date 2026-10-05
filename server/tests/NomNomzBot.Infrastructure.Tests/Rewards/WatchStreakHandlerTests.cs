// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Infrastructure.Rewards.EventHandlers;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Rewards;

/// <summary>
/// Proves a real Twitch watch-streak notification dispatches through the SAME "engagement.watch_streak"
/// key the dashboard's event-response preset catalog exposes — the bug was a mismatched EventTypeKey
/// ("watch_streak" bare) that no configured EventResponse could ever match, so a real redemption fired
/// into a dead end (commands-pipelines.md's shared IEventResponseExecutor path never even ran).
/// </summary>
public sealed class WatchStreakHandlerTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000d101");

    private static (IServiceScopeFactory Scopes, IEventResponseExecutor Executor) Harness()
    {
        IEventResponseExecutor executor = Substitute.For<IEventResponseExecutor>();
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IApplicationDbContext>(AuthTestBuilder.NewContext())
            .AddSingleton(executor)
            .BuildServiceProvider();
        return (provider.GetRequiredService<IServiceScopeFactory>(), executor);
    }

    [Fact]
    public async Task A_watch_streak_notification_dispatches_via_the_dashboard_configurable_key()
    {
        (IServiceScopeFactory scopes, IEventResponseExecutor executor) = Harness();
        WatchStreakHandler handler = new(
            scopes,
            Substitute.For<IPipelineEngine>(),
            TimeProvider.System,
            NullLogger<WatchStreakHandler>.Instance
        );

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = Channel,
                OccurredAt = DateTimeOffset.UtcNow,
                UserId = "777",
                UserLogin = "coffeethencode",
                UserDisplayName = "CoffeeThenCode",
                StreakMonths = 4,
                ChannelPointsEarned = 350,
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
                    v["streak.months"] == "4" && v["streak.points"] == "350"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    private static async Task<(Dictionary<string, string> Variables, int MaxStreakAfter)> RunAsync(
        int? storedMax,
        int newStreak
    )
    {
        IEventResponseExecutor executor = Substitute.For<IEventResponseExecutor>();
        IApplicationDbContext db = AuthTestBuilder.NewContext();
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton(db)
            .AddSingleton(executor)
            .BuildServiceProvider();
        if (storedMax is not null)
        {
            db.WatchStreaks.Add(
                new()
                {
                    Id = Guid.NewGuid(),
                    BroadcasterId = Channel,
                    UserId = "777",
                    UserDisplayName = "CoffeeThenCode",
                    CurrentStreak = storedMax.Value,
                    MaxStreak = storedMax.Value,
                    LastSeenDate = new(2026, 1, 1),
                }
            );
            await db.SaveChangesAsync();
        }

        WatchStreakHandler handler = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IPipelineEngine>(),
            TimeProvider.System,
            NullLogger<WatchStreakHandler>.Instance
        );

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = Channel,
                OccurredAt = DateTimeOffset.UtcNow,
                UserId = "777",
                UserLogin = "coffeethencode",
                UserDisplayName = "CoffeeThenCode",
                StreakMonths = newStreak,
                ChannelPointsEarned = 350,
            }
        );

        Dictionary<string, string> variables =
            (Dictionary<string, string>)executor.ReceivedCalls().Single().GetArguments()[4]!;
        int maxAfter = await db
            .WatchStreaks.Where(w => w.BroadcasterId == Channel && w.UserId == "777")
            .Select(w => w.MaxStreak)
            .SingleAsync();
        return (variables, maxAfter);
    }

    [Fact]
    public async Task A_streak_above_the_stored_max_is_a_new_record_and_raises_the_max()
    {
        (Dictionary<string, string> v, int maxAfter) = await RunAsync(storedMax: 5, newStreak: 8);

        Assert.Equal("5", v["streak.record"]);
        Assert.Equal("new_record", v["streak.state"]);
        Assert.Equal("8", v["streak.months"]);
        Assert.Equal(8, maxAfter);
    }

    [Fact]
    public async Task A_streak_below_the_stored_max_is_rebuilt_and_keeps_the_max()
    {
        (Dictionary<string, string> v, int maxAfter) = await RunAsync(storedMax: 9, newStreak: 3);

        Assert.Equal("9", v["streak.record"]);
        Assert.Equal("rebuilt", v["streak.state"]);
        Assert.Equal(9, maxAfter);
    }

    [Fact]
    public async Task A_first_ever_streak_is_standard_with_record_zero()
    {
        (Dictionary<string, string> v, int maxAfter) = await RunAsync(
            storedMax: null,
            newStreak: 4
        );

        Assert.Equal("0", v["streak.record"]);
        Assert.Equal("standard", v["streak.state"]);
        Assert.Equal(4, maxAfter);
    }

    [Fact]
    public async Task A_streak_equal_to_the_stored_max_is_standard()
    {
        (Dictionary<string, string> v, int maxAfter) = await RunAsync(storedMax: 6, newStreak: 6);

        Assert.Equal("6", v["streak.record"]);
        Assert.Equal("standard", v["streak.state"]);
        Assert.Equal(6, maxAfter);
    }
}
