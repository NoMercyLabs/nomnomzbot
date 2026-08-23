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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Domain.Platform.Enums;
using NomNomzBot.Domain.Twitch.Events;
using NomNomzBot.Infrastructure.Platform.Eventing.EventHandlers;

namespace NomNomzBot.Infrastructure.Tests.Platform.Eventing.EventHandlers;

/// <summary>
/// S034: proves the self-heal half of the two previously-unhandled EventSub lifecycle events — a broadcaster's
/// OWN WebSocket session welcoming again can only happen on a token Twitch just accepted, so a stale
/// <c>needs_reauth</c> is cleared without asking the operator to reconnect (the "never force a re-login" rule).
/// </summary>
public sealed class EventSubConnectedEventHandlerTests
{
    private static readonly Guid Broadcaster = Guid.CreateVersion7();

    private static (EventSubConnectedEventHandler Handler, EventSubTestDbContext Db) Build()
    {
        EventSubTestDbContext db = EventSubTestDbContext.New();
        FakeTimeProvider clock = new(new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero));
        EventSubConnectedEventHandler handler = new(
            db,
            clock,
            NullLogger<EventSubConnectedEventHandler>.Instance
        );
        return (handler, db);
    }

    private static void SeedConnection(
        EventSubTestDbContext db,
        Guid broadcaster,
        string status,
        int failures
    )
    {
        db.IntegrationConnections.Add(
            new()
            {
                BroadcasterId = broadcaster,
                Provider = "twitch",
                ProviderAccountId = "twitch-1",
                Status = status,
                ConsecutiveFailureCount = failures,
                LastErrorAt = DateTime.UtcNow,
            }
        );
        db.SaveChanges();
    }

    private static EventSubConnectedEvent ConnectedEvent(Guid broadcaster) =>
        new()
        {
            BroadcasterId = broadcaster,
            Transport = EventSubTransportKind.WebSocket,
            SessionId = "sess-1",
            ActiveSubscriptionCount = 3,
        };

    [Fact]
    public async Task A_broadcaster_session_welcome_clears_a_stale_needs_reauth()
    {
        (EventSubConnectedEventHandler handler, EventSubTestDbContext db) = Build();
        SeedConnection(db, Broadcaster, AuthEnums.IntegrationStatus.NeedsReauth, failures: 3);

        await handler.HandleAsync(ConnectedEvent(Broadcaster));

        IntegrationConnection connection = db.IntegrationConnections.Single(c =>
            c.BroadcasterId == Broadcaster
        );
        connection.Status.Should().Be(AuthEnums.IntegrationStatus.Connected);
        connection.ConsecutiveFailureCount.Should().Be(0);
        connection.LastErrorAt.Should().BeNull();
    }

    [Fact]
    public async Task An_already_healthy_connection_is_left_untouched()
    {
        (EventSubConnectedEventHandler handler, EventSubTestDbContext db) = Build();
        SeedConnection(db, Broadcaster, AuthEnums.IntegrationStatus.Connected, failures: 0);

        await handler.HandleAsync(ConnectedEvent(Broadcaster));

        db.IntegrationConnections.Single(c => c.BroadcasterId == Broadcaster)
            .Status.Should()
            .Be(AuthEnums.IntegrationStatus.Connected);
    }

    [Fact]
    public async Task The_shared_bot_sessions_platform_sentinel_is_ignored()
    {
        // The bot-owned session carries every channel's chat-read topics — it names no single tenant, so a
        // welcome on it must never be misread as "broadcaster X's token is fine".
        (EventSubConnectedEventHandler handler, EventSubTestDbContext db) = Build();
        SeedConnection(db, Broadcaster, AuthEnums.IntegrationStatus.NeedsReauth, failures: 3);

        await handler.HandleAsync(ConnectedEvent(Guid.Empty));

        db.IntegrationConnections.Single(c => c.BroadcasterId == Broadcaster)
            .Status.Should()
            .Be(
                AuthEnums.IntegrationStatus.NeedsReauth,
                "the platform-sentinel welcome names no tenant"
            );
    }
}
