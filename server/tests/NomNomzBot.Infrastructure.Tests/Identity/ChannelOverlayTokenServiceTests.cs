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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves the overlay-token read + rotate contract: every widget/overlay browser-source URL for a channel
/// shares one opaque <c>Channels.OverlayToken</c>, minted once at channel creation but — until this — never
/// rotatable, so a leaked widget URL could never be invalidated.
/// </summary>
public sealed class ChannelOverlayTokenServiceTests
{
    private static readonly Guid ChannelId = Guid.Parse("0198d000-0000-7000-8000-0000000000f3");
    private static readonly Guid OwnerId = Guid.Parse("0198d000-0000-7000-8000-0000000000f4");

    private static (AuthDbContext Db, Channel Channel) SeededDb()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Users.Add(
            new()
            {
                Id = OwnerId,
                Username = "stoney",
                UsernameNormalized = "stoney",
                DisplayName = "Stoney",
            }
        );
        Channel channel = new()
        {
            Id = ChannelId,
            OwnerUserId = OwnerId,
            TwitchChannelId = "tw-owner",
            ExternalChannelId = "tw-owner",
            Name = "stoney",
            NameNormalized = "stoney",
            OverlayToken = "original-token",
        };
        db.Channels.Add(channel);
        db.SaveChanges();
        return (db, channel);
    }

    private static ChannelService Build(AuthDbContext db) =>
        new(
            db,
            TimeProvider.System,
            new RecordingEventBus(),
            Substitute.For<IChannelRegistry>(),
            Substitute.For<ITwitchEventSubService>()
        );

    [Fact]
    public async Task GetOverlayToken_returns_the_channels_current_token()
    {
        (AuthDbContext db, _) = SeededDb();
        ChannelService sut = Build(db);

        Result<string> result = await sut.GetOverlayTokenAsync(ChannelId.ToString());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("original-token");
    }

    [Fact]
    public async Task RotateOverlayToken_mints_a_new_token_and_persists_it()
    {
        (AuthDbContext db, _) = SeededDb();
        ChannelService sut = Build(db);

        Result<string> result = await sut.RotateOverlayTokenAsync(ChannelId.ToString());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBe("original-token");
        Channel? channel = await db.Channels.FindAsync(ChannelId);
        channel!.OverlayToken.Should().Be(result.Value);
    }

    [Fact]
    public async Task RotateOverlayToken_invalidates_the_old_token_for_widget_auth()
    {
        (AuthDbContext db, _) = SeededDb();
        ChannelService sut = Build(db);

        await sut.RotateOverlayTokenAsync(ChannelId.ToString());

        ChannelOverlayInfo? viaOldToken = await sut.GetByOverlayTokenAsync("original-token");
        viaOldToken.Should().BeNull("the old widget URLs must stop authenticating once rotated");
    }

    [Fact]
    public async Task RotateOverlayToken_for_an_unknown_channel_is_not_found()
    {
        (AuthDbContext db, _) = SeededDb();
        ChannelService sut = Build(db);

        Result<string> result = await sut.RotateOverlayTokenAsync(Guid.NewGuid().ToString());

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("CHANNEL_NOT_FOUND");
    }
}
