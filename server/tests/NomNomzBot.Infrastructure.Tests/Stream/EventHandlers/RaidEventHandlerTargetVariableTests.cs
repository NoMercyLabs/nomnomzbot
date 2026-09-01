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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Stream.Events;
using NomNomzBot.Infrastructure.Platform.Eventing;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Stream.EventHandlers;
using NomNomzBot.Infrastructure.Tests.Supporters;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Stream.EventHandlers;

/// <summary>
/// Regression for S-OWN11: an automated raid (no command argument) never had a <c>target</c> variable, so
/// <c>{target.name}</c> in a shoutout-on-raid pipeline/response silently resolved empty — the raid message
/// in chat was correct (it doesn't use <c>{target.*}</c>), but the shoutout announcement did not interpolate.
/// Drives the REAL handler path — <see cref="RaidEventHandler"/> → <see cref="TwitchAlertHandlerBase{TEvent}"/>
/// → the real <see cref="EventResponseExecutor"/> → the real <see cref="TemplateResolver"/> — and asserts the
/// literal chat message actually sent contains the raider's own name under <c>{target.name}</c>.
/// </summary>
public sealed class RaidEventHandlerTargetVariableTests
{
    private static readonly Guid Channel = Guid.Parse("0192c500-0000-7000-9000-0000000d4a1d");

    [Fact]
    public async Task A_raid_response_template_referencing_target_name_resolves_to_the_raiders_login()
    {
        using SupporterTestDbContext db = SupporterTestDbContext.New();
        db.EventResponses.Add(
            new()
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = Channel,
                EventType = "channel.raid",
                ResponseType = "chat_message",
                Message = "Shoutout to {target.name}!",
                IsEnabled = true,
            }
        );
        await db.SaveChangesAsync();

        IChatProvider chat = Substitute.For<IChatProvider>();
        chat.SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        services.AddSingleton<IChannelRegistry>(Substitute.For<IChannelRegistry>());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ILogger<TemplateResolver>>(NullLogger<TemplateResolver>.Instance);
        services.AddSingleton<ITemplateResolver, TemplateResolver>();
        services.AddSingleton(chat);
        services.AddSingleton<IEventResponseOverlayNotifier>(
            Substitute.For<IEventResponseOverlayNotifier>()
        );
        services.AddSingleton<IPipelineEngine>(Substitute.For<IPipelineEngine>());
        services.AddSingleton<ILogger<EventResponseExecutor>>(
            NullLogger<EventResponseExecutor>.Instance
        );
        services.AddSingleton<IEventResponseExecutor, EventResponseExecutor>();
        services.AddSingleton<ILogger<RaidEventHandler>>(NullLogger<RaidEventHandler>.Instance);
        ServiceProvider provider = services.BuildServiceProvider();

        RaidEventHandler handler = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IPipelineEngine>(),
            provider.GetRequiredService<ILogger<RaidEventHandler>>()
        );

        RaidEvent raid = new()
        {
            EventId = Guid.CreateVersion7(),
            BroadcasterId = Channel,
            FromUserId = "906093391",
            FromDisplayName = "HillForGames",
            FromLogin = "hillforgames",
            ViewerCount = 42,
        };

        await handler.HandleAsync(raid);

        await chat.Received(1)
            .SendMessageAsync(
                Channel,
                "Shoutout to hillforgames!", // {target.name} aliased from the raider's own {user.name}
                Arg.Any<CancellationToken>()
            );
    }
}
