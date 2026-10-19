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
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Music;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Music.PipelineActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// music-playback-determinism.md §3 and §7 row 29: account features with no YouTube counterpart (save track,
/// follow artist, add to a Spotify playlist) act on the Spotify catalog id of the item. For a YouTube item
/// they return the typed <c>NotSupportedForEngine</c> result and make no Spotify call.
/// </summary>
public sealed class AccountActionsOnYouTubeItemTests
{
    private const string NotSupported = "NotSupportedForEngine";
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000ac005");

    private static PipelineExecutionContext Ctx() =>
        new()
        {
            BroadcasterId = ChannelId,
            TriggeredByUserId = "twitch-42",
            TriggeredByDisplayName = "Bamo",
            MessageId = "msg-1",
            RawMessage = "!cmd",
        };

    private static ActionDefinition Def(
        string type,
        params (string Key, string Value)[] parameters
    ) =>
        new()
        {
            Type = type,
            Parameters = parameters.ToDictionary(
                p => p.Key,
                p => JsonSerializer.SerializeToElement(p.Value)
            ),
        };

    /// <summary>A YouTube item playing while the channel's Spotify account is the active provider.</summary>
    private static IMusicService SpotifyAccountYouTubeItem()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetActiveProviderKeyAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns("spotify");
        music
            .GetNowPlayingAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns(
                new NowPlaying(
                    "Clip",
                    "Channel",
                    null,
                    null,
                    200_000,
                    1_000,
                    true,
                    100,
                    null,
                    "youtube",
                    "yt-video-1",
                    ArtistId: "yt-channel-1"
                )
            );
        return music;
    }

    private static void AssertRefusedWithNoSpotifyCall(
        ActionResult result,
        IMusicProviderManageApi manageApi
    )
    {
        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Be(NotSupported);
        manageApi.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Save_track_on_a_youtube_item_is_not_supported_and_calls_no_spotify_api()
    {
        IMusicProviderManageApi manageApi = Substitute.For<IMusicProviderManageApi>();
        MusicSaveTrackAction action = new(
            SpotifyAccountYouTubeItem(),
            manageApi,
            Substitute.For<IEventBus>()
        );

        ActionResult result = await action.ExecuteAsync(Ctx(), Def("music_save_track"));

        AssertRefusedWithNoSpotifyCall(result, manageApi);
    }

    [Fact]
    public async Task Unsave_track_on_a_youtube_item_is_not_supported_and_calls_no_spotify_api()
    {
        IMusicProviderManageApi manageApi = Substitute.For<IMusicProviderManageApi>();
        MusicUnsaveTrackAction action = new(
            SpotifyAccountYouTubeItem(),
            manageApi,
            Substitute.For<IEventBus>()
        );

        ActionResult result = await action.ExecuteAsync(Ctx(), Def("music_unsave_track"));

        AssertRefusedWithNoSpotifyCall(result, manageApi);
    }

    [Fact]
    public async Task Toggle_saved_on_a_youtube_item_is_not_supported_and_calls_no_spotify_api()
    {
        IMusicProviderManageApi manageApi = Substitute.For<IMusicProviderManageApi>();
        MusicToggleSavedAction action = new(
            SpotifyAccountYouTubeItem(),
            manageApi,
            Substitute.For<IEventBus>()
        );

        ActionResult result = await action.ExecuteAsync(Ctx(), Def("music_toggle_saved"));

        AssertRefusedWithNoSpotifyCall(result, manageApi);
    }

    [Fact]
    public async Task Follow_artist_on_a_youtube_item_is_not_supported_and_calls_no_spotify_api()
    {
        IMusicProviderManageApi manageApi = Substitute.For<IMusicProviderManageApi>();
        MusicFollowArtistAction action = new(SpotifyAccountYouTubeItem(), manageApi);

        ActionResult result = await action.ExecuteAsync(Ctx(), Def("music_follow_artist"));

        AssertRefusedWithNoSpotifyCall(result, manageApi);
    }

    [Fact]
    public async Task Unfollow_artist_on_a_youtube_item_is_not_supported_and_calls_no_spotify_api()
    {
        IMusicProviderManageApi manageApi = Substitute.For<IMusicProviderManageApi>();
        MusicUnfollowArtistAction action = new(SpotifyAccountYouTubeItem(), manageApi);

        ActionResult result = await action.ExecuteAsync(Ctx(), Def("music_unfollow_artist"));

        AssertRefusedWithNoSpotifyCall(result, manageApi);
    }

    [Fact]
    public async Task Add_to_playlist_on_a_youtube_item_is_not_supported_and_calls_no_spotify_api()
    {
        IMusicProviderManageApi manageApi = Substitute.For<IMusicProviderManageApi>();
        MusicAddToPlaylistAction action = new(SpotifyAccountYouTubeItem(), manageApi);

        ActionResult result = await action.ExecuteAsync(
            Ctx(),
            Def("music_add_to_playlist", ("playlistId", "playlist-1"))
        );

        AssertRefusedWithNoSpotifyCall(result, manageApi);
    }

    [Fact]
    public async Task Remove_from_playlist_on_a_youtube_item_is_not_supported_and_calls_no_spotify_api()
    {
        IMusicProviderManageApi manageApi = Substitute.For<IMusicProviderManageApi>();
        MusicRemoveFromPlaylistAction action = new(SpotifyAccountYouTubeItem(), manageApi);

        ActionResult result = await action.ExecuteAsync(
            Ctx(),
            Def("music_remove_from_playlist", ("playlistId", "playlist-1"))
        );

        AssertRefusedWithNoSpotifyCall(result, manageApi);
    }

    [Fact]
    public void The_shared_playlist_track_check_names_the_engine_gap_for_a_youtube_item_on_spotify()
    {
        NowPlaying youtubeItem = new(
            "Clip",
            "Channel",
            null,
            null,
            1,
            1,
            true,
            100,
            null,
            "youtube",
            "yt-1"
        );

        Result<NowPlaying> result = PlaylistProviderChoice.RequireTrackFrom(youtubeItem, "spotify");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(NotSupported);
        result.ErrorMessage.Should().Be(NotSupported);
    }
}
