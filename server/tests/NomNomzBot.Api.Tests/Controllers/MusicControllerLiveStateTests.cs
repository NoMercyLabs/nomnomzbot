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

using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Music;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Stoney, 2026-11-17, on the Favorite player button: "this button should be the reactive button as it is on the
/// stream deck and respond to true or false". The queue read that feeds the dashboard's player buttons carries
/// the live states the Stream Deck keys show: saved (Liked Songs) and muted, as the now-playing read does.
/// </summary>
public sealed class MusicControllerLiveStateTests
{
    private static readonly Guid Channel = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string TrackUri = "spotify:track:made-up";

    private static NowPlaying Playing() =>
        new(
            "Song A",
            "Artist One",
            "Album",
            null,
            200_000,
            65_000,
            true,
            99,
            null,
            "spotify",
            TrackUri
        );

    private static MusicController Controller(bool? saved)
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetQueueAsync(Channel.ToString(), Arg.Any<CancellationToken>())
            .Returns(new MusicQueue(Playing(), []));
        music
            .GetNowPlayingAsync(Channel.ToString(), Arg.Any<CancellationToken>())
            .Returns(Playing());
        IMusicConfigService config = Substitute.For<IMusicConfigService>();
        config
            .GetConfigAsync(Channel.ToString(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<MusicConfigDto>("none", "NOT_FOUND"));
        IMusicHistoryReader history = Substitute.For<IMusicHistoryReader>();
        history
            .ResolveRequestersAsync(
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new Dictionary<string, RequesterIdentityDto>());
        PlaybackItem item = new(
            "item-1",
            "owner",
            EngineKind.Spotify,
            TrackUri,
            "Song A",
            ["Artist One"],
            null,
            200_000,
            "owner"
        );
        IPlaybackStateReader state = Substitute.For<IPlaybackStateReader>();
        state.StateOf(Channel).Returns(PlaybackState.Playing(EngineKind.Spotify, item));
        state.LevelOf(Channel).Returns(new PlaybackLevel(0, true));
        IMusicProviderManageApi manage = Substitute.For<IMusicProviderManageApi>();
        manage
            .AreTracksSavedAsync(
                Channel,
                "spotify",
                Arg.Is<IReadOnlyList<string>>(uris => uris.Count == 1 && uris[0] == TrackUri),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                saved is { } value
                    ? Result.Success<IReadOnlyList<bool>>([value])
                    : Result.Failure<IReadOnlyList<bool>>(
                        "no saving here",
                        "CAPABILITY_UNSUPPORTED"
                    )
            );

        return new MusicController(
            music,
            config,
            Substitute.For<ISongRequestPageTokenService>(),
            Substitute.For<IBlockedTrackService>(),
            history,
            Substitute.For<ISongRequestIntakeService>(),
            state,
            manage
        );
    }

    private static async Task<NowPlayingDto> QueueRead(bool? saved)
    {
        IActionResult result = await Controller(saved).GetQueue(Channel.ToString(), default);

        return ((StatusResponseDto<MusicQueueDto>)((OkObjectResult)result).Value!)
            .Data!
            .NowPlaying!;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_queue_read_says_whether_the_playing_song_is_saved(bool saved)
    {
        NowPlayingDto dto = await QueueRead(saved);

        dto.IsSaved.Should().Be(saved);
    }

    [Fact]
    public async Task A_provider_that_cannot_save_leaves_the_saved_state_unknown()
    {
        NowPlayingDto dto = await QueueRead(null);

        dto.IsSaved.Should().BeNull();
    }

    [Fact]
    public async Task The_queue_read_carries_the_mute_state_as_the_now_playing_read_does()
    {
        NowPlayingDto queue = await QueueRead(true);
        IActionResult now = await Controller(true).GetNowPlaying(Channel.ToString(), default);
        NowPlayingDto nowPlaying = (
            (StatusResponseDto<NowPlayingDto>)((OkObjectResult)now).Value!
        ).Data!;

        queue.Muted.Should().BeTrue();
        queue.State.Should().Be("playing");
        nowPlaying.IsSaved.Should().BeTrue();
        nowPlaying.Muted.Should().BeTrue();
    }
}
