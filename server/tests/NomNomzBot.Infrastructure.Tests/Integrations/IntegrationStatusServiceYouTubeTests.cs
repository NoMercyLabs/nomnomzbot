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
using NomNomzBot.Application.Integrations.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Integrations;

/// <summary>
/// S036c-b — <see cref="IntegrationStatusService"/> resolves YouTube's connectivity from the vaulted
/// <c>IntegrationConnection</c>, not the legacy <c>Service</c> row: a stale/leftover Service row must
/// never report a channel as YouTube-connected once the vault says otherwise, and a genuinely connected
/// vault row must report connected even with NO Service row at all (the shape every post-migration
/// connect leaves behind, since the mirror no longer writes YouTube).
/// </summary>
public sealed class IntegrationStatusServiceYouTubeTests
{
    private static readonly Guid Channel = Guid.Parse("0199f000-0000-7000-8000-0000000000e1");

    [Fact]
    public async Task GetStatusesAsync_ReportsYouTubeConnected_FromTheVaultAlone_NoServiceRow()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            new()
            {
                Id = Channel,
                Provider = AuthEnums.Platform.Twitch,
                ExternalChannelId = "tw1",
                TwitchChannelId = "tw1",
                Name = "streamer",
                NameNormalized = "streamer",
                OwnerUserId = Guid.NewGuid(),
            }
        );
        db.IntegrationConnections.Add(
            new()
            {
                BroadcasterId = Channel,
                Provider = AuthEnums.IntegrationProvider.YouTube,
                ProviderAccountId = "yt-1",
                Status = AuthEnums.IntegrationStatus.Connected,
            }
        );
        await db.SaveChangesAsync();

        IntegrationStatusService sut = new(db, Substitute.For<IMusicService>());

        Result<List<ChannelIntegrationDto>> result = await sut.GetStatusesAsync(Channel);

        result.IsSuccess.Should().BeTrue();
        ChannelIntegrationDto youtube = result.Value.Single(i => i.Id == "youtube");
        youtube
            .Connected.Should()
            .BeTrue("the vault connection alone must be enough — no Service row exists");
    }

    [Fact]
    public async Task GetStatusesAsync_ReportsYouTubeDisconnected_WhenAStaleServiceRowExistsButTheVaultDoesNot()
    {
        // A Service row surviving from before the S036c-a backfill (or never swept) must NOT make the
        // channel read as connected once custody has moved to the vault.
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            new()
            {
                Id = Channel,
                Provider = AuthEnums.Platform.Twitch,
                ExternalChannelId = "tw2",
                TwitchChannelId = "tw2",
                Name = "streamer2",
                NameNormalized = "streamer2",
                OwnerUserId = Guid.NewGuid(),
            }
        );
        db.Services.Add(
            new()
            {
                Id = Guid.NewGuid().ToString(),
                Name = "youtube",
                BroadcasterId = Channel,
                Enabled = true,
                AccessToken = "stale-envelope",
            }
        );
        await db.SaveChangesAsync();

        IntegrationStatusService sut = new(db, Substitute.For<IMusicService>());

        Result<List<ChannelIntegrationDto>> result = await sut.GetStatusesAsync(Channel);

        result.IsSuccess.Should().BeTrue();
        ChannelIntegrationDto youtube = result.Value.Single(i => i.Id == "youtube");
        youtube
            .Connected.Should()
            .BeFalse(
                "a legacy Service row with no live vault connection must read as disconnected"
            );
    }

    [Fact]
    public async Task GetStatusesAsync_ReportsYouTubeDisconnected_WhenTheVaultConnectionIsRevoked()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            new()
            {
                Id = Channel,
                Provider = AuthEnums.Platform.Twitch,
                ExternalChannelId = "tw3",
                TwitchChannelId = "tw3",
                Name = "streamer3",
                NameNormalized = "streamer3",
                OwnerUserId = Guid.NewGuid(),
            }
        );
        db.IntegrationConnections.Add(
            new()
            {
                BroadcasterId = Channel,
                Provider = AuthEnums.IntegrationProvider.YouTube,
                ProviderAccountId = "yt-3",
                Status = AuthEnums.IntegrationStatus.Revoked,
            }
        );
        await db.SaveChangesAsync();

        IntegrationStatusService sut = new(db, Substitute.For<IMusicService>());

        Result<List<ChannelIntegrationDto>> result = await sut.GetStatusesAsync(Channel);

        ChannelIntegrationDto youtube = result.Value.Single(i => i.Id == "youtube");
        youtube.Connected.Should().BeFalse();
    }
}
