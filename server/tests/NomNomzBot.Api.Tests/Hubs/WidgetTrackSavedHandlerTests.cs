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
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Widgets.Entities;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves a track save/unsave reaches the <c>now_playing</c> overlay's heart-pulse animation as its own
/// transient <c>track_saved_changed</c> event — distinct from the standing now_playing snapshot, and only
/// delivered to widgets that actually subscribe to it.
/// </summary>
public sealed class WidgetTrackSavedHandlerTests
{
    [Fact]
    public async Task A_like_reaches_a_subscribed_widget_carrying_the_saved_flag()
    {
        IWidgetNotifier widgets = Substitute.For<IWidgetNotifier>();
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid channel = Guid.CreateVersion7();
        Widget nowPlaying = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = channel,
            Name = "Now playing",
            IsEnabled = true,
            EventSubscriptions = ["now_playing", "track_saved_changed"],
        };
        db.Widgets.Add(nowPlaying);
        await db.SaveChangesAsync();
        WidgetTrackSavedHandler handler = new(db, widgets);

        await handler.HandleAsync(
            new TrackSavedChangedEvent
            {
                BroadcasterId = channel,
                TrackUri = "spotify:track:abc123",
                TrackName = "Song A",
                Artist = "Artist A",
                IsSaved = true,
            }
        );

        await widgets
            .Received(1)
            .SendWidgetEventAsync(
                channel.ToString(),
                nowPlaying.Id.ToString(),
                Arg.Is<WidgetEventDto>(evt =>
                    evt.EventType == "track_saved_changed" && PayloadMatches(evt.Data)
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_widget_not_subscribed_to_the_event_never_receives_it()
    {
        IWidgetNotifier widgets = Substitute.For<IWidgetNotifier>();
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid channel = Guid.CreateVersion7();
        db.Widgets.Add(
            new Widget
            {
                Id = Guid.NewGuid(),
                BroadcasterId = channel,
                Name = "Now playing",
                IsEnabled = true,
                EventSubscriptions = ["now_playing"],
            }
        );
        await db.SaveChangesAsync();
        WidgetTrackSavedHandler handler = new(db, widgets);

        await handler.HandleAsync(
            new TrackSavedChangedEvent
            {
                BroadcasterId = channel,
                TrackUri = "spotify:track:abc123",
                IsSaved = false,
            }
        );

        await widgets
            .DidNotReceiveWithAnyArgs()
            .SendWidgetEventAsync(default!, default!, default!, default);
    }

    private static bool PayloadMatches(object? data)
    {
        if (data is null)
            return false;
        JsonElement json = JsonSerializer.SerializeToElement(
            data,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
        );
        return json.GetProperty("trackUri").GetString() == "spotify:track:abc123"
            && json.GetProperty("track").GetString() == "Song A"
            && json.GetProperty("artist").GetString() == "Artist A"
            && json.GetProperty("isSaved").GetBoolean();
    }
}
