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

using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Music;

/// <summary>The music namespace's outside-world answers for the cross-surface matrix.</summary>
internal static class MusicSurfaceFixture
{
    private static readonly Guid ViewerId = Guid.Parse("0192b400-0000-7000-8000-00000000f042");

    public static void Seed(TemplateHelperContext surface, Dictionary<string, string> seeds)
    {
        if (surface != TemplateHelperContext.Pipeline)
            return;

        // SongRequestFavoriteAction writes these outputs after its pipeline step completes.
        seeds["music.favorite.track"] = "Song A";
        seeds["music.favorite.artist"] = "Artist A";
        seeds["music.favorite.count"] = "1";
        seeds["music.favorite.outcome"] = "added";
    }

    public static void Configure(ServiceCollection services, Guid channel)
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetNowPlayingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(
                new NowPlaying(
                    "Song A",
                    "Artist A",
                    "Album A",
                    "https://i.scdn.co/image/sample",
                    210000,
                    83000,
                    true,
                    60,
                    "samplefriend",
                    "spotify",
                    "spotify:track:sample"
                )
            );
        music
            .GetQueueAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                new MusicQueue(
                    null,
                    [
                        new MusicQueueItem("Song B", "Artist B", null, 180000, "samplefriend", 300),
                        new MusicQueueItem("Song C", "Artist C", null, 200000, "SampleViewer", 300),
                        new MusicQueueItem(
                            "Song D",
                            "Artist D",
                            null,
                            250000,
                            "another viewer",
                            300
                        ),
                    ]
                )
            );
        music
            .SnapshotQueue(Arg.Any<string>())
            .Returns([
                new SongRequestQueueSnapshotItem("Song B", "samplefriend", 180, 1, "A1B2"),
                new SongRequestQueueSnapshotItem("Song C", "SampleViewer", 200, 2, "K7QM"),
                new SongRequestQueueSnapshotItem("Song D", "another viewer", 250, 3, "E5F6"),
            ]);
        music
            .GetActiveProviderKeyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("spotify");

        IMusicHistoryReader history = Substitute.For<IMusicHistoryReader>();
        history
            .GetRecentAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new List<SongHistoryItemDto>
                    {
                        new(
                            "Song A",
                            "Artist A",
                            null,
                            "spotify",
                            "sampleviewer",
                            "SampleViewer",
                            new DateTime(2026, 11, 3, 17, 0, 0, DateTimeKind.Utc)
                        ),
                    }
                )
            );
        history
            .GetTopRequestersAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new List<TopRequesterDto>()));

        IPlaybackStateReader playback = Substitute.For<IPlaybackStateReader>();
        playback.StateOf(channel).Returns(new PlaybackState(PlaybackStateKind.Playing));

        IMusicConfigService config = Substitute.For<IMusicConfigService>();
        config
            .GetConfigAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new MusicConfigDto(
                        true,
                        "spotify",
                        50,
                        3,
                        true,
                        true,
                        "everyone",
                        RequestsOpen: true,
                        MaxSongSeconds: 360
                    )
                )
            );

        IViewerAnalyticsService analytics = Substitute.For<IViewerAnalyticsService>();
        analytics
            .GetProfileAsync(channel, ViewerId, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new ViewerProfileDto(
                        ViewerId,
                        "100000042",
                        "SampleViewer",
                        null,
                        null,
                        0,
                        0,
                        0,
                        0,
                        9,
                        true,
                        true,
                        "1000",
                        false
                    )
                )
            );

        services.AddSingleton(music);
        services.AddSingleton(history);
        services.AddSingleton(playback);
        services.AddSingleton(config);
        services.AddSingleton(analytics);
        services.AddSingleton<IViewerDirectory, MatrixViewerDirectory>();
    }

    private sealed class MatrixViewerDirectory : IViewerDirectory
    {
        public Task<ViewerRef?> ResolveAsync(
            IServiceProvider services,
            ViewerSide side,
            IReadOnlyDictionary<string, string> seeds,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                side == ViewerSide.User
                && seeds.TryGetValue("user", out string? user)
                && !string.IsNullOrWhiteSpace(user)
                    ? new ViewerRef(ViewerId, "100000042", "sampleviewer", "SampleViewer", "twitch")
                    : null
            );
    }
}
