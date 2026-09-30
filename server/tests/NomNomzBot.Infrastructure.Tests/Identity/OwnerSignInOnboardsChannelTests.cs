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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.BackgroundServices;
using NomNomzBot.Infrastructure.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// A moderator opening a channel creates an un-onboarded tenant; only the channel's OWNER signing in installs
/// the bot there. Owner sign-in also keeps the channel's base <see cref="PlatformConnection"/> (D1) in place.
/// </summary>
public sealed class OwnerSignInOnboardsChannelTests
{
    // The Twitch identity AuthServiceReAuthOnboardingRepublishTests.Build signs in as.
    private const string OwnerTwitchId = "tw-100";

    private static readonly OAuthCallbackDto Callback = new() { Code = "auth-code" };
    private static readonly AuthContextDto Context = new("web", "127.0.0.1", "test-agent");

    [Fact]
    public async Task Owner_sign_in_onboards_the_tenant_a_moderator_opened_and_the_bot_starts_serving_it()
    {
        string database = Guid.NewGuid().ToString();
        AuthDbContext db = AuthTestBuilder.NewContext(database);
        Guid ownerId = await SeedUserAsync(db, OwnerTwitchId, "stoney");

        Result<Guid> tenant = await BuildChannelService(db)
            .EnsureModeratedTenantAsync(OwnerTwitchId, "stoney", "Stoney", ownerId);
        tenant.IsSuccess.Should().BeTrue();

        Channel opened = await db.Channels.AsNoTracking().SingleAsync(c => c.Id == tenant.Value);
        opened.IsOnboarded.Should().BeFalse();
        opened.BotJoinedAt.Should().BeNull();
        (await db.PlatformConnections.CountAsync()).Should().Be(0);
        (await SubscribedChannelsAsync(database)).Should().NotContain(tenant.Value);

        DateTime before = DateTime.UtcNow;
        Result<AuthResultDto> signIn = await AuthServiceReAuthOnboardingRepublishTests
            .Build(db, new RecordingEventBus())
            .HandleTwitchCallbackAsync(Callback, Context);
        signIn.IsSuccess.Should().BeTrue();

        Channel promoted = await db.Channels.AsNoTracking().SingleAsync();
        promoted
            .Id.Should()
            .Be(tenant.Value, "the owner's sign-in reuses the tenant, never a second one");
        promoted.IsOnboarded.Should().BeTrue();
        promoted.BotJoinedAt.Should().NotBeNull().And.BeOnOrAfter(before);

        PlatformConnection connection = await db.PlatformConnections.AsNoTracking().SingleAsync();
        connection.ChannelId.Should().Be(tenant.Value);
        connection.Provider.Should().Be(AuthEnums.Platform.Twitch);
        connection.ExternalChannelId.Should().Be(OwnerTwitchId);
        connection.DisplayName.Should().Be("Stoney");
        connection.IsPrimary.Should().BeTrue();

        (await SubscribedChannelsAsync(database)).Should().Contain(tenant.Value);
    }

    [Fact]
    public async Task Owner_sign_in_adds_the_missing_platform_connection_exactly_once()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        Guid ownerId = await SeedUserAsync(db, OwnerTwitchId, "stoney");
        DateTime joinedAt = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        Channel channel = new()
        {
            OwnerUserId = ownerId,
            TwitchChannelId = OwnerTwitchId,
            ExternalChannelId = OwnerTwitchId,
            Name = "stoney",
            NameNormalized = "stoney",
            IsOnboarded = true,
            BotJoinedAt = joinedAt,
        };
        db.Channels.Add(channel);
        await db.SaveChangesAsync();

        AuthService auth = AuthServiceReAuthOnboardingRepublishTests.Build(
            db,
            new RecordingEventBus()
        );
        (await auth.HandleTwitchCallbackAsync(Callback, Context)).IsSuccess.Should().BeTrue();
        (await auth.HandleTwitchCallbackAsync(Callback, Context)).IsSuccess.Should().BeTrue();

        PlatformConnection connection = await db.PlatformConnections.AsNoTracking().SingleAsync();
        connection.ChannelId.Should().Be(channel.Id);
        connection.ExternalChannelId.Should().Be(OwnerTwitchId);
        connection.IsPrimary.Should().BeTrue();

