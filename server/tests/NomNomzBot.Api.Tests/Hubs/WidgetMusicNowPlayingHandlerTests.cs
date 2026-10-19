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
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// S-PD-10 (music-playback-determinism.md row 21): a playback change reaches the widgets that subscribe to
/// <c>music.nowPlaying</c> as the one now-playing shape, every field filled for both engines, only Engine differs.
/// </summary>
public sealed class WidgetMusicNowPlayingHandlerTests
{
    private sealed record Pushed(string WidgetId, string EventType, object Data);

    private static async Task<List<Pushed>> Push(EngineKind engine, string provider, string[] subs)
    {
        Guid channel = Guid.CreateVersion7();
        string uri = engine == EngineKind.Spotify ? "spotify:track:abc" : "yt-abc";
        PlaybackItem item = new(
            "item-1",
            "owner",
            engine,
            uri,
            "Song A",
            ["Artist One"],
            "https://img/a.jpg",
            200_000,
            "viewer1"
        );
        IPlaybackStateReader state = Substitute.For<IPlaybackStateReader>();
        state.StateOf(channel).Returns(PlaybackState.Playing(engine, item));
        state.LevelOf(channel).Returns(new PlaybackLevel(40, true));
        IWidgetNotifier widgets = Substitute.For<IWidgetNotifier>();
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        db.Widgets.Add(
            new()
            {
                Id = Guid.NewGuid(),
                BroadcasterId = channel,
                Name = "Now playing",
                IsEnabled = true,
                EventSubscriptions = [.. subs],
            }
        );
        await db.SaveChangesAsync();

        await new WidgetMusicNowPlayingHandler(db, widgets, state).HandleAsync(
            new()
            {
                BroadcasterId = channel,
                IsPlaying = true,
                TrackName = "Song A",
                Artist = "Artist One",
                AlbumArtUrl = "https://img/a.jpg",
                DurationMs = 200_000,
                ProgressMs = 65_000,
                Provider = provider,
                TrackUri = uri,
                RequestedBy = "viewer1",
            }
        );

        return widgets
            .ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IWidgetNotifier.SendWidgetEventAsync))
            .Select(c => (WidgetEventDto)c.GetArguments()[2]!)
            .Select(e => new Pushed(e.WidgetId, e.EventType, e.Data!))
            .ToList();
    }

    [Fact]
    public async Task Spotify_change_reaches_a_subscribed_widget_as_the_shape()
    {
        List<Pushed> pushed = await Push(EngineKind.Spotify, "spotify", ["music.nowPlaying"]);

        pushed.Should().ContainSingle();
        pushed[0].EventType.Should().Be("music.nowPlaying");
        pushed[0]
            .Data.Should()
            .BeEquivalentTo(
                new NowPlayingShapeDto(
                    "spotify",
                    "item-1",
                    "Song A",
                    ["Artist One"],
                    "https://img/a.jpg",
                    200_000,
                    65_000,
                    true,
                    "viewer1",
                    40,
                    true,
                    "playing"
                )
            );
    }

    [Fact]
    public async Task YouTube_change_carries_the_same_shape_with_only_the_engine_changed()
    {
        List<Pushed> spotify = await Push(EngineKind.Spotify, "spotify", ["music.nowPlaying"]);
        List<Pushed> youtube = await Push(EngineKind.YouTube, "youtube", ["music.nowPlaying"]);

        NowPlayingShapeDto yt = (NowPlayingShapeDto)youtube.Single().Data;
        yt.Engine.Should().Be("youtube");
        yt.Should()
            .BeEquivalentTo(
                (NowPlayingShapeDto)spotify.Single().Data,
                o => o.Excluding(s => s.Engine)
            );
    }

    [Fact]
    public async Task A_widget_subscribed_only_to_the_old_key_gets_no_shape()
    {
        List<Pushed> pushed = await Push(EngineKind.Spotify, "spotify", ["now_playing"]);

        pushed.Should().BeEmpty();
    }
}
