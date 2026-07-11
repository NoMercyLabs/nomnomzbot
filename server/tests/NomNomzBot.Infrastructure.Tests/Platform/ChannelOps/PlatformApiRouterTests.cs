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
using NomNomzBot.Application.Contracts.Platform;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Platform.ChannelOps;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.ChannelOps;

/// <summary>
/// Proves the channel-ops seam's routing (the <c>ChatPlatformRouter</c> twin): a stream-info update on a
/// YouTube tenant reaches the YouTube platform API, a Twitch tenant's the Twitch one, and an
/// unknown/unregistered provider falls back to Twitch (the pre-seam behavior) instead of throwing.
/// </summary>
public sealed class PlatformApiRouterTests
{
    private static readonly Guid TwitchTenant = Guid.Parse("0192b000-0000-7000-8000-0000000000d1");
    private static readonly Guid YouTubeTenant = Guid.Parse("0192b000-0000-7000-8000-0000000000d2");
    private static readonly Guid KickTenant = Guid.Parse("0192b000-0000-7000-8000-0000000000d3");
    private static readonly Guid Owner = Guid.Parse("0192b000-0000-7000-8000-0000000000d9");

    private static async Task<(
        PlatformApiRouter Router,
        IPlatformApi Twitch,
        IPlatformApi YouTube
    )> BuildAsync()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(Channel(TwitchTenant, AuthEnums.Platform.Twitch, "tw1"));
        db.Channels.Add(Channel(YouTubeTenant, AuthEnums.Platform.YouTube, "UCyt"));
        db.Channels.Add(Channel(KickTenant, AuthEnums.Platform.Kick, "kick1"));
        await db.SaveChangesAsync();

        IPlatformApi twitch = Substitute.For<IPlatformApi>();
        twitch.Provider.Returns(AuthEnums.Platform.Twitch);
        twitch
            .UpdateStreamInfoAsync(
                Arg.Any<Guid>(),
                Arg.Any<PlatformStreamInfoUpdate>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new PlatformStreamInfoApplied("t", null, null)));
        IPlatformApi youtube = Substitute.For<IPlatformApi>();
        youtube.Provider.Returns(AuthEnums.Platform.YouTube);
        youtube
            .UpdateStreamInfoAsync(
                Arg.Any<Guid>(),
                Arg.Any<PlatformStreamInfoUpdate>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new PlatformStreamInfoApplied("t", null, null)));

        PlatformApiRouter router = new(
            [twitch, youtube],
            db,
            NullLogger<PlatformApiRouter>.Instance
        );
        return (router, twitch, youtube);
    }

    [Fact]
    public async Task A_youtube_tenants_update_routes_to_the_youtube_platform()
    {
        (PlatformApiRouter router, IPlatformApi twitch, IPlatformApi youtube) = await BuildAsync();
        PlatformStreamInfoUpdate update = new(Title: "new title");

        Result<PlatformStreamInfoApplied> result = await router.UpdateStreamInfoAsync(
            YouTubeTenant,
            update
        );

        result.IsSuccess.Should().BeTrue();
        await youtube
            .Received(1)
            .UpdateStreamInfoAsync(YouTubeTenant, update, Arg.Any<CancellationToken>());
        await twitch
            .DidNotReceiveWithAnyArgs()
            .UpdateStreamInfoAsync(default, default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_twitch_tenants_update_routes_to_the_twitch_platform()
    {
        (PlatformApiRouter router, IPlatformApi twitch, IPlatformApi youtube) = await BuildAsync();

        await router.UpdateStreamInfoAsync(TwitchTenant, new PlatformStreamInfoUpdate("t"));

        await twitch
            .Received(1)
            .UpdateStreamInfoAsync(
                TwitchTenant,
                Arg.Any<PlatformStreamInfoUpdate>(),
                Arg.Any<CancellationToken>()
            );
        await youtube
            .DidNotReceiveWithAnyArgs()
            .UpdateStreamInfoAsync(default, default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unregistered_provider_falls_back_to_twitch_instead_of_throwing()
    {
        // Kick has a Channel row but no registered platform API yet — same fallback as the chat seam.
        (PlatformApiRouter router, IPlatformApi twitch, _) = await BuildAsync();

        await router.UpdateStreamInfoAsync(KickTenant, new PlatformStreamInfoUpdate("t"));

        await twitch
            .Received(1)
            .UpdateStreamInfoAsync(
                KickTenant,
                Arg.Any<PlatformStreamInfoUpdate>(),
                Arg.Any<CancellationToken>()
            );
    }

    private static Channel Channel(Guid id, string provider, string externalId) =>
        new()
        {
            Id = id,
            OwnerUserId = Owner,
            Provider = provider,
            ExternalChannelId = externalId,
            TwitchChannelId = provider == AuthEnums.Platform.Twitch ? externalId : null,
            Name = externalId,
            NameNormalized = externalId.ToLowerInvariant(),
            IsOnboarded = true,
            DeploymentMode = AuthEnums.DeploymentMode.Saas,
            BillingTierKey = "free",
        };
}
