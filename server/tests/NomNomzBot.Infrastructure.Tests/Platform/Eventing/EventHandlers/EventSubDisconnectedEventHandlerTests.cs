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
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Domain.Platform.Enums;
using NomNomzBot.Domain.Twitch.Events;
using NomNomzBot.Infrastructure.Platform.Eventing.EventHandlers;

namespace NomNomzBot.Infrastructure.Tests.Platform.Eventing.EventHandlers;

/// <summary>
/// S034: proves <see cref="EventSubDisconnectedEvent"/> — newly published by the transport on every unexpected
/// drop (S033) — actually reaches a consumer instead of being a fact nobody reads. The consumer here stamps a
/// diagnostic timestamp; it deliberately does NOT flip <c>Status</c> (a transient drop mid-backoff-retry is not
/// proof the token is dead — only a genuine <see cref="EventSubRevokedEvent"/> or the refresh-failure threshold
/// earns that).
/// </summary>
public sealed class EventSubDisconnectedEventHandlerTests
{
    private static readonly Guid Broadcaster = Guid.CreateVersion7();

    private static (EventSubDisconnectedEventHandler Handler, EventSubTestDbContext Db) Build()
    {
        EventSubTestDbContext db = EventSubTestDbContext.New();
        EventSubDisconnectedEventHandler handler = new(
            db,
            NullLogger<EventSubDisconnectedEventHandler>.Instance
        );
        return (handler, db);
    }

    private static void SeedConnection(EventSubTestDbContext db, Guid broadcaster)
    {
        db.IntegrationConnections.Add(
            new()
            {
                BroadcasterId = broadcaster,
                Provider = "twitch",
                ProviderAccountId = "twitch-1",
                Status = AuthEnums.IntegrationStatus.Connected,
            }
        );
        db.SaveChanges();
    }

    private static EventSubDisconnectedEvent DisconnectedEvent(Guid broadcaster) =>
        new()
        {
            BroadcasterId = broadcaster,
            Transport = EventSubTransportKind.WebSocket,
            SessionId = "sess-1",
            Reason = "closed by server (code Empty — none)",
            NextRetryIn = TimeSpan.FromSeconds(2),
        };

    [Fact]
    public async Task A_broadcaster_session_drop_stamps_last_error_at_without_changing_status()
    {
        (EventSubDisconnectedEventHandler handler, EventSubTestDbContext db) = Build();
        SeedConnection(db, Broadcaster);

        await handler.HandleAsync(DisconnectedEvent(Broadcaster));

        IntegrationConnection connection = db.IntegrationConnections.Single(c =>
            c.BroadcasterId == Broadcaster
        );
        connection.LastErrorAt.Should().NotBeNull("the drop is recorded for diagnostics");
        connection
            .Status.Should()
            .Be(
                AuthEnums.IntegrationStatus.Connected,
                "a transient drop mid-retry is not a dead token"
            );
    }

    [Fact]
    public async Task The_shared_bot_sessions_platform_sentinel_touches_no_connection_row()
    {
        (EventSubDisconnectedEventHandler handler, EventSubTestDbContext db) = Build();
        SeedConnection(db, Broadcaster);

        await handler.HandleAsync(DisconnectedEvent(Guid.Empty));

        db.IntegrationConnections.Single(c => c.BroadcasterId == Broadcaster)
            .LastErrorAt.Should()
            .BeNull("the bot session's drop names no single broadcaster to stamp");
    }
}
