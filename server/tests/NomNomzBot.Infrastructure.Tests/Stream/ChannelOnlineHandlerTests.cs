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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Stream.Events;
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

    private static (ChannelOnlineHandler Sut, AuthDbContext Db) Build()
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
                new ChannelContext
                {
                    BroadcasterId = Broadcaster,
                    TwitchChannelId = "tw-9",
                    ChannelName = "streamer9",
                }
            );

        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IApplicationDbContext>(db)
            .AddSingleton(Substitute.For<IEventResponseExecutor>())
            .AddSingleton(
                Substitute.For<NomNomzBot.Application.Contracts.Twitch.ITwitchStreamsApi>()
            )
            .BuildServiceProvider();

        ChannelOnlineHandler sut = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            registry,
            NullLogger<ChannelOnlineHandler>.Instance
        );
        return (sut, db);
    }

    [Fact]
    public async Task A_stale_open_stream_from_a_missed_offline_is_closed_when_the_channel_goes_live_again()
    {
        (ChannelOnlineHandler sut, AuthDbContext db) = Build();
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
        await db.SaveChangesAsync();

        DateTimeOffset newStart = new(2026, 8, 27, 18, 0, 0, TimeSpan.Zero);
        await sut.HandleAsync(
            new ChannelOnlineEvent
            {
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
        stale.EndedAt.Should().NotBeNull("the stale row must not stay open forever");

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
}
