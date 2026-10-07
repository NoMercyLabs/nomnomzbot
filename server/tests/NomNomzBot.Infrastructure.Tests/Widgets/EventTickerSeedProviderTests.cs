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
using FluentAssertions;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Widgets;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// Proves the event_ticker widget gets its last chips back on join: the newest captured alerts whose type the
/// widget shows, oldest first, with the payload exactly as it was pushed, and never another channel's alerts.
/// </summary>
public sealed class EventTickerSeedProviderTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192b000-0000-7000-8000-0000000000e1");
    private static readonly Guid OtherBroadcaster = Guid.Parse(
        "0192b000-0000-7000-8000-0000000000e2"
    );
    private static readonly DateTime Start = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    private static RenderedAlertCapture Capture(
        Guid broadcasterId,
        string eventType,
        string payload,
        int minute
    ) =>
        new()
        {
            BroadcasterId = broadcasterId,
            EventType = eventType,
            Payload = payload,
            CreatedAt = Start.AddMinutes(minute),
        };

    private static Widget Ticker(Dictionary<string, object> settings) =>
        new() { BroadcasterId = Broadcaster, Settings = settings };

    [Fact]
    public async Task Seed_returns_the_newest_count_of_shown_types_oldest_first_with_payloads_verbatim()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        await using (WidgetTestDbContext ctx = database.NewContext())
        {
            ctx.RenderedAlertCaptures.AddRange(
                Capture(Broadcaster, "follow", "{\"userDisplayName\":\"Ann\"}", 1),
                Capture(
                    Broadcaster,
                    "subscription",
                    "{\"userDisplayName\":\"Bo\",\"tier\":\"1000\"}",
                    2
                ),
                Capture(Broadcaster, "follow", "{\"userDisplayName\":\"Cy\"}", 3),
                Capture(Broadcaster, "ad_break", "{\"seconds\":60}", 4),
                Capture(
                    Broadcaster,
                    "subscription",
                    "{\"userDisplayName\":\"Di\",\"tier\":\"2000\"}",
                    5
                ),
                Capture(Broadcaster, "follow", "{\"userDisplayName\":\"Ed\"}", 6)
            );
            await ctx.SaveChangesAsync();
        }

        await using WidgetTestDbContext readCtx = database.NewContext();
        EventTickerSeedProvider sut = new(readCtx);

        IReadOnlyList<WidgetSeedFrame> frames = await sut.SeedAsync(
            Broadcaster,
            Ticker(
                new Dictionary<string, object>
                {
                    ["count"] = 4L,
                    ["events"] = new List<object?> { "follow", "subscription" },
                }
            ),
            CancellationToken.None
        );

        frames
            .Select(f => f.EventType)
            .Should()
            .Equal("subscription", "follow", "subscription", "follow");
        frames
            .Select(f => ((JsonElement)f.Data!).GetRawText())
            .Should()
            .Equal(
                "{\"userDisplayName\":\"Bo\",\"tier\":\"1000\"}",
                "{\"userDisplayName\":\"Cy\"}",
                "{\"userDisplayName\":\"Di\",\"tier\":\"2000\"}",
                "{\"userDisplayName\":\"Ed\"}"
            );
        frames
            .Select(f => f.OccurredAt.UtcDateTime)
            .Should()
            .Equal(
                Start.AddMinutes(2),
                Start.AddMinutes(3),
                Start.AddMinutes(5),
                Start.AddMinutes(6)
            );
        sut.NaturalKey.Should().Be("event_ticker");
    }

    [Fact]
    public async Task Seed_never_returns_another_channels_captures()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        await using (WidgetTestDbContext ctx = database.NewContext())
        {
            ctx.RenderedAlertCaptures.AddRange(
                Capture(Broadcaster, "follow", "{\"userDisplayName\":\"Mine\"}", 1),
                Capture(OtherBroadcaster, "follow", "{\"userDisplayName\":\"Theirs\"}", 2)
            );
            await ctx.SaveChangesAsync();
        }

        await using WidgetTestDbContext readCtx = database.NewContext();
        EventTickerSeedProvider sut = new(readCtx);

        IReadOnlyList<WidgetSeedFrame> frames = await sut.SeedAsync(
            Broadcaster,
            Ticker(new Dictionary<string, object>()),
            CancellationToken.None
        );

        frames.Should().ContainSingle();
        ((JsonElement)frames[0].Data!).GetRawText().Should().Be("{\"userDisplayName\":\"Mine\"}");
    }

    [Fact]
    public async Task Seed_gives_no_frame_when_nothing_was_captured()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        await using WidgetTestDbContext readCtx = database.NewContext();
        EventTickerSeedProvider sut = new(readCtx);

        IReadOnlyList<WidgetSeedFrame> frames = await sut.SeedAsync(
            Broadcaster,
            Ticker(new Dictionary<string, object>()),
            CancellationToken.None
        );

        frames.Should().BeEmpty();
    }
}
