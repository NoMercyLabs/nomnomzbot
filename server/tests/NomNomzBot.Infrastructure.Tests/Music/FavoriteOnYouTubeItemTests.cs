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
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Music;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Music.PipelineActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Owner request 2026-11-17 part 3: Favorite on a playing YouTube video is a like on that video, even
/// while Spotify is the channel's active provider. The write routes by the playing item's provider.
/// </summary>
public sealed class FavoriteOnYouTubeItemTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000ac006");

    private static PipelineExecutionContext Ctx() =>
        new()
        {
            BroadcasterId = ChannelId,
            TriggeredByUserId = "twitch-42",
            TriggeredByDisplayName = "Bamo",
            MessageId = "msg-1",
            RawMessage = "!cmd",
        };

    private static ActionDefinition Def(string type) => new() { Type = type };

    private static IMusicService SpotifyActiveYouTubeItem()
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
                    "yt-video-1"
                )
            );
        return music;
    }

    private static IMusicProviderManageApi ManageApiWhereSavedIs(bool saved)
    {
        IMusicProviderManageApi manageApi = Substitute.For<IMusicProviderManageApi>();
        manageApi
            .AreTracksSavedAsync(
                ChannelId,
                "youtube",
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success<IReadOnlyList<bool>>([saved]));
        manageApi
            .SaveTracksAsync(
                ChannelId,
                "youtube",
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        manageApi
            .RemoveSavedTracksAsync(
                ChannelId,
                "youtube",
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        return manageApi;
    }

    [Fact]
    public async Task Toggle_saved_on_a_youtube_item_likes_the_video_through_the_youtube_provider()
    {
        IMusicProviderManageApi manageApi = ManageApiWhereSavedIs(false);
        IEventBus eventBus = Substitute.For<IEventBus>();
        MusicToggleSavedAction action = new(
            SpotifyActiveYouTubeItem(),
            manageApi,
            MusicWriteQueueTestKit.Over(manageApi),
            eventBus
        );

        ActionResult result = await action.ExecuteAsync(Ctx(), Def("music_toggle_saved"));

        result.Succeeded.Should().BeTrue();
        await manageApi
            .Received(1)
            .SaveTracksAsync(
                ChannelId,
                "youtube",
                Arg.Is<IReadOnlyList<string>>(l => l.Count == 1 && l[0] == "yt-video-1"),
                Arg.Any<CancellationToken>()
            );
        await eventBus
            .Received(1)
            .PublishAsync(
                Arg.Is<TrackSavedChangedEvent>(e => e.IsSaved && e.TrackUri == "yt-video-1"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Toggle_saved_on_an_already_liked_youtube_item_removes_the_like()
    {
        IMusicProviderManageApi manageApi = ManageApiWhereSavedIs(true);
        IEventBus eventBus = Substitute.For<IEventBus>();
        MusicToggleSavedAction action = new(
            SpotifyActiveYouTubeItem(),
            manageApi,
            MusicWriteQueueTestKit.Over(manageApi),
            eventBus
        );

        ActionResult result = await action.ExecuteAsync(Ctx(), Def("music_toggle_saved"));

        result.Succeeded.Should().BeTrue();
        await manageApi
            .Received(1)
            .RemoveSavedTracksAsync(
                ChannelId,
                "youtube",
                Arg.Is<IReadOnlyList<string>>(l => l.Count == 1 && l[0] == "yt-video-1"),
                Arg.Any<CancellationToken>()
            );
        await eventBus
            .Received(1)
            .PublishAsync(
                Arg.Is<TrackSavedChangedEvent>(e => !e.IsSaved),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Save_track_on_a_youtube_item_likes_the_video_through_the_youtube_provider()
    {
        IMusicProviderManageApi manageApi = ManageApiWhereSavedIs(false);
        MusicSaveTrackAction action = new(
            SpotifyActiveYouTubeItem(),
            MusicWriteQueueTestKit.Over(manageApi),
            Substitute.For<IEventBus>()
        );

        ActionResult result = await action.ExecuteAsync(Ctx(), Def("music_save_track"));

        result.Succeeded.Should().BeTrue();
        await manageApi
            .Received(1)
            .SaveTracksAsync(
                ChannelId,
                "youtube",
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Unsave_track_on_a_youtube_item_removes_the_like_through_the_youtube_provider()
    {
        IMusicProviderManageApi manageApi = ManageApiWhereSavedIs(true);
        MusicUnsaveTrackAction action = new(
            SpotifyActiveYouTubeItem(),
            MusicWriteQueueTestKit.Over(manageApi),
            Substitute.For<IEventBus>()
        );

        ActionResult result = await action.ExecuteAsync(Ctx(), Def("music_unsave_track"));

        result.Succeeded.Should().BeTrue();
        await manageApi
            .Received(1)
            .RemoveSavedTracksAsync(
                ChannelId,
                "youtube",
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            );
    }
}
