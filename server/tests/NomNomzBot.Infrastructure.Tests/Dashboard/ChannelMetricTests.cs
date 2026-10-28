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
using NomNomzBot.Application.Dashboard.Dtos;
using NomNomzBot.Application.Dashboard.Stats;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Dashboard.Stats;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Dashboard;

/// <summary>The recap names the streaming channel and carries its profile picture for the watermark.</summary>
public sealed class ChannelMetricTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192f000-0000-7000-8000-0000000000c1");
    private static readonly Guid OtherChannelId = Guid.Parse(
        "0192f000-0000-7000-8000-0000000000c2"
    );
    private static readonly DateTimeOffset Started = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    private static StreamWindow Window(Guid channelId) =>
        new(channelId, "s-1", Started, null, Started.AddHours(2), null, null, null);

    private static async Task<AuthDbContext> SeededAsync()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF;");
        Guid ownerId = Guid.NewGuid();
        Guid otherOwnerId = Guid.NewGuid();
        db.Users.AddRange(
            new User
            {
                Id = ownerId,
                TwitchUserId = "tw-owner",
                Username = "stoney_eagle",
                UsernameNormalized = "stoney_eagle",
                DisplayName = "Stoney_Eagle",
                ProfileImageUrl = "https://cdn.example/stoney.png",
            },
            new User
            {
                Id = otherOwnerId,
                TwitchUserId = "tw-other",
                Username = "someone",
                UsernameNormalized = "someone",
                DisplayName = "Someone",
                ProfileImageUrl = "https://cdn.example/someone.png",
            }
        );
        db.Channels.AddRange(
            NewChannel(ChannelId, ownerId),
            NewChannel(OtherChannelId, otherOwnerId)
        );
        await db.SaveChangesAsync();
        return db;
    }

    private static Channel NewChannel(Guid id, Guid ownerId) =>
        new()
        {
            Id = id,
            OwnerUserId = ownerId,
            Provider = AuthEnums.Platform.Twitch,
            ExternalChannelId = $"ext-{id}",
            Name = $"channel-{id}",
            NameNormalized = $"channel-{id}",
        };

    [Fact]
    public async Task Channel_metric_carries_the_streamers_name_and_avatar_and_not_another_channels()
    {
        AuthDbContext db = await SeededAsync();
        StreamStatsDto stats = new() { Stream = new() { Id = "s-1" } };

        await new ChannelMetric(db).ComputeIntoAsync(Window(ChannelId), stats);

        stats.Channel.Should().NotBeNull();
        stats.Channel!.Name.Should().Be("Stoney_Eagle");
        stats.Channel.AvatarUrl.Should().Be("https://cdn.example/stoney.png");
    }

    [Fact]
    public async Task Channel_metric_leaves_the_channel_out_when_the_channel_is_unknown()
    {
        AuthDbContext db = await SeededAsync();
        StreamStatsDto stats = new() { Stream = new() { Id = "s-1" } };

        await new ChannelMetric(db).ComputeIntoAsync(Window(Guid.NewGuid()), stats);

        stats.Channel.Should().BeNull();
    }
}
