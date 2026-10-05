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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Stream.Events;
using NomNomzBot.Infrastructure.Stream;
using NomNomzBot.Infrastructure.Stream.EventHandlers;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Stream;

/// <summary>
/// The action-required inbox reads Channel.IsLive. The offline handler must signal it only after the flip is
/// saved, or the refresh reads the old value and the live item stays until the next presence change.
/// </summary>
public sealed class ChannelOfflineHandlerTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192f000-0000-7000-8000-0000000000d1");
    private static readonly Guid Owner = Guid.Parse("0192f000-0000-7000-8000-0000000000d9");

    [Fact]
    public async Task The_inbox_is_signalled_once_after_IsLive_is_saved_as_false()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            new()
            {
                Id = Broadcaster,
                OwnerUserId = Owner,
                Provider = AuthEnums.Platform.Twitch,
                ExternalChannelId = "tw-8",
                TwitchChannelId = "tw-8",
                Name = "streamer8",
                NameNormalized = "streamer8",
                IsOnboarded = true,
                IsLive = true,
                DeploymentMode = AuthEnums.DeploymentMode.Saas,
                BillingTierKey = "free",
            }
        );
        db.SaveChanges();

        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IApplicationDbContext>(db)
            .AddSingleton(Substitute.For<IEventResponseExecutor>())
            .BuildServiceProvider();
        LiveStateCapturingNotifier inbox = new(db);
        ChannelOfflineHandler sut = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IPipelineEngine>(),
            Substitute.For<IChannelRegistry>(),
            inbox,
            new ShoutoutQueue(),
            new FakeTimeProvider(new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<ChannelOfflineHandler>.Instance
        );

        await sut.HandleAsync(
            new ChannelOfflineEvent
            {
                Provider = AuthEnums.Platform.Twitch,
                BroadcasterId = Broadcaster,
                BroadcasterDisplayName = "Streamer8",
                StreamDuration = TimeSpan.FromHours(1),
            }
        );

        inbox.Calls.Should().Equal((Broadcaster, false));
    }

    [Fact]
    public async Task Going_offline_clears_the_shoutouts_still_waiting_in_that_channels_queue_and_leaves_other_channels_alone()
    {
        ShoutoutQueue queue = new();
        Guid otherChannel = Guid.Parse("0192f000-0000-7000-8000-0000000000d2");
        queue.Enqueue(WaitingShoutout(Broadcaster, "501"));
        queue.Enqueue(WaitingShoutout(otherChannel, "502"));
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IApplicationDbContext>(AuthTestBuilder.NewContext())
            .AddSingleton(Substitute.For<IEventResponseExecutor>())
            .BuildServiceProvider();
        ChannelOfflineHandler sut = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IPipelineEngine>(),
            Substitute.For<IChannelRegistry>(),
            new LiveStateCapturingNotifier(AuthTestBuilder.NewContext()),
            queue,
            new FakeTimeProvider(new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<ChannelOfflineHandler>.Instance
        );

        await sut.HandleAsync(
            new ChannelOfflineEvent
            {
                Provider = AuthEnums.Platform.Twitch,
                BroadcasterId = Broadcaster,
                BroadcasterDisplayName = "Streamer8",
                StreamDuration = TimeSpan.FromHours(1),
            }
        );

        queue.Peek(Broadcaster).Should().BeNull();
        queue.Peek(otherChannel)!.Target.Id.Should().Be("502");
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
            "line",
            false,
            "viewer-1",
            IsRaid: false,
            TimeSpan.FromMinutes(2),
            TimeSpan.FromMinutes(60),
            DateTimeOffset.UnixEpoch
        );
}
