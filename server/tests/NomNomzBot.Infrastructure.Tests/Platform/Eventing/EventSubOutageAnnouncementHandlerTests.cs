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
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Enums;
using NomNomzBot.Domain.Twitch.Events;
using NomNomzBot.Infrastructure.Platform.Eventing;
using NomNomzBot.Infrastructure.Platform.Eventing.EventHandlers;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Eventing;

/// <summary>
/// The outage chat lines: one drop line per unplanned outage (a retry storm fires many disconnect events),
/// one recovery line only to the chats that heard the drop, and nothing for a planned
/// <c>session_reconnect</c> handoff (that path fires a connected event with no disconnect before it).
/// </summary>
public sealed class EventSubOutageAnnouncementHandlerTests
{
    private const string LostText =
        "Lost connection to Twitch events — channel point redeems and commands are paused while I reconnect. Hang tight!";
    private const string RestoredText =
        "Reconnected! Channel point redeems and commands are working again.";

    private readonly ConcurrentQueue<(Guid Channel, string Message)> _sent = [];
    private readonly EventSubOutageLedger _ledger = new();

    [Fact]
    public async Task Two_quick_failures_post_one_drop_line_and_the_recovery_posts_one_line()
    {
        Guid live = Guid.NewGuid();
        EventSubOutageAnnouncementHandler handler = Build(Channel(live, live: true));

        await handler.HandleAsync(Dropped(Guid.Empty), CancellationToken.None);
        await handler.HandleAsync(Dropped(Guid.Empty), CancellationToken.None);

        _sent.Should().ContainSingle().Which.Should().Be((live, LostText));

        await handler.HandleAsync(Connected(Guid.Empty), CancellationToken.None);
        await handler.HandleAsync(Connected(Guid.Empty), CancellationToken.None);

        _sent.Should().HaveCount(2);
        _sent.Last().Should().Be((live, RestoredText));
    }

    [Fact]
    public async Task A_planned_reconnect_welcome_posts_nothing()
    {
        EventSubOutageAnnouncementHandler handler = Build(Channel(Guid.NewGuid(), live: true));

        await handler.HandleAsync(Connected(Guid.Empty), CancellationToken.None);

        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task A_shared_session_drop_tells_every_live_channel_and_recovery_tells_the_same_ones()
    {
        Guid liveA = Guid.NewGuid();
        Guid liveB = Guid.NewGuid();
        Guid offline = Guid.NewGuid();
        EventSubOutageAnnouncementHandler handler = Build(
            Channel(liveA, live: true),
            Channel(liveB, live: true),
            Channel(offline, live: false)
        );

        await handler.HandleAsync(Dropped(Guid.Empty), CancellationToken.None);
        await handler.HandleAsync(Connected(Guid.Empty), CancellationToken.None);

        _sent
            .Where(s => s.Message == LostText)
            .Select(s => s.Channel)
            .Should()
            .BeEquivalentTo([liveA, liveB]);
        _sent
            .Where(s => s.Message == RestoredText)
            .Select(s => s.Channel)
            .Should()
            .BeEquivalentTo([liveA, liveB]);
    }

    [Fact]
    public async Task A_broadcaster_session_drop_tells_only_that_channel()
    {
        Guid dropped = Guid.NewGuid();
        Guid other = Guid.NewGuid();
        EventSubOutageAnnouncementHandler handler = Build(
            Channel(dropped, live: true),
            Channel(other, live: true)
        );

        await handler.HandleAsync(Dropped(dropped), CancellationToken.None);
        await handler.HandleAsync(Connected(dropped), CancellationToken.None);

        _sent.Should().Equal([(dropped, LostText), (dropped, RestoredText)]);
    }

    [Fact]
    public async Task A_channel_that_never_heard_the_drop_hears_no_recovery()
    {
        Guid offline = Guid.NewGuid();
        EventSubOutageAnnouncementHandler handler = Build(Channel(offline, live: false));

        await handler.HandleAsync(Dropped(Guid.Empty), CancellationToken.None);
        await handler.HandleAsync(Connected(Guid.Empty), CancellationToken.None);

        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task The_line_is_spoken_in_the_channels_tone()
    {
        Guid sassy = Guid.NewGuid();
        EventSubOutageAnnouncementHandler handler = Build(
            Channel(sassy, live: true, PersonalityTone.Sassy)
        );

        await handler.HandleAsync(Dropped(Guid.Empty), CancellationToken.None);

        _sent
            .Single()
            .Message.Should()
            .BeOneOf(
                ToneTemplateCatalog.Get(
                    PersonalityTone.Sassy,
                    BuiltinResponseSlots.BotStatus.Key,
                    BuiltinResponseSlots.BotStatus.ConnectionLost
                )
            );
        _sent.Single().Message.Should().NotBe(LostText);
    }

    private static EventSubDisconnectedEvent Dropped(Guid owner) =>
        new()
        {
            BroadcasterId = owner,
            Transport = EventSubTransportKind.WebSocket,
            SessionId = "s1",
            Reason = "keepalive timeout",
            NextRetryIn = TimeSpan.FromSeconds(1),
        };

    private static EventSubConnectedEvent Connected(Guid owner) =>
        new()
        {
            BroadcasterId = owner,
            Transport = EventSubTransportKind.WebSocket,
            SessionId = "s2",
            ActiveSubscriptionCount = 3,
        };

    private EventSubOutageAnnouncementHandler Build(params Channel[] channels)
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.AddRange(channels);
        db.SaveChanges();

        IBuiltinResponseComposer composer = Substitute.For<IBuiltinResponseComposer>();
        composer
            .ComposeAsync(Arg.Any<BuiltinResponseRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                BuiltinResponseRequest request = call.ArgAt<BuiltinResponseRequest>(0);
                return Task.FromResult(
                    ToneTemplateCatalog.Pick(request.Personality, request.BuiltinKey, request.Slot)
                        ?? request.NeutralFallback
                );
            });

        IChatProvider chat = Substitute.For<IChatProvider>();
        chat.SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _sent.Enqueue((call.ArgAt<Guid>(0), call.ArgAt<string>(1)));
                return Task.FromResult(true);
            });

        return new(
            _ledger,
            db,
            composer,
            chat,
            NullLogger<EventSubOutageAnnouncementHandler>.Instance
        );
    }

    private static Channel Channel(
        Guid id,
        bool live,
        string personality = PersonalityTone.Informative
    ) =>
        new()
        {
            Id = id,
            OwnerUserId = Guid.NewGuid(),
            Name = $"chan{id:N}",
            NameNormalized = $"chan{id:N}",
            Enabled = true,
            IsOnboarded = true,
            IsLive = live,
            Status = AuthEnums.ChannelStatus.Active,
            Personality = personality,
        };
}
