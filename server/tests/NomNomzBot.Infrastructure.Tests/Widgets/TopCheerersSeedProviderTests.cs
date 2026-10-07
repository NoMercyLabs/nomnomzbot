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
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Widgets;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// Proves top_cheerers keeps its board after an overlay reload: one <c>cheer</c> frame per cheerer with the summed
/// bits, in the shape the widget already reads, counting only the chosen range (this stream, or since a reset) and
/// only this channel's own stored cheers.
/// </summary>
public sealed class TopCheerersSeedProviderTests : IDisposable
{
    private static readonly Guid Broadcaster = Guid.Parse("0192b000-0000-7000-8000-0000000000e1");
    private static readonly Guid OtherBroadcaster = Guid.Parse(
        "0192b000-0000-7000-8000-0000000000e2"
    );
    private static readonly DateTimeOffset WentLive = new(2026, 10, 7, 10, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);
    private static int _next;

    private readonly IChannelRegistry _registry = Substitute.For<IChannelRegistry>();
    private readonly WidgetSqliteTestDatabase _database = WidgetSqliteTestDatabase.Open();
    private readonly WidgetTestDbContext _ctx;

    public TopCheerersSeedProviderTests() => _ctx = _database.NewContext();

    public void Dispose()
    {
        _ctx.Dispose();
        _database.Dispose();
    }

    private static ChannelEvent Cheer(Guid channelId, string user, int bits, DateTimeOffset at)
    {
        JsonObject body = new()
        {
            ["user"] = user,
            ["bits"] = bits.ToString(),
            ["anonymous"] = "false",
            ["occurredAt"] = at.ToString("O"),
        };
        return new()
        {
            Id = $"cheer-{++_next}",
            ChannelId = channelId,
            Type = "channel.cheer",
            Data = body.ToJsonString(),
            // A legacy import rewrites CreatedAt: the moment in Data is the truth.
            CreatedAt = new DateTime(2026, 10, 7, 11, 0, 0, DateTimeKind.Utc),
        };
    }

    private TopCheerersSeedProvider Sut(DateTimeOffset? wentLiveAt)
    {
        _registry
            .Get(Broadcaster)
            .Returns(
                new ChannelContext
                {
                    BroadcasterId = Broadcaster,
                    TwitchChannelId = "1",
                    ChannelName = "c",
                    WentLiveAt = wentLiveAt,
                }
            );
        return new(_ctx, _registry);
    }

    private static Widget Board(string range, string? resetAt = null)
    {
        Dictionary<string, object> settings = new() { ["range"] = range };
        if (resetAt is not null)
            settings["resetAt"] = resetAt;
        return new() { BroadcasterId = Broadcaster, Settings = settings };
    }

    private static Dictionary<string, int> Totals(IReadOnlyList<WidgetSeedFrame> frames) =>
        frames.ToDictionary(
            f =>
                JsonSerializer
                    .SerializeToElement(f.Data, Wire)
                    .GetProperty("displayName")
                    .GetString()!,
            f => JsonSerializer.SerializeToElement(f.Data, Wire).GetProperty("bits").GetInt32()
        );

    private async Task AddRows(params ChannelEvent[] rows)
    {
        _ctx.ChannelEvents.AddRange(rows);
        await _ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task Stream_range_sums_only_cheers_after_go_live_per_user()
    {
        await AddRows(
            Cheer(Broadcaster, "Ann", 500, WentLive.AddHours(-3)),
            Cheer(Broadcaster, "Bob", 100, WentLive.AddHours(-1)),
            Cheer(Broadcaster, "Ann", 100, WentLive.AddMinutes(5)),
            Cheer(Broadcaster, "Ann", 50, WentLive.AddMinutes(10)),
            Cheer(Broadcaster, "Bob", 300, WentLive.AddMinutes(20)),
            Cheer(OtherBroadcaster, "Ann", 9999, WentLive.AddMinutes(30))
        );

        IReadOnlyList<WidgetSeedFrame> frames = await Sut(WentLive)
            .SeedAsync(Broadcaster, Board("stream"), CancellationToken.None);

        frames.Should().OnlyContain(f => f.EventType == "cheer");
        Totals(frames)
            .Should()
            .BeEquivalentTo(new Dictionary<string, int> { ["Ann"] = 150, ["Bob"] = 300 });
        JsonSerializer
            .SerializeToElement(frames[0].Data, Wire)
            .GetProperty("anonymous")
            .GetBoolean()
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task Stream_range_while_offline_gives_no_frames()
    {
        await AddRows(Cheer(Broadcaster, "Ann", 500, WentLive.AddMinutes(5)));

        IReadOnlyList<WidgetSeedFrame> frames = await Sut(null)
            .SeedAsync(Broadcaster, Board("stream"), CancellationToken.None);

        frames.Should().BeEmpty();
    }

    [Fact]
    public async Task SinceReset_range_counts_from_the_reset_moment()
    {
        await AddRows(
            Cheer(Broadcaster, "Ann", 500, WentLive.AddDays(-9)),
            Cheer(Broadcaster, "Ann", 70, WentLive.AddDays(-1)),
            Cheer(Broadcaster, "Bob", 30, WentLive.AddHours(-2)),
            Cheer(OtherBroadcaster, "Cy", 40, WentLive.AddHours(-2))
        );

        IReadOnlyList<WidgetSeedFrame> frames = await Sut(null)
            .SeedAsync(
                Broadcaster,
                Board("sinceReset", WentLive.AddDays(-2).ToString("O")),
                CancellationToken.None
            );

        Totals(frames)
            .Should()
            .BeEquivalentTo(new Dictionary<string, int> { ["Ann"] = 70, ["Bob"] = 30 });
    }

    [Fact]
    public async Task SinceReset_range_with_no_reset_date_counts_all_time()
    {
        await AddRows(
            Cheer(Broadcaster, "Ann", 500, WentLive.AddDays(-90)),
            Cheer(Broadcaster, "Ann", 70, WentLive.AddDays(-1))
        );

        IReadOnlyList<WidgetSeedFrame> frames = await Sut(null)
            .SeedAsync(Broadcaster, Board("sinceReset"), CancellationToken.None);

        Totals(frames).Should().BeEquivalentTo(new Dictionary<string, int> { ["Ann"] = 570 });
    }
}
