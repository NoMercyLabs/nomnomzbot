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
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Domain.Widgets.Entities;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves the ad countdown reaches overlay widgets: an ad schedule poll becomes an <c>ad_schedule</c> widget event
/// and a crossed warn threshold becomes an <c>ad_upcoming</c> widget event, each with the old bot's fields, and
/// each only for the widgets that subscribe to that name.
/// </summary>
public sealed class AdScheduleBroadcastHandlersTests
{
    private static readonly DateTimeOffset NextAd = new(2026, 10, 5, 12, 50, 0, TimeSpan.Zero);

    private static JsonElement Wire(object? data) =>
        JsonSerializer.SerializeToElement(
            data,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
        );

    [Fact]
    public async Task A_schedule_poll_reaches_only_the_widget_subscribed_to_ad_schedule()
    {
        IWidgetNotifier widgets = Substitute.For<IWidgetNotifier>();
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid channel = Guid.CreateVersion7();
        Widget countdown = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = channel,
            Name = "Ad countdown",
            IsEnabled = true,
            EventSubscriptions = ["ad_schedule", "ad_upcoming"],
        };
        Widget bystander = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = channel,
            Name = "Now playing",
            IsEnabled = true,
            EventSubscriptions = ["now_playing"],
        };
        db.Widgets.AddRange(countdown, bystander);
        await db.SaveChangesAsync();
        AdScheduleBroadcastHandler handler = new(db, widgets);

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = channel,
                NextAdAt = NextAd,
                LastAdAt = NextAd.AddMinutes(-60),
                DurationSeconds = 90,
                PrerollFreeTimeSeconds = 0,
                SnoozeCount = 3,
                SnoozeRefreshAt = NextAd.AddMinutes(30),
                TimeUntilNextAdSeconds = 180,
            }
        );

        await widgets
            .Received(1)
            .SendWidgetEventAsync(
                channel.ToString(),
                countdown.Id.ToString(),
                Arg.Is<WidgetEventDto>(evt =>
                    evt.EventType == "ad_schedule"
                    && Wire(evt.Data).GetProperty("durationSeconds").GetInt32() == 90
                    && Wire(evt.Data).GetProperty("snoozeCount").GetInt32() == 3
                    && Wire(evt.Data).GetProperty("timeUntilNextAdSeconds").GetInt32() == 180
                    && Wire(evt.Data).GetProperty("nextAdAt").GetDateTimeOffset() == NextAd
                ),
                Arg.Any<CancellationToken>()
            );
        await widgets
            .DidNotReceive()
            .SendWidgetEventAsync(
                channel.ToString(),
                bystander.Id.ToString(),
                Arg.Any<WidgetEventDto>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_crossed_threshold_reaches_the_subscribed_widget_as_ad_upcoming()
    {
        IWidgetNotifier widgets = Substitute.For<IWidgetNotifier>();
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid channel = Guid.CreateVersion7();
        Widget countdown = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = channel,
            Name = "Ad countdown",
            IsEnabled = true,
            EventSubscriptions = ["ad_upcoming"],
        };
        db.Widgets.Add(countdown);
        await db.SaveChangesAsync();
        AdUpcomingBroadcastHandler handler = new(db, widgets);

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = channel,
                SecondsUntilAd = 58,
                ThresholdSeconds = 60,
                DurationSeconds = 90,
                NextAdAt = NextAd,
            }
        );

        await widgets
            .Received(1)
            .SendWidgetEventAsync(
                channel.ToString(),
                countdown.Id.ToString(),
                Arg.Is<WidgetEventDto>(evt =>
                    evt.EventType == "ad_upcoming"
                    && Wire(evt.Data).GetProperty("secondsUntilAd").GetInt32() == 58
                    && Wire(evt.Data).GetProperty("thresholdSeconds").GetInt32() == 60
                    && Wire(evt.Data).GetProperty("durationSeconds").GetInt32() == 90
                ),
                Arg.Any<CancellationToken>()
            );
    }
}
