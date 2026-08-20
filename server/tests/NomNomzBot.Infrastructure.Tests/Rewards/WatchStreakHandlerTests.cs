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
using NomNomzBot.Domain.Rewards.Events;
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
            new WatchStreakReceivedEvent
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
}
