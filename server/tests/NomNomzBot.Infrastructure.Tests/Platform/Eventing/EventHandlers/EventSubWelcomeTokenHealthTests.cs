// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Domain.Platform.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Twitch.Events;
using NomNomzBot.Infrastructure.Platform;

namespace NomNomzBot.Infrastructure.Tests.Platform.Eventing.EventHandlers;

/// <summary>
/// A WebSocket welcome needs no user token, so it proves nothing about the broadcaster's grant. Every
/// <see cref="EventSubConnectedEvent"/> handler the production scan binds is run against a connection marked
/// <c>needs_reauth</c>: the flag, the failure count and the error stamp must all survive, or the status
/// oscillates between healthy and dead on every reconnect.
/// </summary>
public sealed class EventSubWelcomeTokenHealthTests
{
    private static readonly Guid Broadcaster = Guid.CreateVersion7();
    private static readonly DateTime FailedAt = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task A_broadcaster_welcome_leaves_a_needs_reauth_connection_marked()
    {
        await using EventSubTestDbContext db = EventSubTestDbContext.New();
        db.IntegrationConnections.Add(
            new()
            {
                BroadcasterId = Broadcaster,
                Provider = AuthEnums.IntegrationProvider.Twitch,
                ProviderAccountId = "twitch-1",
                Status = AuthEnums.IntegrationStatus.NeedsReauth,
                ConsecutiveFailureCount = 3,
                LastErrorAt = FailedAt,
            }
        );
        await db.SaveChangesAsync();

        await PublishThroughProductionHandlersAsync(
            db,
            new()
            {
                BroadcasterId = Broadcaster,
                Transport = EventSubTransportKind.WebSocket,
                SessionId = "sess-1",
                ActiveSubscriptionCount = 3,
            }
        );

        db.ChangeTracker.Clear();
        IntegrationConnection connection = db.IntegrationConnections.Single();
        connection.Status.Should().Be(AuthEnums.IntegrationStatus.NeedsReauth);
        connection.ConsecutiveFailureCount.Should().Be(3);
        connection.LastErrorAt.Should().Be(FailedAt);
    }

    private static async Task PublishThroughProductionHandlersAsync(
        EventSubTestDbContext db,
        EventSubConnectedEvent welcome
    )
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IApplicationDbContext>(db);
        services.AddOpenGenericHandlers(
            typeof(DependencyInjection).Assembly,
            typeof(IEventHandler<>),
            ServiceLifetime.Scoped
        );

        await using ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        foreach (
            IEventHandler<EventSubConnectedEvent> handler in scope.ServiceProvider.GetServices<
                IEventHandler<EventSubConnectedEvent>
            >()
        )
            await handler.HandleAsync(welcome);
    }
}
