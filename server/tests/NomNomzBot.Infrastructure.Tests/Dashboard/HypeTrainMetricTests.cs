// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using NomNomzBot.Application.Dashboard.Dtos;
using NomNomzBot.Application.Dashboard.Stats;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Dashboard.Stats;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Dashboard;

/// <summary>The recap reports the highest hype train level of the stream and how many trains ended, and nothing when none did.</summary>
public sealed class HypeTrainMetricTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192f000-0000-7000-8000-0000000000d1");
    private static readonly Guid OtherChannelId = Guid.Parse(
        "0192f000-0000-7000-8000-0000000000d2"
    );
    private static readonly DateTimeOffset Started = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    private static StreamWindow Window() =>
        new(ChannelId, "s-1", Started, null, Started.AddHours(2), null, null, null);

    private static ChannelEvent Train(Guid channel, DateTimeOffset at, int level)
    {
        JsonObject json = JsonSerializer
            .SerializeToNode(new { level, total = level * 100 })!
            .AsObject();
        json["occurredAt"] = at.UtcDateTime.ToString("O");
        return new()
        {
            Id = Guid.NewGuid().ToString(),
            ChannelId = channel,
            Type = "channel.hype_train.end",
            CreatedAt = at.UtcDateTime,
            Data = json.ToJsonString(),
        };
    }

    private static async Task<StreamStatsDto> ComputeAsync(params ChannelEvent[] events)
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.ChannelEvents.AddRange(events);
        await db.SaveChangesAsync();
        StreamStatsDto stats = new() { Stream = new() { Id = "s-1" } };

        HypeTrainMetric metric = new(new StreamFactReader(db, new StreamBanFilter(db)));
        await metric.ComputeIntoAsync(Window(), stats);
        return stats;
    }

    [Fact]
    public async Task Reports_the_highest_level_and_the_train_count_inside_the_stream()
    {
        StreamStatsDto stats = await ComputeAsync(
            Train(ChannelId, Started.AddMinutes(20), 3),
            Train(ChannelId, Started.AddMinutes(70), 5),
            Train(ChannelId, Started.AddMinutes(100), 2),
            Train(ChannelId, Started.AddHours(-3), 9),
            Train(OtherChannelId, Started.AddMinutes(30), 8)
        );

        stats.HypeTrain.Should().NotBeNull();
        stats.HypeTrain!.Level.Should().Be(5);
        stats.HypeTrain.Trains.Should().Be(3);
    }

    [Fact]
    public async Task Reports_nothing_when_no_hype_train_ended()
    {
        StreamStatsDto stats = await ComputeAsync(Train(OtherChannelId, Started.AddMinutes(30), 4));

        stats.HypeTrain.Should().BeNull();
    }
}
