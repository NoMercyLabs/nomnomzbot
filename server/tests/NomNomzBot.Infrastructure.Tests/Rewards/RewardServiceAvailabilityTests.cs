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
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Rewards.Dtos;
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Infrastructure.Rewards;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Rewards;

/// <summary>
/// Channel points need a Twitch Affiliate or Partner. Proves the service reports that from the owner's stored
/// Twitch broadcaster type, and that a reward row with no Twitch reward id reads as not on Twitch.
/// </summary>
public sealed class RewardServiceAvailabilityTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000d301");
    private static readonly Guid Owner = Guid.Parse("0192a000-0000-7000-8000-00000000d300");

    private static (RewardService Sut, AuthDbContext Db) Build(string broadcasterType)
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Users.Add(
            new()
            {
                Id = Owner,
                Username = "fillz",
                UsernameNormalized = "fillz",
                DisplayName = "Fillz",
                BroadcasterType = broadcasterType,
            }
        );
        db.Channels.Add(
            new()
            {
                Id = Channel,
                OwnerUserId = Owner,
                TwitchChannelId = "tw-channel",
                Name = "fillz",
                NameNormalized = "fillz",
            }
        );
        db.SaveChanges();

        RewardService sut = new(
            db,
            Substitute.For<ITwitchChannelPointsApi>(),
            TimeProvider.System,
            NullLogger<RewardService>.Instance,
            RewardServiceTestProvider.Create()
        );
        return (sut, db);
    }

    [Theory]
    [InlineData("affiliate", true)]
    [InlineData("partner", true)]
    [InlineData("", false)]
    public async Task GetAvailabilityAsync_ReadsTheOwnersBroadcasterType(
        string broadcasterType,
        bool expected
    )
    {
        (RewardService sut, AuthDbContext _) = Build(broadcasterType);

        Result<RewardAvailability> result = await sut.GetAvailabilityAsync(Channel.ToString());

        result.IsSuccess.Should().BeTrue();
        result.Value.ChannelPointsAvailable.Should().Be(expected);
        result.Value.BroadcasterType.Should().Be(broadcasterType);
    }

    [Fact]
    public async Task GetAsync_RewardWithoutTwitchId_IsNotOnTwitch()
    {
        (RewardService sut, AuthDbContext db) = Build("");
        Reward local = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = Channel,
            Title = "Hydrate",
            Cost = 100,
            IsPaused = true,
        };
        Reward live = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = Channel,
            Title = "Live one",
            Cost = 100,
            TwitchRewardId = "tw-1",
        };
        db.Rewards.AddRange(local, live);
        db.SaveChanges();

        Result<RewardDetail> localDetail = await sut.GetAsync(
            Channel.ToString(),
            local.Id.ToString()
        );
        Result<RewardDetail> liveDetail = await sut.GetAsync(
            Channel.ToString(),
            live.Id.ToString()
        );

        localDetail.Value.IsOnTwitch.Should().BeFalse();
        liveDetail.Value.IsOnTwitch.Should().BeTrue();
    }
}
