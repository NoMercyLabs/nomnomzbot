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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Infrastructure.Chat;
using NomNomzBot.Infrastructure.Moderation.EventHandlers;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// Proves the OUTGOING shared-ban gate (moderation.md §3.5): a ban is offered to the trust web ONLY when the
/// origin channel is live in a shared-chat session AND opted in to sharing — a plain ban outside a session,
/// or one from a non-sharing channel, publishes nothing.
/// </summary>
public sealed class SharedChatBanSharePublisherTests
{
    private static readonly Guid Origin = Guid.Parse("0192a000-0000-7000-8000-00000000bb01");

    private static UserBannedEvent Ban() =>
        new()
        {
            BroadcasterId = Origin,
            TargetUserId = "troll-42",
            TargetDisplayName = "Troll",
            ModeratorUserId = "mod-7",
            Reason = "spam",
        };

    private static SharedChatSessionRestorer NoSessionAtTwitch(
        ModerationServiceTestDbContext db,
        SharedChatSessionTracker sessions
    )
    {
        ITwitchChatAssetsApi helix = Substitute.For<ITwitchChatAssetsApi>();
        helix
            .GetSharedChatSessionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<TwitchSharedChatSession>("none", TwitchErrorCodes.NotFound));
        return new(sessions, helix, db, NullLogger<SharedChatSessionRestorer>.Instance);
    }

    [Fact]
    public async Task A_ban_in_a_session_from_a_sharing_channel_is_offered_with_the_session_id()
    {
        ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        db.SharedBanSettings.Add(new() { BroadcasterId = Origin, ShareOutgoingBans = true });
        await db.SaveChangesAsync();
        SharedChatSessionTracker sessions = new();
        sessions.SetSession(Origin, new("session-9", "host-1", []));
        RecordingEventBus bus = new();

        await new SharedChatBanSharePublisher(db, NoSessionAtTwitch(db, sessions), bus).HandleAsync(
            Ban()
        );

        SharedChatBanIssuedEvent published = bus
            .Published.OfType<SharedChatBanIssuedEvent>()
            .Single();
        published.SharedChatSessionId.Should().Be("session-9");
        published.OriginChannelId.Should().Be(Origin);
        published.TargetTwitchUserId.Should().Be("troll-42");
    }

    [Fact]
    public async Task A_ban_outside_a_session_or_without_the_sharing_opt_in_offers_nothing()
    {
        ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        SharedChatSessionTracker sessions = new();
        RecordingEventBus bus = new();
        SharedChatBanSharePublisher sut = new(db, NoSessionAtTwitch(db, sessions), bus);

        // No active session (sharing state irrelevant).
        await sut.HandleAsync(Ban());
        bus.Published.Should().BeEmpty();

        // In a session, but the channel never opted in to sharing (no settings row = share OFF).
        sessions.SetSession(Origin, new("session-9", "host-1", []));
        await sut.HandleAsync(Ban());
        bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task A_sharing_channel_that_Twitch_says_is_in_no_session_offers_nothing()
    {
        ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        db.SharedBanSettings.Add(new() { BroadcasterId = Origin, ShareOutgoingBans = true });
        await db.SaveChangesAsync();
        SharedChatSessionTracker sessions = new();
        RecordingEventBus bus = new();

        await new SharedChatBanSharePublisher(db, NoSessionAtTwitch(db, sessions), bus).HandleAsync(
            Ban()
        );

        bus.Published.Should().BeEmpty();
    }
}
