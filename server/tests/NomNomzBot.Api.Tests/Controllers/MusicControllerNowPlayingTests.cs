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
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// GET music/now-playing returns the one now-playing shape (music-playback-determinism.md section 2.1)
/// next to the older fields, filled the same way for a Spotify track and a YouTube track.
/// </summary>
public sealed class MusicControllerNowPlayingTests
{
    private static readonly Guid Channel = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static async Task<NowPlayingDto> Get(EngineKind engine, string provider, string uri)
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetNowPlayingAsync(Channel.ToString(), Arg.Any<CancellationToken>())
            .Returns(
                new NowPlaying(
                    "Song A",
                    "Artist One",
                    "Album",
                    "https://img/a.jpg",
                    200_000,
                    65_000,
                    true,
                    99,
                    "viewer1",
                    provider,
                    uri
                )
            );
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
        state.StateOf(Channel).Returns(PlaybackState.Playing(engine, item));
        state.LevelOf(Channel).Returns(new PlaybackLevel(40, true));
        MusicController controller = new(
            music,
            Substitute.For<IMusicConfigService>(),
            Substitute.For<ISongRequestPageTokenService>(),
            Substitute.For<IBlockedTrackService>(),
            Substitute.For<IMusicHistoryReader>(),
            Substitute.For<ISongRequestIntakeService>(),
            state
        );

        IActionResult result = await controller.GetNowPlaying(Channel.ToString(), default);

        StatusResponseDto<NowPlayingDto> body =
            (StatusResponseDto<NowPlayingDto>)((OkObjectResult)result).Value!;
        return body.Data!;
    }

    [Fact]
    public async Task Spotify_read_carries_the_shape_and_keeps_the_old_fields()
    {
        NowPlayingDto dto = await Get(EngineKind.Spotify, "spotify", "spotify:track:abc");

        dto.Engine.Should().Be("spotify");
        dto.ItemId.Should().Be("item-1");
        dto.Title.Should().Be("Song A");
        dto.Artists.Should().Equal("Artist One");
        dto.AlbumArtUrl.Should().Be("https://img/a.jpg");
        dto.Muted.Should().BeTrue();
        dto.State.Should().Be("playing");
        dto.TrackName.Should().Be("Song A");
        dto.Provider.Should().Be("spotify");
    }

    [Fact]
    public async Task YouTube_read_differs_from_Spotify_only_in_the_engine()
    {
        NowPlayingDto spotify = await Get(EngineKind.Spotify, "spotify", "spotify:track:abc");
        NowPlayingDto youtube = await Get(EngineKind.YouTube, "youtube", "yt-abc");

        youtube.Engine.Should().Be("youtube");
        youtube
            .Should()
            .BeEquivalentTo(
                spotify,
                o =>
                    o.Excluding(d => d.Engine).Excluding(d => d.Provider).Excluding(d => d.TrackUri)
            );
    }
}
