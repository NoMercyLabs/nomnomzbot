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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Music;

/// <summary>
/// The music fixture of catalog section 5: Song A by Artist A at 1:23 of 3:30 (requested by ViewerTwo), five
/// songs queued, ViewerOne at place 3 with two songs; the clock is 2026-10-26T14:30Z in Europe/Amsterdam and
/// the stream went live at 12:15Z. Fakes sit at the service interfaces; the resolver is the real one.
/// </summary>
internal sealed class MusicHelperFixture : IAsyncDisposable
{
    public static readonly Guid ChannelA = Guid.Parse("00000000-0000-0000-0000-00000000000a");

    private static readonly Guid ViewerOneId = Guid.Parse("00000000-0000-0000-0000-000000001001");
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 14, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LiveSince = new(2026, 10, 26, 12, 15, 0, TimeSpan.Zero);

    private readonly ServiceProvider _services;
    private readonly TemplateResolver _resolver;

    public MusicHelperFixture(bool idle = false)
    {
        IChannelRegistry channels = Substitute.For<IChannelRegistry>();
        channels.Get(ChannelA).Returns(Context());

        Music = Substitute.For<IMusicService>();
        Music
            .GetNowPlayingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(idle ? null : NowPlayingA());
        Music
            .GetQueueAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MusicQueue(null, idle ? [] : QueueItems()));
        Music.SnapshotQueue(Arg.Any<string>()).Returns(idle ? [] : Snapshot());
        Music
            .GetActiveProviderKeyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("spotify");

        History = Substitute.For<IMusicHistoryReader>();
        History
            .GetRecentAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(HistoryRows()));
        History
            .GetTopRequestersAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new List<TopRequesterDto>()));

        IPlaybackStateReader playback = Substitute.For<IPlaybackStateReader>();
        playback
            .StateOf(ChannelA)
            .Returns(idle ? PlaybackState.Idle : new PlaybackState(PlaybackStateKind.Playing));

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
            .GetProfileAsync(ChannelA, ViewerOneId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(Profile()));

        _services = new ServiceCollection()
            .AddSingleton(Music)
            .AddSingleton(History)
            .AddSingleton(playback)
            .AddSingleton(config)
            .AddSingleton(analytics)
            .AddSingleton<IViewerDirectory, FakeDirectory>()
            .AddTemplateHelpers(typeof(TemplateResolver).Assembly)
            .BuildServiceProvider();
        Registry = _services.GetRequiredService<ITemplateHelperRegistry>();
        _resolver = new TemplateResolver(
            _services.GetRequiredService<IServiceScopeFactory>(),
            channels,
            NullLogger<TemplateResolver>.Instance,
            new FakeTimeProvider(Now),
            Registry
        );
    }

    public IMusicService Music { get; }

    public IMusicHistoryReader History { get; }

    public ITemplateHelperRegistry Registry { get; }

    /// <summary>The seeds of a chat command typed by ViewerOne, naming ViewerTwo as its first argument.</summary>
    public static Dictionary<string, string> Seeds() =>
        new()
        {
            ["user"] = "ViewerOne",
            ["user.name"] = "viewerone",
            ["user.id"] = "1001",
            ["user.provider"] = "twitch",
            ["target"] = "@ViewerTwo",
        };

    public Task<string> RenderAsync(string template, Dictionary<string, string>? seeds = null) =>
        _resolver.ResolveAsync(template, seeds ?? Seeds(), ChannelA);

    public ValueTask DisposeAsync() => _services.DisposeAsync();

    private static ChannelContext Context() =>
        new()
        {
            BroadcasterId = ChannelA,
            TwitchChannelId = "123456",
            ChannelName = "samplechannel",
            DisplayName = "SampleChannel",
            IsLive = true,
            WentLiveAt = LiveSince,
            Timezone = "Europe/Amsterdam",
            Language = "en",
        };

    private static NowPlaying NowPlayingA() =>
        new(
            "Song A",
            "Artist A",
            "Album A",
            "https://i.scdn.co/image/sample",
            210000,
            83000,
            true,
            60,
            "ViewerTwo",
            "spotify",
            "spotify:track:sample"
        );

    private static List<MusicQueueItem> QueueItems() =>
        [
            Item("Song B", "Artist B", "ViewerThree", 180),
            Item("Song C", "Artist C", "ViewerOne", 200),
            Item("Song D", "Artist D", "ViewerFour", 250),
            Item("Song E", "Artist E", "ViewerOne", 210),
            Item("Song F", "Artist F", "ViewerTwo", 200),
        ];

    private static MusicQueueItem Item(string title, string artist, string by, int seconds) =>
        new(title, artist, null, seconds * 1000, by, 300);

    private static List<SongRequestQueueSnapshotItem> Snapshot() =>
        [
            new("Song B", "ViewerThree", 180, 1, "A1B2"),
            new("Song G", "ViewerTwo", 153, 2, "C3D4"),
            new("Song C", "ViewerOne", 200, 3, "K7QM"),
            new("Song D", "ViewerFour", 250, 4, "E5F6"),
            new("Song E", "ViewerOne", 210, 5, "G7H8"),
        ];

    /// <summary>
    /// Newest first: 21 requests since the stream went live (ViewerTwo 4, ViewerOne 2, Artist A 7) and one
    /// from before it that must not count.
    /// </summary>
    private static List<SongHistoryItemDto> HistoryRows()
    {
        List<SongHistoryItemDto> rows =
        [
            Row("Song Z", "Artist Z", "ViewerThree", 0),
            Row("Song Y", "Artist Y", "ViewerFour", 1),
            Row("Song X", "Artist X", "ViewerThree", 2),
            Row("Song C", "Artist C", "ViewerOne", 3),
            Row("Song A", "Artist A", "ViewerTwo", 4),
            Row("Song A", "Artist A", "ViewerTwo", 5),
            Row("Song A", "Artist A", "ViewerTwo", 6),
            Row("Song A", "Artist A", "ViewerTwo", 7),
            Row("Song A2", "Artist A", "ViewerFive", 8),
            Row("Song A2", "Artist A", "ViewerFive", 9),
            Row("Song A2", "Artist A", "ViewerFive", 10),
            Row("Song H", "Artist H", "ViewerOne", 11),
        ];
        for (int i = 12; i <= 20; i++)
            rows.Add(Row("Song " + i, "Artist " + i, "Fan" + i, i));

        rows.Add(
            new SongHistoryItemDto(
                "Song Old",
                "Artist A",
                null,
                "spotify",
                "9",
                "ViewerTwo",
                new DateTime(2026, 10, 24, 20, 0, 0, DateTimeKind.Utc)
            )
        );
        return rows;
    }

    private static SongHistoryItemDto Row(string title, string artist, string by, int index) =>
        new(
            title,
            artist,
            null,
            "spotify",
            by,
            by,
            new DateTime(2026, 10, 26, 14, 0, 0, DateTimeKind.Utc).AddMinutes(-5 * index)
        );

    private static ViewerProfileDto Profile() =>
        new(
            ViewerOneId,
            "1001",
            "ViewerOne",
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
            false,
            0
        );

    private sealed class FakeDirectory : IViewerDirectory
    {
        public Task<ViewerRef?> ResolveAsync(
            IServiceProvider services,
            ViewerSide side,
            IReadOnlyDictionary<string, string> seeds,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<ViewerRef?>(
                side == ViewerSide.User
                    ? new ViewerRef(ViewerOneId, "1001", "viewerone", "ViewerOne", "twitch")
                    : null
            );
    }
}
