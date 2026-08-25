// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Contracts.Kick;
using NomNomzBot.Application.Contracts.YouTube;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Platform.Scheduling;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Scheduling;

/// <summary>
/// S036 boot sweep — at process startup, every provider that owns an active token-consuming surface gets a
/// proactive refresh pass so an already-expiring connection doesn't wait for its first caller after
/// restart to pay the refresh latency. Proves the sweep is exhaustive over the provider list: Twitch's
/// existing periodic sweep is invoked, every Kick-connected channel is asked for a token, and every
/// connected YouTube vault connection (S036c-b — no longer the legacy Service row) is asked for a token
/// — asserted per provider, not just "it ran".
/// </summary>
public sealed class IntegrationTokenBootSweepServiceTests
{
    private static readonly Guid TwitchBroadcaster = Guid.Parse(
        "0199e000-0000-7000-8000-0000000000d1"
    );
    private static readonly Guid KickBroadcaster = Guid.Parse(
        "0199e000-0000-7000-8000-0000000000d2"
    );
    private static readonly Guid YouTubeBroadcaster = Guid.Parse(
        "0199e000-0000-7000-8000-0000000000d3"
    );

    [Fact]
    public async Task StartAsync_SweepsEveryProvider_TwitchKickAndYouTube()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            new()
            {
                Id = KickBroadcaster,
                Provider = AuthEnums.Platform.Kick,
                ExternalChannelId = "kick-ext-1",
                Name = "kick-streamer",
                NameNormalized = "kick-streamer",
                OwnerUserId = Guid.NewGuid(),
            }
        );
        db.IntegrationConnections.Add(
            new()
            {
                BroadcasterId = YouTubeBroadcaster,
                Provider = AuthEnums.IntegrationProvider.YouTube,
                ProviderAccountId = "yt-ext-1",
                Status = AuthEnums.IntegrationStatus.Connected,
            }
        );
        // A revoked YouTube connection must NOT be swept.
        db.IntegrationConnections.Add(
            new()
            {
                BroadcasterId = Guid.NewGuid(),
                Provider = AuthEnums.IntegrationProvider.YouTube,
                ProviderAccountId = "yt-ext-2",
                Status = AuthEnums.IntegrationStatus.Revoked,
            }
        );
        await db.SaveChangesAsync();

        ITwitchAuthService twitchAuth = Substitute.For<ITwitchAuthService>();
        IKickAccessTokenProvider kick = Substitute.For<IKickAccessTokenProvider>();
        IYouTubeAccessTokenProvider youTube = Substitute.For<IYouTubeAccessTokenProvider>();

        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        services.AddSingleton(twitchAuth);
        services.AddSingleton(kick);
        services.AddSingleton(youTube);
        ServiceProvider provider = services.BuildServiceProvider();

        IntegrationTokenBootSweepService sut = new(
            provider,
            NullLogger<IntegrationTokenBootSweepService>.Instance
        );

        await sut.StartAsync(CancellationToken.None);

        await twitchAuth.Received(1).RefreshExpiringTokensAsync(Arg.Any<CancellationToken>());
        await kick.Received(1).GetAsync(KickBroadcaster, Arg.Any<CancellationToken>());
        await youTube
            .Received(1)
            .GetAccessTokenAsync(YouTubeBroadcaster, Arg.Any<CancellationToken>());
        // The disabled row's broadcaster must never be touched.
        await youTube
            .DidNotReceive()
            .GetAccessTokenAsync(
                Arg.Is<Guid>(id => id != YouTubeBroadcaster),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task StartAsync_WhenOneProviderThrows_StillSweepsTheOthers()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            new()
            {
                Id = KickBroadcaster,
                Provider = AuthEnums.Platform.Kick,
                ExternalChannelId = "kick-ext-2",
                Name = "kick-streamer-2",
                NameNormalized = "kick-streamer-2",
                OwnerUserId = Guid.NewGuid(),
            }
        );
        await db.SaveChangesAsync();

        ITwitchAuthService twitchAuth = Substitute.For<ITwitchAuthService>();
        twitchAuth
            .RefreshExpiringTokensAsync(Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("twitch boom"));
        IKickAccessTokenProvider kick = Substitute.For<IKickAccessTokenProvider>();
        IYouTubeAccessTokenProvider youTube = Substitute.For<IYouTubeAccessTokenProvider>();

        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        services.AddSingleton(twitchAuth);
        services.AddSingleton(kick);
        services.AddSingleton(youTube);
        ServiceProvider provider = services.BuildServiceProvider();

        IntegrationTokenBootSweepService sut = new(
            provider,
            NullLogger<IntegrationTokenBootSweepService>.Instance
        );

        // Twitch throwing must not stop Kick's sweep from running.
        await sut.StartAsync(CancellationToken.None);

        await kick.Received(1).GetAsync(KickBroadcaster, Arg.Any<CancellationToken>());
    }
}
