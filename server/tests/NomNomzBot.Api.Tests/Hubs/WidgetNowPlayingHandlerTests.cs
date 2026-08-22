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
/// Proves a playback-state change reaches the <c>now_playing</c> overlay widget carrying provider,
/// artist, album art, and track URI — required so the merged now-playing widget can tell Spotify from
/// YouTube tracks and render each provider's playback mode.
/// </summary>
public sealed class WidgetNowPlayingHandlerTests
{
    [Fact]
    public async Task Playback_change_carries_provider_artist_art_and_track_uri_to_the_widget()
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
            EventSubscriptions = ["now_playing"],
        };
        db.Widgets.Add(nowPlaying);
        await db.SaveChangesAsync();
        WidgetNowPlayingHandler handler = new(db, widgets);

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = channel,
                IsPlaying = true,
                TrackName = "Song A",
                Artist = "Artist A",
                AlbumArtUrl = "https://example.com/art.png",
                Provider = "youtube",
                TrackUri = "https://www.youtube.com/watch?v=abc123",
                DurationMs = 210_000,
                ProgressMs = 45_000,
            }
        );

        await widgets
            .Received(1)
            .SendWidgetEventAsync(
                channel.ToString(),
                nowPlaying.Id.ToString(),
                Arg.Is<WidgetEventDto>(evt =>
                    evt.EventType == "now_playing" && PayloadMatches(evt.Data)
                ),
                Arg.Any<CancellationToken>()
            );
    }

    private static bool PayloadMatches(object? data)
    {
        if (data is null)
            return false;
        JsonElement json = JsonSerializer.SerializeToElement(
            data,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
        );
        return json.GetProperty("isPlaying").GetBoolean()
            && json.GetProperty("track").GetString() == "Song A"
            && json.GetProperty("artist").GetString() == "Artist A"
            && json.GetProperty("artUrl").GetString() == "https://example.com/art.png"
            && json.GetProperty("provider").GetString() == "youtube"
            && json.GetProperty("trackUri").GetString() == "https://www.youtube.com/watch?v=abc123";
    }
}
