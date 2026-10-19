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
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves <see cref="NowPlayingBroadcastHandler"/> pushes the one now-playing shape as the hub event
/// <c>music.nowPlaying</c>, filled the same way for a Spotify track and a YouTube track.
/// </summary>
public sealed class NowPlayingBroadcastHandlerTests
{
    private static NowPlayingShapeDto? PushedShape(IDashboardNotifier notifier, string channel)
    {
        IEnumerable<object?> pushed = notifier
            .ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IDashboardNotifier.NotifyChannelAsync))
            .Where(c => (string)c.GetArguments()[0]! == channel)
            .Where(c => (string)c.GetArguments()[1]! == "music.nowPlaying")
            .Select(c => c.GetArguments()[2]);
        return pushed.SingleOrDefault() as NowPlayingShapeDto;
    }

    private static async Task<NowPlayingShapeDto?> Push(EngineKind engine, string provider)
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
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        NowPlayingBroadcastHandler handler = new(notifier, state);

        await handler.HandleAsync(
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
                VolumePercent = 99,
            }
        );

        return PushedShape(notifier, channel.ToString());
    }

    [Fact]
    public async Task Spotify_change_pushes_the_shape_with_the_director_level()
    {
        NowPlayingShapeDto? shape = await Push(EngineKind.Spotify, "spotify");

        shape
            .Should()
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
    public async Task YouTube_change_pushes_the_same_shape_with_only_the_engine_changed()
    {
        NowPlayingShapeDto? spotify = await Push(EngineKind.Spotify, "spotify");
        NowPlayingShapeDto? youtube = await Push(EngineKind.YouTube, "youtube");

        youtube.Should().NotBeNull();
        youtube!.Engine.Should().Be("youtube");
        youtube.Should().BeEquivalentTo(spotify!, o => o.Excluding(s => s.Engine));
    }

    [Fact]
    public async Task Idle_change_pushes_an_empty_card_with_the_idle_state()
    {
        Guid channel = Guid.CreateVersion7();
        IPlaybackStateReader state = Substitute.For<IPlaybackStateReader>();
        state.StateOf(channel).Returns(PlaybackState.Idle);
        state.LevelOf(channel).Returns(PlaybackLevel.Default);
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();

        await new NowPlayingBroadcastHandler(notifier, state).HandleAsync(
            new() { BroadcasterId = channel, IsPlaying = false }
        );

        NowPlayingShapeDto? shape = PushedShape(notifier, channel.ToString());
        shape.Should().NotBeNull();
        shape!.State.Should().Be("idle");
        shape.Title.Should().BeEmpty();
        shape.IsPlaying.Should().BeFalse();
    }
}
