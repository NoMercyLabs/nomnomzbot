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
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Rewards.Events;
using NomNomzBot.Infrastructure.Rewards.EventHandlers;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Rewards;

/// <summary>
/// S022 done-when: a Kick subscription fires the SAME <c>channel.subscribe</c> event response Twitch
/// subscriptions fire, and the response's variables see <c>Provider=kick</c> — proving Kick and Twitch
/// subs are one canonical event, distinguishable only by <see cref="NewSubscriptionEvent.Provider"/>
/// (supporter-events.md §4.1), not two parallel per-platform events.
/// </summary>
public sealed class NewSubscriptionEventHandlerTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000d501");

    private sealed record Harness(
        NewSubscriptionEventHandler Handler,
        IEventResponseExecutor Executor
    );

    private static Harness Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        IEventResponseExecutor executor = Substitute.For<IEventResponseExecutor>();

        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IApplicationDbContext>(db)
            .AddSingleton(executor)
            .BuildServiceProvider();

        NewSubscriptionEventHandler handler = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IPipelineEngine>(),
            NullLogger<NewSubscriptionEventHandler>.Instance
        );

        return new(handler, executor);
    }

    [Fact]
    public async Task A_kick_subscription_fires_the_channel_subscribe_response_with_provider_kick()
    {
        Harness h = Build();
        NewSubscriptionEvent kickSub = new()
        {
            BroadcasterId = Channel,
            UserId = "kick-888",
            UserDisplayName = "SubGuy",
            Tier = "1000",
            Provider = AuthEnums.Platform.Kick,
        };

        await h.Handler.HandleAsync(kickSub);

        // The consequence: the SAME "channel.subscribe" trigger key Twitch subs fire — one operator
        // config, one Alert surface — with the delivering platform readable off the variables the
        // response (and its templates) actually receive.
        await h
            .Executor.Received(1)
            .ExecuteAsync(
                Channel,
                "channel.subscribe",
                "kick-888",
                "SubGuy",
                Arg.Is<Dictionary<string, string>>(v =>
                    v["provider"] == AuthEnums.Platform.Kick && v["user"] == "SubGuy"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_twitch_subscription_still_fires_the_same_response_with_provider_twitch()
    {
        Harness h = Build();
        NewSubscriptionEvent twitchSub = new()
        {
            BroadcasterId = Channel,
            UserId = "tw-999",
            UserDisplayName = "SubGal",
            Tier = "2000",
        };

        await h.Handler.HandleAsync(twitchSub);

        await h
            .Executor.Received(1)
            .ExecuteAsync(
                Channel,
                "channel.subscribe",
                "tw-999",
                "SubGal",
                Arg.Is<Dictionary<string, string>>(v => v["provider"] == AuthEnums.Platform.Twitch),
                Arg.Any<CancellationToken>()
            );
    }
}
