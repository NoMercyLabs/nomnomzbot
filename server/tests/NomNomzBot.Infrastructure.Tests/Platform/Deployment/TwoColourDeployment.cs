// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.DTOs.Twitch.EventSub;
using NomNomzBot.Domain.Platform.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Eventing;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Deployment;

/// <summary>
/// A blue/green switchover in a test: two REAL EventSub hosts (the production <see cref="IActiveInstanceGate"/>)
/// over one shared lease store, the way two API colours contend over one database. <see cref="Blue"/> boots
/// first and takes the chat-ingest lease; <see cref="Green"/> boots second and waits as the standby.
/// <see cref="HandOverAsync"/> stops Blue and returns once Green has taken the lease over.
/// </summary>
internal sealed class TwoColourDeployment
{
    private TwoColourDeployment(TwitchEventSubHostedService blue, TwitchEventSubHostedService green)
    {
        Blue = blue;
        Green = green;
    }

    /// <summary>The outgoing colour: the lease holder until <see cref="HandOverAsync"/>.</summary>
    public TwitchEventSubHostedService Blue { get; }

    /// <summary>The incoming colour: a standby until <see cref="HandOverAsync"/>.</summary>
    public TwitchEventSubHostedService Green { get; }

    public static async Task<TwoColourDeployment> StartAsync()
    {
        ConcurrentDictionary<string, byte> sharedLeases = new();
        TwitchEventSubHostedService blue = BuildColour(sharedLeases);
        TwitchEventSubHostedService green = BuildColour(sharedLeases);

        await blue.StartAsync(CancellationToken.None);
        await green.StartAsync(CancellationToken.None);
        return new(blue, green);
    }

    /// <summary>Blue stops (a deploy draining it); returns once Green holds the lease and is active.</summary>
    public async Task HandOverAsync()
    {
        await Blue.StopAsync(CancellationToken.None);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));
        await Green.WaitUntilActiveAsync(timeout.Token);
    }

    public Task StopAsync() => Green.StopAsync(CancellationToken.None);

    private static TwitchEventSubHostedService BuildColour(
        ConcurrentDictionary<string, byte> sharedLeases
    )
    {
        IPlatformBotReadinessGate configured = Substitute.For<IPlatformBotReadinessGate>();
        configured.IsPlatformBotConfiguredAsync(Arg.Any<CancellationToken>()).Returns(true);

        ServiceProvider provider = new ServiceCollection()
            .AddSingleton(configured)
            .AddScoped<IRunOnceGuard>(_ => new SharedFakeRunOnceGuard(sharedLeases))
            .BuildServiceProvider();

        IEventSubTransport transport = Substitute.For<IEventSubTransport>();
        transport
            .StartAsync(Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new EventSubTransportHandle
                    {
                        Kind = EventSubTransportKind.WebSocket,
                        SessionId = Guid.NewGuid().ToString(),
                    }
                )
            );

        return new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            transport,
            new EventSubConditionBuilder(),
            Substitute.For<IEventBus>(),
            TimeProvider.System,
            NullLogger<TwitchEventSubHostedService>.Instance
        );
    }
}