        Channel after = await db.Channels.AsNoTracking().SingleAsync();
        after
            .BotJoinedAt.Should()
            .Be(joinedAt, "an already-onboarded channel keeps its real join time");
    }

    [Fact]
    public async Task Owner_sign_in_refreshes_a_stale_platform_connection_display_name()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        Guid ownerId = await SeedUserAsync(db, OwnerTwitchId, "stoney");
        Channel channel = new()
        {
            OwnerUserId = ownerId,
            TwitchChannelId = OwnerTwitchId,
            ExternalChannelId = OwnerTwitchId,
            Name = "stoney",
            NameNormalized = "stoney",
            IsOnboarded = true,
        };
        db.Channels.Add(channel);
        db.PlatformConnections.Add(
            new()
            {
                ChannelId = channel.Id,
                Provider = AuthEnums.Platform.Twitch,
                ExternalChannelId = OwnerTwitchId,
                DisplayName = "old-name",
                IsPrimary = true,
            }
        );
        await db.SaveChangesAsync();

        (
            await AuthServiceReAuthOnboardingRepublishTests
                .Build(db, new RecordingEventBus())
                .HandleTwitchCallbackAsync(Callback, Context)
        )
            .IsSuccess.Should()
            .BeTrue();

        PlatformConnection connection = await db.PlatformConnections.AsNoTracking().SingleAsync();
        connection.DisplayName.Should().Be("Stoney");
    }

    [Fact]
    public async Task A_moderator_entering_or_signing_in_never_onboards_the_channel_they_moderate()
    {
        string database = Guid.NewGuid().ToString();
        AuthDbContext db = AuthTestBuilder.NewContext(database);
        const string broadcasterTwitchId = "tw-200";
        Guid broadcasterId = await SeedUserAsync(db, broadcasterTwitchId, "broadcaster");
        ChannelService channels = BuildChannelService(db);

        // The moderator (tw-100) opens the channel twice, then signs in with their own account.
        Result<Guid> first = await channels.EnsureModeratedTenantAsync(
            broadcasterTwitchId,
            "broadcaster",
            "Broadcaster",
            broadcasterId
        );
        Result<Guid> second = await channels.EnsureModeratedTenantAsync(
            broadcasterTwitchId,
            "broadcaster",
            "Broadcaster",
            broadcasterId
        );
        second.Value.Should().Be(first.Value);
        (
            await AuthServiceReAuthOnboardingRepublishTests
                .Build(db, new RecordingEventBus())
                .HandleTwitchCallbackAsync(Callback, Context)
        )
            .IsSuccess.Should()
            .BeTrue();

        Channel moderated = await db.Channels.AsNoTracking().SingleAsync(c => c.Id == first.Value);
        moderated.IsOnboarded.Should().BeFalse();
        moderated.BotJoinedAt.Should().BeNull();
        (await db.PlatformConnections.AnyAsync(p => p.ChannelId == first.Value)).Should().BeFalse();
        (await SubscribedChannelsAsync(database)).Should().NotContain(first.Value);
    }

    // ─── scaffolding ───────────────────────────────────────────────────────────

    private static async Task<Guid> SeedUserAsync(AuthDbContext db, string twitchId, string login)
    {
        User user = new()
        {
            TwitchUserId = twitchId,
            Username = login,
            UsernameNormalized = login,
            DisplayName = login,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static ChannelService BuildChannelService(AuthDbContext db) =>
        new(
            db,
            TimeProvider.System,
            new RecordingEventBus(),
            Substitute.For<IChannelRegistry>(),
            Substitute.For<ITwitchEventSubService>(),
            Substitute.For<IChatProvider>(),
            Substitute.For<IBuiltinResponseComposer>(),
            Substitute.For<IBotModeratorStatusService>()
        );

    /// <summary>Runs one real BotLifecycleService sweep over the database and returns the channels it subscribed.</summary>
    private static async Task<IReadOnlyCollection<Guid>> SubscribedChannelsAsync(string database)
    {
        ConcurrentBag<Guid> subscribed = [];
        ITwitchEventSubService eventSub = Substitute.For<ITwitchEventSubService>();
        eventSub
            .EnsureSubscribedAsync(
                Arg.Any<Guid>(),
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                Guid channelId = call.ArgAt<Guid>(0);
                if (channelId != Guid.Empty)
                    subscribed.Add(channelId);
                return Task.FromResult(Result.Success());
            });

        ITwitchStreamsApi streams = Substitute.For<ITwitchStreamsApi>();
        streams
            .GetStreamAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<TwitchStream>("offline", TwitchErrorCodes.NotFound));

        ServiceCollection services = new();
        services.AddScoped<IApplicationDbContext>(_ => AuthTestBuilder.NewContext(database));
        services.AddSingleton(eventSub);
        services.AddSingleton(streams);
        services.AddSingleton(Substitute.For<IBotModeratorStatusService>());

        await new BotLifecycleService(
            services.BuildServiceProvider(),
            NullLogger<BotLifecycleService>.Instance
        ).SyncChannelsAsync(CancellationToken.None, reconcileStale: false);

        return subscribed;
    }
}
