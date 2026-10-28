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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Music.Playback;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;
using static NomNomzBot.Infrastructure.Tests.Music.YouTubeDirectorKit;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// S-YOUTUBE-NOW-PLAYING: a YouTube request that ENDED leaves the song request queue and the next one plays.
/// The real music service, YouTube provider, player state store, report service and queue reconciler run
/// together; only the widget notifier, the overlay presence and the clock are fakes. The real event bus
/// delivers the published events to the reconciler.
/// </summary>
public sealed class YouTubeEndedAdvancesQueueTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192b000-0000-7000-8000-0000000a7c01");
    private static readonly Guid PlayerWidget = Guid.Parse("0192b000-0000-7000-8000-0000000a7c02");
    private const string First = "dQw4w9WgXcQ";
    private const string Second = "9bZkp7q19f0";
    private const string Third = "kJQP7kiw5Fk";

    private sealed class RecordingWidgetEventNotifier : IWidgetEventNotifier
    {
        public List<(Guid Widget, string EventType, object? Data)> Sent { get; } = [];

        public Task SendWidgetEventAsync(
            Guid broadcasterId,
            Guid widgetId,
            string eventType,
            object? data,
            CancellationToken ct = default
        )
        {
            Sent.Add((widgetId, eventType, data));
            return Task.CompletedTask;
        }

        public List<string> Of(string eventType) =>
            [.. Sent.Where(s => s.EventType == eventType).Select(s => s.EventType)];

        public List<string> PlayedVideoIds =>
            [
                .. Sent.Where(s => s.EventType == "youtube.play")
                    .Select(s => ((YouTubePlayWidgetPayload)s.Data!).VideoId),
            ];
    }

    private sealed record Rig(
        MusicService Music,
        YouTubeMusicProvider Provider,
        YouTubePlayerReportService Reports,
        SongRequestQueueStore Queues,
        RecordingWidgetEventNotifier Notifier,
        DispatchingEventBus Bus,
        RealYouTubeEngineFactory Engines,
        PlaybackDirectorRegistry Registry
    );

    private static string VideoJson(string id, string title) =>
        $$$"""
            {"items":[{"id":"{{{id}}}",
              "snippet":{"title":"{{{title}}}","channelTitle":"Some Channel","liveBroadcastContent":"none",
                "thumbnails":{"high":{"url":"https://i.ytimg.com/vi/{{{id}}}/hqdefault.jpg"} } },
              "contentDetails":{"duration":"PT3M"},
              "status":{"embeddable":true}}]}
            """;

    private static async Task<Rig> BuildAsync(bool attached = true)
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        MusicTestDbContext musicDb = MusicTestDbContext.New();
        db.Channels.Add(
            new()
            {
                Id = Broadcaster,
                OwnerUserId = Guid.NewGuid(),
                TwitchChannelId = "yt2",
                Name = "TubeStreamer",
                NameNormalized = "tubestreamer",
            }
        );
        db.Widgets.Add(
            new Widget
            {
                Id = PlayerWidget,
                BroadcasterId = Broadcaster,
                Name = "now playing",
                IsEnabled = true,
                EventSubscriptions = ["now_playing"],
            }
        );
        musicDb.Services.Add(
            new()
            {
                Id = Guid.NewGuid().ToString(),
                Name = "youtube",
                BroadcasterId = Broadcaster,
                Enabled = true,
                AccessToken = "test-access-token",
            }
        );
        await db.SaveChangesAsync();
        await musicDb.SaveChangesAsync();

        IOverlayPresenceRegistry presence = Substitute.For<IOverlayPresenceRegistry>();
        presence.IsWidgetAttached(Broadcaster, PlayerWidget).Returns(true);
        presence.GetMusicWidgetId(Broadcaster).Returns(PlayerWidget);

        FakeTimeProvider clock = new(DateTimeOffset.Parse("2026-10-04T12:00:00Z"));
        RecordingWidgetEventNotifier notifier = new();
        DispatchingEventBus bus = new();
        LateSink lateSink = new();
        YouTubePlayerStateStore playerStore = new(clock);
        YouTubePlayerDispatcher players = new(db, presence, notifier, playerStore);

        RecordingHttpHandler handler = new();
        handler.RespondWhen(
            r =>
                r.RequestUri!.AbsolutePath.EndsWith("/videos")
                && r.RequestUri.Query.Contains(First),
            System.Net.HttpStatusCode.OK,
            VideoJson(First, "First Song")
        );
        handler.RespondWhen(
            r =>
                r.RequestUri!.AbsolutePath.EndsWith("/videos")
                && r.RequestUri.Query.Contains(Second),
            System.Net.HttpStatusCode.OK,
            VideoJson(Second, "Second Song")
        );
        handler.RespondWhen(
            r =>
                r.RequestUri!.AbsolutePath.EndsWith("/videos")
                && r.RequestUri.Query.Contains(Third),
            System.Net.HttpStatusCode.OK,
            VideoJson(Third, "Third Song")
        );
        YouTubeMusicProvider provider = YouTubeProviderFactory.Create(
            apiKey: "test-key",
            handler: handler,
            db: db,
            playerState: playerStore,
            players: players,
            playback: lateSink
        );

        SongRequestQueueStore queues = new();
        // The music service sends its requests to THIS registry, so the real YouTube engine starts them.
        RealYouTubeEngineFactory engines = new(provider, players, playerStore);
        PlaybackDirectorRegistry registry = new(queues, engines, bus, TimeProvider.System);
        lateSink.Inner = registry;
        MusicService music = new(
            [provider],
            musicDb,
            bus,
            new BlockedTrackService(musicDb),
            queues,
            new NoOpSongRequestQueuePersistence(),
            NullLogger<MusicService>.Instance,
            new InMemoryIntegrationCapabilityStore(),
            PermissiveMusicConfigService.Instance,
            Substitute.For<ICurrencyAccountService>(),
            new NowPlayingCache(),
            new OutboundSanctionAccessor(),
            Substitute.For<IUserIdentityService>(),
            Substitute.For<IForeignLinkTitleLookup>(),
            registry,
            registry
        );
        YouTubePlayerReportService reports = new(
            playerStore,
            players,
            bus,
            registry,
            new PlayOnceResumeTracker(),
            NullLogger<YouTubePlayerReportService>.Instance
        );
        SongRequestQueueReconciler reconciler = new(
            queues,
            new NoOpSongRequestQueuePersistence(),
            bus,
            registry
        );
        bus.Deliver(
            new ServiceCollection()
                .AddSingleton<IEventHandler<PlaybackStateChangedEvent>>(reconciler)
                .BuildServiceProvider()
        );
        // The open player attaches before any request exists, which is how a channel gets its director.
        if (attached)
            await registry.SendAsync(Broadcaster, new OutputAttached());

        return new(music, provider, reports, queues, notifier, bus, engines, registry);
    }

    private static string Watch(string videoId) => $"https://www.youtube.com/watch?v={videoId}";

    private static async Task ReportAsync(Rig rig, string videoId, string state)
    {
        Result reported = await rig.Reports.ReportAsync(
            Broadcaster,
            PlayerWidget,
            videoId,
            state,
            1000
        );
        reported.IsSuccess.Should().BeTrue();
    }

    private static List<string> QueuedUris(Rig rig) =>
        rig
            .Queues.TryGet(Broadcaster.ToString())!
            .GetSnapshot()
            .Select(e => e.Item.TrackUri)
            .ToList();

    private static async Task<Rig> TwoQueuedAsync(bool attached = true)
    {
        Rig rig = await BuildAsync(attached);
        (await rig.Music.AddToQueueAsync(Broadcaster.ToString(), Watch(First), "viewer1"))
            .IsSuccess.Should()
            .BeTrue();
        (await rig.Music.AddToQueueAsync(Broadcaster.ToString(), Watch(Second), "viewer2"))
            .IsSuccess.Should()
            .BeTrue();
        return rig;
    }

    [Fact]
    public async Task The_first_request_goes_to_the_open_player_and_the_second_waits_in_the_queue()
    {
        Rig rig = await TwoQueuedAsync();

        rig.Notifier.PlayedVideoIds.Should().Equal(First);
        rig.Queues.GetInFlight(Broadcaster.ToString())!.TrackUri.Should().Be(Watch(First));
        QueuedUris(rig).Should().Equal(Watch(First), Watch(Second));
    }

    [Fact]
    public async Task A_playing_report_makes_the_request_the_now_playing_track()
    {
        Rig rig = await TwoQueuedAsync();

        await ReportAsync(rig, First, "playing");

        TrackInfo? current = await rig.Provider.GetCurrentTrackAsync(Broadcaster);
        current.Should().NotBeNull();
        current.TrackUri.Should().Be(Watch(First));
        current.IsPlaying.Should().BeTrue();
        QueuedUris(rig).Should().Equal([Watch(Second)], "the playing request left the queue");
        rig.Queues.GetInFlight(Broadcaster.ToString())
            .Should()
            .BeNull("the playing request freed the provider and nothing else is handed over");
        rig.Notifier.PlayedVideoIds.Should()
            .Equal([First], "the player is busy, so the second waits in the fair queue");
        (await rig.Provider.GetQueueAsync(Broadcaster))!
            .Should()
            .BeEmpty("the player holds no pushed next video; the director starts it at the end");
        rig.Engines.YouTube!.StartedVideoIds.Should().Equal([First]);
    }

    [Fact]
    public async Task An_ended_report_plays_the_next_request_exactly_once_and_it_then_leaves_the_queue()
    {
        Rig rig = await TwoQueuedAsync();
        await ReportAsync(rig, First, "playing");

        await ReportAsync(rig, First, "ended");

        rig.Engines.YouTube!.StartedVideoIds.Should()
            .Equal(
                [First, Second],
                "the director started the head, then the next request through the engine exactly once"
            );
        rig.Notifier.PlayedVideoIds.Should()
            .Equal([First, Second], "only the director pushes the next video");
        QueuedUris(rig)
            .Should()
            .Equal([Watch(Second)], "the second is handed over, not yet playing");
        rig.Queues.GetInFlight(Broadcaster.ToString())
            .Should()
            .BeNull("the director started the second; the intake path marks only the first");

        await ReportAsync(rig, Second, "playing");

        QueuedUris(rig).Should().BeEmpty();
        rig.Queues.GetInFlight(Broadcaster.ToString()).Should().BeNull();
        (await rig.Provider.GetCurrentTrackAsync(Broadcaster))!.TrackUri.Should().Be(Watch(Second));

        await ReportAsync(rig, Second, "ended");

        rig.Notifier.PlayedVideoIds.Should().Equal(First, Second);
        QueuedUris(rig).Should().BeEmpty();
    }

    [Fact]
    public async Task The_music_widget_becoming_ready_starts_the_idle_head_once_through_the_engine()
    {
        Rig rig = await TwoQueuedAsync();
        // The first request reached the open player through the legacy push; the director has no state yet.
        await rig.Registry.SendAsync(Broadcaster, new OutputAttached());

        rig.Engines.YouTube!.StartedVideoIds.Should()
            .Equal([First], "the director starts the head through the engine exactly once");
        await rig.Registry.SendAsync(Broadcaster, new OutputAttached());
        rig.Engines.YouTube.StartedVideoIds.Should()
            .Equal([First], "a second ready while the head is starting starts nothing again");
        QueuedUris(rig).Should().Equal(Watch(First), Watch(Second));
    }

    [Fact]
    public async Task A_play_once_video_with_no_resume_pending_ends_and_the_director_starts_the_waiting_request_once()
    {
        Rig rig = await TwoQueuedAsync();
        await ReportAsync(rig, First, "playing");

        // The play-once video is no request: it is pushed over the playing one, plays, and ends.
        (await rig.Music.PlayTrackOnceAsync(Broadcaster.ToString(), Watch(Third)))
            .IsSuccess.Should()
            .BeTrue();
        await ReportAsync(rig, Third, "playing");
        await ReportAsync(rig, Third, "ended");

        // Nothing is left to resume, so the ended once video hands the player to the waiting request.
        rig.Engines.YouTube!.StartedVideoIds.Should()
            .Equal(
                [First, Second],
                "the director started the head, then the waiting request exactly once"
            );
        rig.Notifier.PlayedVideoIds.Should().Equal([First, Third, Second]);
        QueuedUris(rig).Should().Equal([Watch(Second)], "handed over, not yet reported playing");
    }

    [Fact]
    public async Task A_skip_on_the_playing_youtube_head_stops_it_and_the_director_starts_the_next()
    {
        Rig rig = await TwoQueuedAsync();
        await rig.Registry.SendAsync(Broadcaster, new RequestAdded());
        await ReportAsync(rig, First, "playing");
        rig.Engines.YouTube!.StartedVideoIds.Should().Equal([First]);
        int playsBeforeSkip = rig.Notifier.PlayedVideoIds.Count;

        await rig.Provider.SkipAsync(Broadcaster);

        rig.Engines.YouTube.StartedVideoIds.Should()
            .Equal([First], "the director stops the player first and waits for it to confirm");
        rig.Notifier.Of("youtube.stop").Should().ContainSingle();

        // The player confirms the stop the way it ends a video.
        await ReportAsync(rig, First, "ended");

        rig.Engines.YouTube.StartedVideoIds.Should()
            .Equal(
                [First, Second],
                "the confirmed stop makes the director start the next request, once"
            );
        // The setup's own push of the head (the hand-over plus the explicit RequestAdded) is not the skip's.
        rig.Notifier.PlayedVideoIds.Skip(playsBeforeSkip)
            .Should()
            .Equal([Second], "the skip starts the next request once");
        QueuedUris(rig).Should().Equal([Watch(Second)]);
    }

    [Fact]
    public async Task An_ended_report_with_no_request_left_leaves_the_queue_empty_and_sends_nothing()
    {
        Rig rig = await BuildAsync();
        (await rig.Music.AddToQueueAsync(Broadcaster.ToString(), Watch(First), "viewer1"))
            .IsSuccess.Should()
            .BeTrue();
        await ReportAsync(rig, First, "playing");
        int sentBefore = rig.Notifier.Sent.Count;

        await ReportAsync(rig, First, "ended");

        rig.Notifier.Sent.Should().HaveCount(sentBefore);
        rig.Notifier.PlayedVideoIds.Should().Equal(First);
        QueuedUris(rig).Should().BeEmpty();
        rig.Queues.GetInFlight(Broadcaster.ToString()).Should().BeNull();
    }

    [Fact]
    public async Task A_late_ended_report_for_a_video_already_replaced_does_not_skip_the_one_playing()
    {
        Rig rig = await TwoQueuedAsync();
        (await rig.Music.AddToQueueAsync(Broadcaster.ToString(), Watch(Third), "viewer3"))
            .IsSuccess.Should()
            .BeTrue();
        await ReportAsync(rig, First, "playing");
        await ReportAsync(rig, First, "ended");
        await ReportAsync(rig, Second, "playing");

        // The first video's ENDED arrives again (a retry after a network blip, or a second report).
        int published = rig.Bus.Published.Count;
        Result late = await rig.Reports.ReportAsync(
            Broadcaster,
            PlayerWidget,
            First,
            "ended",
            1000
        );

        late.ErrorCode.Should().Be("STALE_VIDEO");
        rig.Bus.Published.Should().HaveCount(published, "a stale report changes nothing");
        rig.Engines.YouTube!.StartedVideoIds.Should()
            .Equal([First, Second], "the third request waits until the second ends");
        rig.Notifier.PlayedVideoIds.Should().Equal(First, Second);
        (await rig.Provider.GetCurrentTrackAsync(Broadcaster))!.TrackUri.Should().Be(Watch(Second));
        (await rig.Provider.GetQueueAsync(Broadcaster))!
            .Should()
            .BeEmpty("the third waits in the fair queue, not in the player");
        QueuedUris(rig).Should().Equal([Watch(Third)]);
    }

    [Fact]
    public async Task A_video_the_player_cannot_play_blocks_the_head_and_the_adapter_leaves_the_queue_to_the_director()
    {
        // A request always gives the channel a director, so the director takes the failure report. The
        // report service announces nothing itself (the second-error test covers the one notice).
        Rig rig = await TwoQueuedAsync();

        await ReportAsync(rig, First, "error");

        // Queue ownership (S-PD-3): the report adapter only reports. One failure blocks the head; the
        // director keeps it and waits for the output to come back.
        rig.Bus.Published.OfType<SongPlaybackFailedEvent>().Should().BeEmpty();
        PlaybackState state = rig.Registry.StateOf(Broadcaster);
        state.Kind.Should().Be(PlaybackStateKind.Blocked);
        rig.Engines.YouTube!.StartedVideoIds.Should()
            .Equal([First], "the director started the head once and nothing else");
        rig.Notifier.PlayedVideoIds.Should()
            .Equal([First], "the adapter hands nothing over by itself");
        QueuedUris(rig).Should().Equal(Watch(First), Watch(Second));
    }

    [Fact]
    public async Task A_second_error_on_the_head_removes_it_and_the_director_plays_the_next_with_one_notice()
    {
        Rig rig = await TwoQueuedAsync();
        // A director only exists once the channel's first playback input reaches the registry.
        await rig.Registry.SendAsync(Broadcaster, new RequestAdded());
        rig.Engines.YouTube!.StartedVideoIds.Should()
            .Equal([First], "the director starts the head");
        await ReportAsync(rig, First, "error");
        rig.Bus.Published.OfType<SongPlaybackFailedEvent>()
            .Should()
            .BeEmpty("one failure only blocks the head and the director took the report");
        await rig.Registry.SendAsync(Broadcaster, new OutputAttached());
        rig.Engines.YouTube.StartedVideoIds.Should()
            .Equal([First, First], "the output coming back retries the head once");

        await ReportAsync(rig, First, "error");

        QueuedUris(rig).Should().Equal([Watch(Second)], "the failed head left the queue");
        rig.Engines.YouTube.StartedVideoIds.Should()
            .Contain(Second, "the director started the next request through the engine");
        rig.Notifier.PlayedVideoIds.Should().Contain(Second);
        rig.Bus.Published.OfType<SongPlaybackFailedEvent>()
            .Should()
            .ContainSingle("one failure episode reaches chat once")
            .Which.TrackUri.Should()
            .Be(Watch(First));
    }

    [Fact]
    public async Task A_request_handed_to_the_player_before_a_bot_restart_is_started_again_once()
    {
        // A fresh rig is the restarted bot: the player's memory is empty, the queue comes back from the
        // database with its head marked as already handed over.
        Rig rig = await BuildAsync();
        SongRequestEntry first = new(
            Watch(First),
            "First Song",
            "Some Channel",
            null,
            180_000,
            "viewer1",
            0,
            null,
            "AB12"
        );
        SongRequestEntry second = new(
            Watch(Second),
            "Second Song",
            "Some Channel",
            null,
            180_000,
            "viewer2",
            0,
            null,
            "CD34"
        );
        rig.Queues.Restore(
            Broadcaster.ToString(),
            [("viewer1", first), ("viewer2", second)],
            inFlightIndex: 0
        );

        // The restarted bot's output comes back (twice, as a reconnecting widget does): the director
        // starts the restored head through the engine, and only once.
        await rig.Registry.SendAsync(Broadcaster, new OutputAttached());
        await rig.Registry.SendAsync(Broadcaster, new OutputAttached());

        rig.Engines.YouTube!.StartedVideoIds.Should()
            .Equal([First], "the director starts the restored head exactly once");
        rig.Notifier.PlayedVideoIds.Should()
            .Equal([First], "the restored request plays, and only once");
        rig.Queues.GetInFlight(Broadcaster.ToString()).Should().BeSameAs(first);
        QueuedUris(rig).Should().Equal(Watch(First), Watch(Second));
        rig.Bus.Published.OfType<SongRequestLostAtProviderEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task The_playing_video_carries_its_length_and_artwork_to_now_playing_and_the_dashboard()
    {
        Rig rig = await TwoQueuedAsync();

        await ReportAsync(rig, First, "playing");

        TrackInfo current = (await rig.Provider.GetCurrentTrackAsync(Broadcaster))!;
        current.DurationMs.Should().Be(180_000);
        current.AlbumArtUrl.Should().Be($"https://i.ytimg.com/vi/{First}/hqdefault.jpg");
        PlaybackStateChangedEvent changed = rig
            .Bus.Published.OfType<PlaybackStateChangedEvent>()
            .Last();
        changed
            .DurationMs.Should()
            .Be(180_000, "the dashboard's progress bar is measured against it");
        changed.AlbumArtUrl.Should().Be($"https://i.ytimg.com/vi/{First}/hqdefault.jpg");
    }
}
