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
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Domain.Twitch.Events;
using NomNomzBot.Infrastructure.Platform.Eventing.EventHandlers;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Eventing.EventHandlers;

/// <summary>
/// S034: proves the reactive half of EventSub revocation — the exact gap the finding described ("a revoked
/// authorization leaves the tenant looking fine while receiving nothing"). Every assertion is on a persisted
/// state change or a real side effect, never a log line.
/// </summary>
public sealed class EventSubRevokedEventHandlerTests
{
    private static readonly Guid Broadcaster = Guid.CreateVersion7();

    private static (
        EventSubRevokedEventHandler Handler,
        EventSubTestDbContext Db,
        IChatProvider Chat
    ) Build()
    {
        EventSubTestDbContext db = EventSubTestDbContext.New();
        IChatProvider chat = Substitute.For<IChatProvider>();
        chat.SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        FakeTimeProvider clock = new(new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero));
        EventSubRevokedEventHandler handler = new(
            db,
            chat,
            clock,
            NullLogger<EventSubRevokedEventHandler>.Instance
        );
        return (handler, db, chat);
    }

    private static void SeedConnection(EventSubTestDbContext db, Guid broadcaster, string status)
    {
        db.IntegrationConnections.Add(
            new()
            {
                BroadcasterId = broadcaster,
                Provider = "twitch",
                ProviderAccountId = "twitch-1",
                Status = status,
                ConsecutiveFailureCount = 0,
            }
        );
        db.SaveChanges();
    }

    private static EventSubRevokedEvent RevokedEvent(Guid broadcaster, string status) =>
        new()
        {
            BroadcasterId = broadcaster,
            TwitchSubscriptionId = "sub-1",
            EventType = "channel.follow",
            Status = status,
        };

    [Fact]
    public async Task Authorization_revoked_flips_the_connection_to_needs_reauth_and_notifies_the_streamer()
    {
        (EventSubRevokedEventHandler handler, EventSubTestDbContext db, IChatProvider chat) =
            Build();
        SeedConnection(db, Broadcaster, AuthEnums.IntegrationStatus.Connected);

        await handler.HandleAsync(RevokedEvent(Broadcaster, "authorization_revoked"));

        IntegrationConnection connection = db.IntegrationConnections.Single(c =>
            c.BroadcasterId == Broadcaster
        );
        connection.Status.Should().Be(AuthEnums.IntegrationStatus.NeedsReauth);
        connection.ConsecutiveFailureCount.Should().BeGreaterThanOrEqualTo(1);
        connection.LastErrorAt.Should().NotBeNull();

        await chat.Received(1)
            .SendMessageAsync(Broadcaster, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task User_removed_also_flips_the_connection_to_needs_reauth()
    {
        (EventSubRevokedEventHandler handler, EventSubTestDbContext db, _) = Build();
        SeedConnection(db, Broadcaster, AuthEnums.IntegrationStatus.Connected);

        await handler.HandleAsync(RevokedEvent(Broadcaster, "user_removed"));

        db.IntegrationConnections.Single(c => c.BroadcasterId == Broadcaster)
            .Status.Should()
            .Be(AuthEnums.IntegrationStatus.NeedsReauth);
    }

    [Fact]
    public async Task Version_removed_leaves_the_connection_untouched()
    {
        // version_removed is API-version housekeeping, not a lost authorization — flipping the connection here
        // would falsely tell the streamer to reconnect when the token is still perfectly valid.
        (EventSubRevokedEventHandler handler, EventSubTestDbContext db, IChatProvider chat) =
            Build();
        SeedConnection(db, Broadcaster, AuthEnums.IntegrationStatus.Connected);

        await handler.HandleAsync(RevokedEvent(Broadcaster, "version_removed"));

        db.IntegrationConnections.Single(c => c.BroadcasterId == Broadcaster)
            .Status.Should()
            .Be(AuthEnums.IntegrationStatus.Connected);
        await chat.DidNotReceive()
            .SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_already_needs_reauth_connection_is_not_re_notified()
    {
        // Idempotent: a second revocation frame for an already-flagged connection must not spam a second chat
        // notice or bump the failure counter again.
        (EventSubRevokedEventHandler handler, EventSubTestDbContext db, IChatProvider chat) =
            Build();
        SeedConnection(db, Broadcaster, AuthEnums.IntegrationStatus.NeedsReauth);

        await handler.HandleAsync(RevokedEvent(Broadcaster, "authorization_revoked"));

        await chat.DidNotReceive()
            .SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_platform_sentinel_broadcaster_is_ignored()
    {
        (EventSubRevokedEventHandler handler, EventSubTestDbContext db, IChatProvider chat) =
            Build();

        await handler.HandleAsync(RevokedEvent(Guid.Empty, "authorization_revoked"));

        db.IntegrationConnections.Should().BeEmpty();
        await chat.DidNotReceive()
            .SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
