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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Analytics.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Analytics;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Analytics;

/// <summary>
/// Proves the chatter presence poll: a viewer who is in a live channel's chat but never spoke gets a
/// <see cref="ViewerProfile.LastSeenAt"/> stamp (the People "last seen"), the stamp only moves forward, an unknown
/// chatter gets a profile through the shared viewer identity path, and offline channels and channels without the
/// <c>moderator:read:chatters</c> scope are not read for chatters.
/// </summary>
public sealed class ChatterPresencePollServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);

    private sealed record Rig(
        ChatterPresencePollService Sut,
        AuthDbContext Db,
        ViewerResolver Resolver,
        ITwitchChatAssetsApi Api
    );

    private static Rig Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        IServiceScopeFactory scopeFactory = services
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();
        IUserService userService = AuthTestBuilder.UserService(
            db,
            Substitute.For<ICurrentUserService>(),
            scopeFactory
        );
        ChatterPresencePollService sut = new(
            scopeFactory,
            new FakeTimeProvider(new DateTimeOffset(Now)),
            NullLogger<ChatterPresencePollService>.Instance
        );
        return new(sut, db, new(db, userService), Substitute.For<ITwitchChatAssetsApi>());
    }

    private static Channel AddChannel(AuthDbContext db, bool live, string login)
    {
        Guid ownerId = Guid.NewGuid();
        db.Users.Add(
            new()
            {
                Id = ownerId,
                Username = login,
                UsernameNormalized = login,
                DisplayName = login,
            }
        );
        Channel channel = new()
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerId,
            Name = login,
            NameNormalized = login,
            Enabled = true,
            IsOnboarded = true,
            IsLive = live,
        };
        db.Channels.Add(channel);
        return channel;
    }

    private static void ReturnsChatters(Rig rig, Guid channelId, params TwitchChatter[] chatters) =>
        rig
            .Api.GetChattersAsync(
                channelId,
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(new TwitchPage<TwitchChatter>(chatters, null, chatters.Length))
            );

    [Fact]
    public async Task A_present_but_silent_chatter_gets_a_profile_stamped_with_the_poll_time()
    {
        Rig rig = Build();
        Channel channel = AddChannel(rig.Db, live: true, "streamer");
        await rig.Db.SaveChangesAsync();
        ReturnsChatters(rig, channel.Id, new TwitchChatter("111", "kana", "Kana"));

        int read = await rig.Sut.PollLiveChannelsAsync(
            rig.Db,
            rig.Api,
            rig.Resolver,
            CancellationToken.None
        );

        read.Should().Be(1);
        ViewerProfile profile = await rig.Db.ViewerProfiles.SingleAsync(p =>
            p.BroadcasterId == channel.Id
        );
        profile.ViewerTwitchUserId.Should().Be("111");
        profile.LastSeenAt.Should().Be(Now);
        profile.FirstSeenAt.Should().Be(Now);
        profile.TotalMessages.Should().Be(0, "presence is not a message");
        profile.UsernameSnapshot.Should().Be("kana");
        (await rig.Db.Users.AnyAsync(u => u.TwitchUserId == "111")).Should().BeTrue();
    }

    [Fact]
    public async Task The_stamp_moves_forward_only()
    {
        Rig rig = Build();
        Channel channel = AddChannel(rig.Db, live: true, "streamer");
        Guid olderViewer = Guid.NewGuid();
        Guid newerViewer = Guid.NewGuid();
        DateTime older = Now.AddDays(-7);
        DateTime newer = Now.AddMinutes(30);
        rig.Db.ViewerProfiles.AddRange(
            new ViewerProfile
            {
                BroadcasterId = channel.Id,
                ViewerUserId = olderViewer,
                ViewerTwitchUserId = "111",
                LastSeenAt = older,
            },
            new ViewerProfile
            {
                BroadcasterId = channel.Id,
                ViewerUserId = newerViewer,
                ViewerTwitchUserId = "222",
                LastSeenAt = newer,
            }
        );
        await rig.Db.SaveChangesAsync();
        ReturnsChatters(
            rig,
            channel.Id,
            new TwitchChatter("111", "old", "Old"),
            new TwitchChatter("222", "new", "New")
        );

        await rig.Sut.PollLiveChannelsAsync(rig.Db, rig.Api, rig.Resolver, CancellationToken.None);

        List<ViewerProfile> profiles = await rig.Db.ViewerProfiles.ToListAsync();
        profiles.Single(p => p.ViewerTwitchUserId == "111").LastSeenAt.Should().Be(Now);
        profiles.Single(p => p.ViewerTwitchUserId == "222").LastSeenAt.Should().Be(newer);
    }

    [Fact]
    public async Task An_offline_channel_is_not_read_and_nothing_is_stamped()
    {
        Rig rig = Build();
        AddChannel(rig.Db, live: false, "sleeping");
        await rig.Db.SaveChangesAsync();

        int read = await rig.Sut.PollLiveChannelsAsync(
            rig.Db,
            rig.Api,
            rig.Resolver,
            CancellationToken.None
        );

        read.Should().Be(0);
        (await rig.Db.ViewerProfiles.CountAsync()).Should().Be(0);
        await rig.Api.DidNotReceiveWithAnyArgs().GetChattersAsync(default, default!);
    }

    [Fact]
    public async Task A_channel_without_the_chatters_scope_is_skipped_and_stamps_nothing()
    {
        Rig rig = Build();
        Channel channel = AddChannel(rig.Db, live: true, "noscope");
        await rig.Db.SaveChangesAsync();
        rig.Api.GetChattersAsync(
                channel.Id,
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Failure<TwitchPage<TwitchChatter>>(
                    "Missing required scope.",
                    TwitchErrorCodes.MissingScope
                )
            );

        int first = await rig.Sut.PollLiveChannelsAsync(
            rig.Db,
            rig.Api,
            rig.Resolver,
            CancellationToken.None
        );
        int second = await rig.Sut.PollLiveChannelsAsync(
            rig.Db,
            rig.Api,
            rig.Resolver,
            CancellationToken.None
        );

        first.Should().Be(0);
        second.Should().Be(0);
        (await rig.Db.ViewerProfiles.CountAsync()).Should().Be(0);
    }
}
