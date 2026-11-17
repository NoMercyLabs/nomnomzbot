// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Stream.Events;
using NomNomzBot.Infrastructure.Platform.Eventing;
using NomNomzBot.Infrastructure.Stream;
using NomNomzBot.Infrastructure.Stream.EventHandlers;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Stream;

/// <summary>
/// Proves the backstop half of the 2026-08-27 watch-time inflation fix: if a channel goes live again while a
/// PREVIOUS Stream row for it is still open (its own stream.offline was missed and the poll reconciler hasn't
/// caught up yet — e.g. right after a process restart), ChannelOnlineHandler must close that stale row itself
/// rather than leaving it open forever (see StreamStatusPollingServiceTests for the poll-side backstop).
/// </summary>
public sealed class ChannelOnlineHandlerTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192f000-0000-7000-8000-0000000000c1");
    private static readonly Guid Owner = Guid.Parse("0192f000-0000-7000-8000-0000000000c9");

    private static (
        ChannelOnlineHandler Sut,
        AuthDbContext Db,
        LiveStateCapturingNotifier Inbox
    ) Build(ChannelContext? ctx = null, IShoutoutQueue? shoutoutQueue = null)
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            new()
            {
                Id = Broadcaster,
                OwnerUserId = Owner,
                Provider = AuthEnums.Platform.Twitch,
                ExternalChannelId = "tw-9",
                TwitchChannelId = "tw-9",
                Name = "streamer9",
                NameNormalized = "streamer9",
                IsOnboarded = true,
                DeploymentMode = AuthEnums.DeploymentMode.Saas,
                BillingTierKey = "free",
            }
        );
        db.SaveChanges();

        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry
            .GetOrCreateAsync(Broadcaster, "tw-9", "streamer9", Arg.Any<CancellationToken>())
            .Returns(
                ctx
                    ?? new ChannelContext
                    {
                        BroadcasterId = Broadcaster,
                        TwitchChannelId = "tw-9",
                        ChannelName = "streamer9",
                    }
            );

        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IApplicationDbContext>(db)
            .AddSingleton(Substitute.For<IEventResponseExecutor>())
            .AddSingleton(Substitute.For<ITwitchStreamsApi>())
            .BuildServiceProvider();

        LiveStateCapturingNotifier inbox = new(db);
        ChannelOnlineHandler sut = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            registry,
            inbox,
            shoutoutQueue ?? new ShoutoutQueue(),
            new ChannelOnboardingGate(provider.GetRequiredService<IServiceScopeFactory>()),
            NullLogger<ChannelOnlineHandler>.Instance
        );
        return (sut, db, inbox);
    }

    [Fact]
    public async Task A_stale_open_stream_from_a_missed_offline_is_closed_when_the_channel_goes_live_again()
    {
        (ChannelOnlineHandler sut, AuthDbContext db, _) = Build();
        DateTimeOffset staleStart = new(2026, 8, 20, 10, 0, 0, TimeSpan.Zero);
        db.Streams.Add(
            new()
            {
                Id = "stale-stream",
                ChannelId = Broadcaster,
                StartedAt = staleStart,
                EndedAt = null, // never closed — the missed stream.offline
            }
        );
        db.ChatMessages.Add(ChatAt("c1", staleStart.AddHours(1)));
        db.ChatMessages.Add(ChatAt("c2", staleStart.AddHours(3)));
        db.ChatMessages.Add(ChatAt("c3", new DateTimeOffset(2026, 8, 27, 19, 0, 0, TimeSpan.Zero)));
        await db.SaveChangesAsync();

        DateTimeOffset newStart = new(2026, 8, 27, 18, 0, 0, TimeSpan.Zero);
        await sut.HandleAsync(
            new ChannelOnlineEvent
            {
                Provider = AuthEnums.Platform.Twitch,
                BroadcasterId = Broadcaster,
                BroadcasterDisplayName = "Streamer9",
                StreamTitle = "New session",
                GameName = "Just Chatting",
                StartedAt = newStart,
            }
        );

        NomNomzBot.Domain.Stream.Entities.Stream stale = await db.Streams.SingleAsync(s =>
            s.Id == "stale-stream"
        );
        stale
            .EndedAt.Should()
            .Be(staleStart.AddHours(3), "the last chat before the new stream is the provable end");

        List<NomNomzBot.Domain.Stream.Entities.Stream> all = await db
            .Streams.Where(s => s.ChannelId == Broadcaster)
            .ToListAsync();
        all.Should().HaveCount(2, "the new stream is created alongside the now-closed stale one");
        all.Should()
            .ContainSingle(
                s => s.Id != "stale-stream" && s.EndedAt == null,
                "the new stream stays open"
            );
    }

    [Fact]
    public async Task A_stale_open_stream_with_no_chat_in_its_window_closes_at_its_start_instead_of_an_invented_end()
    {
        (ChannelOnlineHandler sut, AuthDbContext db, _) = Build();
        DateTimeOffset staleStart = new(2026, 8, 20, 10, 0, 0, TimeSpan.Zero);
        db.Streams.Add(
            new()
            {
                Id = "stale-stream",
                ChannelId = Broadcaster,
                StartedAt = staleStart,
            }
        );
        await db.SaveChangesAsync();

        await sut.HandleAsync(
            new ChannelOnlineEvent
            {
                Provider = AuthEnums.Platform.Twitch,
                BroadcasterId = Broadcaster,
                BroadcasterDisplayName = "Streamer9",
                StreamTitle = "New session",
                GameName = "Just Chatting",
                StartedAt = new(2026, 8, 27, 18, 0, 0, TimeSpan.Zero),
            }
        );

        NomNomzBot.Domain.Stream.Entities.Stream stale = await db.Streams.SingleAsync(s =>
            s.Id == "stale-stream"
        );
        stale
            .EndedAt.Should()
            .Be(staleStart, "no chat proves a later end, and an open row would count as live");
        stale.Title.Should().BeNull("the new stream's title is never copied onto the old row");
    }

    private static NomNomzBot.Domain.Chat.Entities.ChatMessage ChatAt(
        string id,
        DateTimeOffset at
    ) =>
        new()
        {
            Id = id,
            BroadcasterId = Broadcaster,
            UserId = "u1",
            Username = "u1",
            DisplayName = "U1",
            UserType = "viewer",
            Message = "hi",
            CreatedAt = at.UtcDateTime,
        };

    [Fact]
    public async Task A_new_go_live_clears_the_previous_streams_anchor()
    {
        DateTimeOffset oldStart = new(2026, 8, 20, 10, 0, 0, TimeSpan.Zero);
        DateTimeOffset newStart = new(2026, 8, 27, 18, 0, 0, TimeSpan.Zero);
        ChannelContext ctx = new()
        {
            BroadcasterId = Broadcaster,
            TwitchChannelId = "tw-9",
            ChannelName = "streamer9",
            LastStreamStartedAt = oldStart,
        };
        (ChannelOnlineHandler sut, _, _) = Build(ctx);

        await sut.HandleAsync(
            new ChannelOnlineEvent
            {
                Provider = AuthEnums.Platform.Twitch,
                BroadcasterId = Broadcaster,
                BroadcasterDisplayName = "Streamer9",
                StreamTitle = "New session",
                GameName = "Just Chatting",
                StartedAt = newStart,
            }
        );

        ctx.WentLiveAt.Should().Be(newStart);
        ctx.LastStreamStartedAt.Should()
            .BeNull("an older stream's start must never survive a new go-live");
    }

    [Fact]
    public async Task The_inbox_is_signalled_once_after_IsLive_is_saved()
    {
        (ChannelOnlineHandler sut, _, LiveStateCapturingNotifier inbox) = Build();

        await sut.HandleAsync(
            new ChannelOnlineEvent
            {
                Provider = AuthEnums.Platform.Twitch,
                BroadcasterId = Broadcaster,
                BroadcasterDisplayName = "Streamer9",
                StreamTitle = "New session",
                GameName = "Just Chatting",
                StartedAt = new(2026, 8, 27, 18, 0, 0, TimeSpan.Zero),
            }
        );

        inbox.Calls.Should().Equal((Broadcaster, true));
    }

    [Fact]
    public async Task Going_live_clears_the_shoutouts_still_waiting_in_that_channels_queue_and_leaves_other_channels_alone()
    {
        ShoutoutQueue queue = new();
        Guid otherChannel = Guid.Parse("0192f000-0000-7000-8000-0000000000c2");
        queue.Enqueue(WaitingShoutout(Broadcaster, "501"));
        queue.Enqueue(WaitingShoutout(otherChannel, "502"));
        (ChannelOnlineHandler sut, _, _) = Build(shoutoutQueue: queue);

        await sut.HandleAsync(
            new ChannelOnlineEvent
            {
                Provider = AuthEnums.Platform.Twitch,
                BroadcasterId = Broadcaster,
                BroadcasterDisplayName = "Streamer9",
                StreamTitle = "New session",
                GameName = "Just Chatting",
                StartedAt = new(2026, 8, 27, 18, 0, 0, TimeSpan.Zero),
            }
        );

        queue.Peek(Broadcaster).Should().BeNull();
        queue.Peek(otherChannel)!.Target.Id.Should().Be("502");
    }

    // 2026-11-17: a moderator's channel that never onboarded went live, the event bus passed stream.online only
    // to recording handlers, and this one was not one of them, so the channel stayed "offline" all stream.
    [Fact]
    public async Task A_channel_that_did_not_onboard_is_saved_as_live_but_its_go_live_response_does_not_run()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            new()
            {
                Id = Broadcaster,
                OwnerUserId = Owner,
                Provider = AuthEnums.Platform.Twitch,
                ExternalChannelId = "tw-9",
                TwitchChannelId = "tw-9",
                Name = "streamer9",
                NameNormalized = "streamer9",
                IsOnboarded = false,
                DeploymentMode = AuthEnums.DeploymentMode.Saas,
                BillingTierKey = "free",
            }
        );
        db.SaveChanges();
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        ChannelContext ctx = new()
        {
            BroadcasterId = Broadcaster,
            TwitchChannelId = "tw-9",
            ChannelName = "streamer9",
        };
        registry
            .GetOrCreateAsync(Broadcaster, "tw-9", "streamer9", Arg.Any<CancellationToken>())
            .Returns(ctx);
        IEventResponseExecutor executor = Substitute.For<IEventResponseExecutor>();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IApplicationDbContext>(db);
        services.AddSingleton(executor);
        services.AddSingleton(Substitute.For<ITwitchStreamsApi>());
        services.AddSingleton<EventLogger>();
        services.AddSingleton<IChannelOnboardingGate, ChannelOnboardingGate>();
        services.AddSingleton<EventBus>();
        services.AddSingleton<IEventHandler<ChannelOnlineEvent>>(sp => new ChannelOnlineHandler(
            sp.GetRequiredService<IServiceScopeFactory>(),
            registry,
            new LiveStateCapturingNotifier(db),
            new ShoutoutQueue(),
            sp.GetRequiredService<IChannelOnboardingGate>(),
            NullLogger<ChannelOnlineHandler>.Instance
        ));
        ServiceProvider provider = services.BuildServiceProvider();

        await provider
            .GetRequiredService<EventBus>()
            .PublishAsync(
                new ChannelOnlineEvent
                {
                    Provider = AuthEnums.Platform.Twitch,
                    BroadcasterId = Broadcaster,
                    BroadcasterDisplayName = "Streamer9",
                    StreamTitle = "Building a book reader",
                    GameName = "Software and Game Development",
                    StartedAt = new(2026, 11, 17, 12, 15, 0, TimeSpan.Zero),
                    IsChannelIngress = true,
                }
            );

        (await db.Channels.AsNoTracking().SingleAsync(c => c.Id == Broadcaster))
            .IsLive.Should()
            .BeTrue("the moderators see the channel live while it streams");
        ctx.IsLive.Should().BeTrue();
        (await db.Streams.CountAsync(s => s.ChannelId == Broadcaster)).Should().Be(1);
        await executor
            .DidNotReceiveWithAnyArgs()
            .ExecuteAsync(default, default!, default, default, default!, default);
    }

    private static QueuedShoutout WaitingShoutout(Guid broadcasterId, string targetId) =>
        new(
            broadcasterId,
            new(
                Id: targetId,
                Login: "login" + targetId,
                DisplayName: "Name" + targetId,
                Type: "",
                BroadcasterType: "",
                Description: "",
                ProfileImageUrl: "",
                OfflineImageUrl: "",
                ViewCount: 0,
                CreatedAt: DateTimeOffset.UnixEpoch
            ),
            "viewer-1",
            IsRaid: false,
            TimeSpan.FromMinutes(2),
            TimeSpan.FromMinutes(60),
            DateTimeOffset.UnixEpoch
        );
}
