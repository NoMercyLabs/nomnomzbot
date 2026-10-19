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
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Domain.Music.ValueObjects;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// The one now-playing shape (music-playback-determinism.md section 2.1): every field is filled the same way
/// for a Spotify track and a YouTube track; only the engine differs.
/// </summary>
public sealed class DirectorNowPlayingTests
{
    private static readonly PlaybackLevel Level = new(40, true);

    private static PlaybackItem ItemOf(EngineKind engine, string uri) =>
        new(
            "item-1",
            "owner",
            engine,
            uri,
            "Song A",
            ["Artist One", "Artist Two"],
            "https://img/a.jpg",
            200_000,
            "viewer1"
        );

    private static DirectorNowPlaying? BuildFor(EngineKind engine, string provider, string uri) =>
        DirectorNowPlaying.Build(
            PlaybackState.Playing(engine, ItemOf(engine, uri)),
            Level,
            provider,
            uri,
            "Song A",
            "Artist One, Artist Two",
            "https://img/a.jpg",
            200_000,
            65_000,
            true,
            "viewer1"
        );

    [Fact]
    public void Spotify_item_fills_every_field()
    {
        DirectorNowPlaying? shape = BuildFor(EngineKind.Spotify, "spotify", "spotify:track:abc");

        shape
            .Should()
            .BeEquivalentTo(
                new DirectorNowPlaying(
                    EngineKind.Spotify,
                    "item-1",
                    "Song A",
                    ["Artist One", "Artist Two"],
                    "https://img/a.jpg",
                    200_000,
                    65_000,
                    true,
                    "viewer1",
                    40,
                    true,
                    PlaybackStateKind.Playing
                )
            );
    }

    [Fact]
    public void YouTube_item_differs_from_Spotify_only_in_the_engine()
    {
        DirectorNowPlaying? spotify = BuildFor(EngineKind.Spotify, "spotify", "spotify:track:abc");
        DirectorNowPlaying? youtube = BuildFor(EngineKind.YouTube, "youtube", "yt-abc");

        youtube.Should().NotBeNull();
        youtube!.Engine.Should().Be(EngineKind.YouTube);
        youtube.Should().BeEquivalentTo(spotify!, o => o.Excluding(s => s.Engine));
    }

    [Fact]
    public void Wire_form_names_the_engine_and_the_camelCase_state()
    {
        DirectorNowPlaying shape = BuildFor(EngineKind.YouTube, "youtube", "yt-abc")!;
        NowPlayingShapeDto spotifyWire = NowPlayingShapeDto.From(
            shape with
            {
                Engine = EngineKind.Spotify,
                State = PlaybackStateKind.PausedByOwner,
            }
        );

        NowPlayingShapeDto.From(shape).Engine.Should().Be("youtube");
        spotifyWire.Engine.Should().Be("spotify");
        spotifyWire.State.Should().Be("pausedByOwner");
    }

    [Fact]
    public void A_report_for_another_track_than_the_director_item_has_no_item_id()
    {
        DirectorNowPlaying? shape = DirectorNowPlaying.Build(
            PlaybackState.Playing(
                EngineKind.Spotify,
                ItemOf(EngineKind.Spotify, "spotify:track:abc")
            ),
            Level,
            "spotify",
            "spotify:track:other",
            "Other",
            "Someone",
            null,
            1000,
            0,
            true,
            null
        );

        shape.Should().NotBeNull();
        shape!.ItemId.Should().BeNull();
        shape.Artists.Should().Equal("Someone");
        shape.RequestedBy.Should().BeNull();
    }

    [Fact]
    public void Nothing_loaded_builds_no_shape_and_the_wire_form_is_an_empty_card_with_the_state()
    {
        DirectorNowPlaying? shape = DirectorNowPlaying.Build(
            PlaybackState.Idle,
            Level,
            "spotify",
            null,
            null,
            null,
            null,
            0,
            0,
            false,
            null
        );

        shape.Should().BeNull();
        NowPlayingShapeDto empty = NowPlayingShapeDto.Empty(PlaybackStateKind.Idle, Level);
        empty.Title.Should().BeEmpty();
        empty.Artists.Should().BeEmpty();
        empty.IsPlaying.Should().BeFalse();
        empty.State.Should().Be("idle");
        empty.Volume.Should().Be(40);
        empty.Muted.Should().BeTrue();
    }
}
