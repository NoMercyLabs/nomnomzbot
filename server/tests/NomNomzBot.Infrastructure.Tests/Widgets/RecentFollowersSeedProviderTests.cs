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

using System.Text.Json;
using FluentAssertions;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Widgets;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// A reloaded recent_followers widget shows the channel's last followers at once: the newest <c>count</c> follow
/// alerts, oldest first (the widget puts each one on top), payloads as pushed, never another channel's follows.
/// </summary>
public sealed class RecentFollowersSeedProviderTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192b000-0000-7000-8000-0000000000f1");
    private static readonly Guid OtherBroadcaster = Guid.Parse(
        "0192b000-0000-7000-8000-0000000000f2"
    );
    private static readonly DateTime Start = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

    private static RenderedAlertCapture Capture(
        Guid broadcasterId,
        string eventType,
        string name,
        int minute
    ) =>
        new()
        {
            BroadcasterId = broadcasterId,
            EventType = eventType,
            Payload = $"{{\"displayName\":\"{name}\"}}",
            CreatedAt = Start.AddMinutes(minute),
        };

    [Fact]
    public async Task Seed_returns_the_newest_follows_up_to_count_oldest_first()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        await using (WidgetTestDbContext ctx = database.NewContext())
        {
            ctx.RenderedAlertCaptures.AddRange(
                Capture(Broadcaster, "follow", "Ann", 1),
                Capture(Broadcaster, "follow", "Bo", 2),
                Capture(Broadcaster, "cheer", "Cheerer", 3),
                Capture(OtherBroadcaster, "follow", "Stranger", 4),
                Capture(Broadcaster, "follow", "Cy", 5)
            );
            await ctx.SaveChangesAsync();
        }

        await using WidgetTestDbContext readCtx = database.NewContext();
        RecentFollowersSeedProvider sut = new(readCtx);

        IReadOnlyList<WidgetSeedFrame> frames = await sut.SeedAsync(
            Broadcaster,
            new Widget
            {
                BroadcasterId = Broadcaster,
                Settings = new Dictionary<string, object> { ["count"] = 2L },
            },
            CancellationToken.None
        );

        frames.Select(f => f.EventType).Should().Equal("follow", "follow");
        frames
            .Select(f => ((JsonElement)f.Data!).GetProperty("displayName").GetString())
            .Should()
            .Equal("Bo", "Cy");
    }
}
