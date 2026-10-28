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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;
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
/// S-YOUTUBE-NOW-PLAYING: pause, resume and skip reach the one overlay player that owns the channel's YouTube
/// audio. The real music service, YouTube provider, player state store, report service and queue reconciler run
/// together; only the widget notifier, the overlay presence and the clock are fakes.
/// </summary>
public sealed class YouTubeTransportControlTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192b000-0000-7000-8000-0000000a7d01");
    private static readonly Guid OwnerWidget = Guid.Parse("0192b000-0000-7000-8000-0000000a7d02");
    private static readonly Guid OtherWidget = Guid.Parse("0192b000-0000-7000-8000-0000000a7d03");
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

        public List<(Guid Widget, string EventType)> Of(string eventType) =>
            [.. Sent.Where(s => s.EventType == eventType).Select(s => (s.Widget, s.EventType))];

        public List<string> PlayedVideoIds =>
            [
                .. Sent.Where(s => s.EventType == "youtube.play")
                    .Select(s => ((YouTubePlayWidgetPayload)s.Data!).VideoId),
            ];
    }

    private sealed record Rig(
        MusicService Music,
        YouTubePlayerReportService Reports,
        SongRequestQueueReconciler Reconciler,
        PlayOnceResumeHandler ResumeHandler,
        PlayOnceResumeTracker ResumeTracker,
        YouTubePlayerDispatcher Players,
        SongRequestQueueStore Queues,
        YouTubePlayerStateStore PlayerStore,
        RecordingWidgetEventNotifier Notifier,
        RecordingEventBus Bus,
        IOverlayPresenceRegistry Presence,
        RealYouTubeEngineFactory Engines,
        PlaybackDirectorRegistry Registry
    );

    private static string VideoJson(string id, string title) =>
        $$$"""
            {"items":[{"id":"{{{id}}}",
              "snippet":{"title":"{{{title}}}","channelTitle":"Some Channel","liveBroadcastContent":"none"},
              "contentDetails":{"duration":"PT3M"},
              "status":{"embeddable":true}}]}
            """;

    private static async Task<Rig> BuildAsync(bool playerOpen = true, bool youTubeConnected = true)
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        MusicTestDbContext musicDb = MusicTestDbContext.New();
        db.Channels.Add(
            new()
            {
                Id = Broadcaster,
                OwnerUserId = Guid.NewGuid(),
                TwitchChannelId = "yt3",
                Name = "TubeStreamer",
                NameNormalized = "tubestreamer",
            }
        );
        DateTime created = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        foreach ((Guid id, int hour) in new[] { (OwnerWidget, 0), (OtherWidget, 1) })
            db.Widgets.Add(
                new Widget
                {
                    Id = id,
                    BroadcasterId = Broadcaster,
                    Name = "now playing",
                    IsEnabled = true,
                    EventSubscriptions = ["now_playing"],
                    CreatedAt = created.AddHours(hour),
                }
            );
        if (youTubeConnected)
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
        presence.IsWidgetAttached(Broadcaster, OwnerWidget).Returns(playerOpen);
        presence.IsWidgetAttached(Broadcaster, OtherWidget).Returns(playerOpen);
        presence.GetMusicWidgetId(Broadcaster).Returns(playerOpen ? OwnerWidget : null);

        FakeTimeProvider clock = new(DateTimeOffset.Parse("2026-10-04T12:00:00Z"));
        RecordingWidgetEventNotifier notifier = new();
        RecordingEventBus bus = new();
        YouTubePlayerStateStore playerStore = new(clock);
        YouTubePlayerDispatcher players = new(db, presence, notifier, playerStore);
        LateSink lateSink = new();

        RecordingHttpHandler handler = new();
        foreach (
            (string id, string title) in new[]
            {
                (First, "First Song"),
                (Second, "Second Song"),
                (Third, "Third Song"),
            }
        )
            handler.RespondWhen(
                r =>
                    r.RequestUri!.AbsolutePath.EndsWith("/videos")
                    && r.RequestUri.Query.Contains(id),
                System.Net.HttpStatusCode.OK,
                VideoJson(id, title)
            );
        YouTubeMusicProvider provider = YouTubeProviderFactory.Create(
            apiKey: "test-key",
            handler: handler,
            db: db,
            playerState: playerStore,
            players: players,
            playback: lateSink
        );

        IUserIdentityService identities = Substitute.For<IUserIdentityService>();
        identities
            .ResolveUserAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(Guid.NewGuid()));
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
            identities,
            Substitute.For<IForeignLinkTitleLookup>(),
            registry,
            registry
        );
        PlayOnceResumeTracker resumeTracker = new();
        YouTubePlayerReportService reports = new(
            playerStore,
            players,
            bus,
            registry,
            resumeTracker,
            NullLogger<YouTubePlayerReportService>.Instance
        );
        SongRequestQueueReconciler reconciler = new(
            queues,
            new NoOpSongRequestQueuePersistence(),
            bus,
            registry
        );
        PlayOnceResumeHandler resumeHandler = new(
            resumeTracker,
            music,
            registry,
            NullLogger<PlayOnceResumeHandler>.Instance
        );
        return new(
            music,
            reports,
            reconciler,
            resumeHandler,
            resumeTracker,
            players,
            queues,
            playerStore,
            notifier,
            bus,
            presence,
            engines,
            registry
        );
    }

    private static string Watch(string videoId) => $"https://www.youtube.com/watch?v={videoId}";

    private static async Task ReportAsync(
        Rig rig,
        string videoId,
        string state,
        long positionMs = 1000
    )
    {
        int seen = rig.Bus.Published.Count;
        Result reported = await rig.Reports.ReportAsync(
            Broadcaster,
            OwnerWidget,
            videoId,
            state,
            positionMs
        );
        reported.IsSuccess.Should().BeTrue();
        await DeliverAsync(rig, seen);
    }

    private static async Task DeliverAsync(Rig rig, int seen)
    {
        List<PlaybackStateChangedEvent> changes = rig
            .Bus.Published.Skip(seen)
            .OfType<PlaybackStateChangedEvent>()
            .ToList();
        foreach (PlaybackStateChangedEvent changed in changes)
        {
            await rig.Reconciler.HandleAsync(changed);
            await rig.ResumeHandler.HandleAsync(changed);
        }
    }

    private static async Task<Result> PlayOnceAsync(Rig rig, string videoId)
    {
        NowPlaying? prior = await rig.Music.GetNowPlayingAsync(Broadcaster.ToString());
        rig.ResumeTracker.Remember(
            Broadcaster,
            new PlayOnceResumeState(
                Watch(videoId),
                prior?.TrackUri,
                prior?.ProgressMs ?? 0,
                prior?.IsPlaying ?? false
            )
        );
        int seen = rig.Bus.Published.Count;
        Result once = await rig.Music.PlayTrackOnceAsync(Broadcaster.ToString(), Watch(videoId));
        await DeliverAsync(rig, seen);
        return once;
    }

    private static List<string> QueuedUris(Rig rig) =>
        rig
            .Queues.TryGet(Broadcaster.ToString())!
            .GetSnapshot()
            .Select(e => e.Item.TrackUri)
            .ToList();

    private static async Task<Rig> PlayingFirstWithSecondQueuedAsync()
    {
        Rig rig = await BuildAsync();
        (await rig.Music.AddToQueueAsync(Broadcaster.ToString(), Watch(First), "viewer1"))
            .IsSuccess.Should()
            .BeTrue();
        (await rig.Music.AddToQueueAsync(Broadcaster.ToString(), Watch(Second), "viewer2"))
            .IsSuccess.Should()
            .BeTrue();
        await ReportAsync(rig, First, "playing");
        return rig;
    }

    [Fact]
    public async Task A_request_with_no_open_player_is_refused_with_its_own_reason_and_queues_nothing()
    {
        Rig rig = await BuildAsync(playerOpen: false);
        Result queued = await rig.Music.AddToQueueAsync(
            Broadcaster.ToString(),
            Watch(First),
            "viewer1"
        );

        queued.IsSuccess.Should().BeFalse();
        queued.ErrorCode.Should().Be("NO_OVERLAY_PLAYER");
        QueuedUris(rig).Should().BeEmpty();
        rig.Notifier.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task A_request_queued_while_the_player_was_open_is_not_dropped_by_the_ended_report_adapter()
    {
        Rig rig = await BuildAsync();
        (await rig.Music.AddToQueueAsync(Broadcaster.ToString(), Watch(First), "viewer1"))
            .IsSuccess.Should()
            .BeTrue();
        (await rig.Music.AddToQueueAsync(Broadcaster.ToString(), Watch(Second), "viewer2"))
            .IsSuccess.Should()
            .BeTrue();
        rig.Presence.GetMusicWidgetId(Broadcaster).Returns((Guid?)null);
        await ReportAsync(rig, First, "ended");
        rig.Notifier.PlayedVideoIds.Should().Equal(First);

        rig.Presence.GetMusicWidgetId(Broadcaster).Returns(OwnerWidget);
        // Queue ownership (S-PD-3): the ended report is only forwarded; the director (S-PD-4) drops
        // the head, so the adapter leaves both rows alone. The legacy hand-over that re-pushed the head
        // when audio attached is gone (S-PD-4c2b B2); the director is the only way a head starts.
        QueuedUris(rig).Should().Equal(Watch(First), Watch(Second));

        rig.Notifier.PlayedVideoIds.Should()
            .Equal([First], "the adapter pushed nothing more to the player");
    }

    /// <summary>music-sr.md AUTH STANCE: YouTube search and the overlay queue ride the app-level Data API
    /// key, never a per-channel YouTube login. A streamer who set the key but never connected YouTube (and has
    /// no Spotify) must still get a working song request that plays in the Audio widget.</summary>
    [Fact]
    public async Task A_request_plays_in_the_overlay_on_the_api_key_alone_without_a_YouTube_connection()
    {
        Rig rig = await BuildAsync(youTubeConnected: false);

        Result queued = await rig.Music.AddToQueueAsync(
            Broadcaster.ToString(),
            Watch(First),
            "viewer1"
        );

        queued.IsSuccess.Should().BeTrue(queued.ErrorMessage);
        rig.Notifier.PlayedVideoIds.Should().Equal(First);
        rig.Notifier.Sent.Should().ContainSingle().Which.Widget.Should().Be(OwnerWidget);
    }

    /// <summary>The overlay player can pause, resume, skip forward and seek — and nothing else. The now-playing
    /// state the dashboard renders must say so, or it shows previous/shuffle/repeat buttons that do nothing.</summary>
    [Fact]
    public async Task Now_playing_offers_only_the_controls_the_overlay_player_can_do()
    {
        Rig rig = await PlayingFirstWithSecondQueuedAsync();

        NowPlaying? playing = await rig.Music.GetNowPlayingAsync(Broadcaster.ToString());

        playing.Should().NotBeNull();
        playing.TrackUri.Should().Be(Watch(First));
        playing.CanSkipPrevious.Should().BeFalse();
        playing.CanSetShuffle.Should().BeFalse();
        playing.CanSetRepeat.Should().BeFalse();
        playing.CanSkipNext.Should().BeTrue();
        playing.CanPause.Should().BeTrue();
        playing.CanResume.Should().BeTrue();
        playing.CanSeek.Should().BeTrue();
    }

    [Fact]
    public async Task Pause_sends_youtube_pause_to_the_owner_only_and_succeeds()
    {
        Rig rig = await PlayingFirstWithSecondQueuedAsync();

        Result paused = await rig.Music.PauseAsync(Broadcaster.ToString());

        paused.IsSuccess.Should().BeTrue(paused.ErrorMessage);
        rig.Notifier.Of("youtube.pause").Should().Equal([(OwnerWidget, "youtube.pause")]);
        rig.Notifier.Of("youtube.resume").Should().BeEmpty();
        rig.Notifier.PlayedVideoIds.Should().Equal(First);
    }

    [Fact]
    public async Task Play_sends_youtube_resume_to_the_owner_only_and_succeeds()
    {
        Rig rig = await PlayingFirstWithSecondQueuedAsync();
        await ReportAsync(rig, First, "paused");

        Result resumed = await rig.Music.PlayAsync(Broadcaster.ToString());

        resumed.IsSuccess.Should().BeTrue(resumed.ErrorMessage);
        rig.Notifier.Of("youtube.resume").Should().Equal([(OwnerWidget, "youtube.resume")]);
        rig.Notifier.Of("youtube.pause").Should().BeEmpty();
    }

    [Fact]
    public async Task Pause_with_no_open_player_sends_nothing_and_says_nothing_is_playing()
    {
        Rig rig = await BuildAsync(playerOpen: false);

        Result paused = await rig.Music.PauseAsync(Broadcaster.ToString());

        paused.IsSuccess.Should().BeFalse();
        paused.ErrorCode.Should().Be("NO_ACTIVE_DEVICE");
        paused.ErrorData.Should().Be(new NoActivePlayerRefusal("youtube"));
        rig.Notifier.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Skip_tells_the_director_once_and_pushes_no_video_itself()
    {
        Rig rig = await PlayingFirstWithSecondQueuedAsync();
        rig.Notifier.PlayedVideoIds.Should().Equal(First);

        Result skipped = await rig.Music.SkipAsync(Broadcaster.ToString(), "viewer3");

        skipped.IsSuccess.Should().BeTrue(skipped.ErrorMessage);
        rig.Engines.YouTube!.StartedVideoIds.Should()
            .Equal(
                [First],
                "the director waits for the player's paused report before the next start"
            );

        await ReportAsync(rig, First, "paused");

        rig.Engines.YouTube!.StartedVideoIds.Should()
            .Equal([First, Second], "the director starts the next request exactly once");
        rig.Notifier.PlayedVideoIds.Should()
            .Equal([First, Second], "the director starts the next request, not the provider");
        rig.Notifier.Of("youtube.stop")
            .Should()
            .Equal([(OwnerWidget, "youtube.stop")], "the director stops the skipped video once");
    }

    [Fact]
    public async Task Skip_with_nothing_next_still_sends_the_skip_to_the_director_which_owns_the_stop()
    {
        Rig rig = await BuildAsync();
        (await rig.Music.AddToQueueAsync(Broadcaster.ToString(), Watch(First), "viewer1"))
            .IsSuccess.Should()
            .BeTrue();
        await ReportAsync(rig, First, "playing");
        rig.PlayerStore.GetFresh(Broadcaster)!.State.Should().Be(YouTubePlayerState.Playing);

        Result skipped = await rig.Music.SkipAsync(Broadcaster.ToString(), "viewer3");

        skipped.IsSuccess.Should().BeTrue(skipped.ErrorMessage);
        rig.Engines.YouTube!.StartedVideoIds.Should()
            .Equal([First], "nothing waits, so the skip starts no video");
        rig.Notifier.PlayedVideoIds.Should().Equal(First);
        rig.Registry.StateOf(Broadcaster)
            .Kind.Should()
            .Be(PlaybackStateKind.Idle, "the skip emptied the director");

        await ReportAsync(rig, First, "ended");

        rig.PlayerStore.GetFresh(Broadcaster)!.State.Should().Be(YouTubePlayerState.Ended);
        rig.PlayerStore.IsBusy(Broadcaster).Should().BeFalse();
        QueuedUris(rig).Should().BeEmpty();
    }

    /// <summary>With the Audio widget closed and a request waiting, a skip has nothing to skip: the director
    /// keeps the waiting request and nothing plays. Chat must hear the player isn't open — not "Skipped to
    /// the next track." — and the waiting requests stay queued untouched.</summary>
    [Fact]
    public async Task Skip_with_a_request_waiting_but_no_open_player_fails_as_no_player_and_keeps_the_queue()
    {
        Rig rig = await PlayingFirstWithSecondQueuedAsync();
        rig.Presence.GetMusicWidgetId(Broadcaster).Returns((Guid?)null);
        rig.Notifier.Sent.Clear();

        Result skipped = await rig.Music.SkipAsync(Broadcaster.ToString(), "viewer3");

        skipped.IsSuccess.Should().BeFalse();
        skipped.ErrorCode.Should().Be("NO_ACTIVE_DEVICE");
        skipped.ErrorMessage.Should().NotContainEquivalentOf("queue");
        skipped.ErrorData.Should().BeEquivalentTo(new NoActivePlayerRefusal("youtube"));
        rig.Registry.StateOf(Broadcaster).Kind.Should().Be(PlaybackStateKind.Blocked);
        rig.Notifier.Sent.Should().BeEmpty();
        QueuedUris(rig).Should().Equal([Watch(Second)], "the playing First left the queue");
    }

    [Fact]
    public async Task Skip_with_nothing_next_and_no_open_player_sends_nothing()
    {
        Rig rig = await BuildAsync(playerOpen: false);

        await rig.Music.SkipAsync(Broadcaster.ToString(), "viewer3");

        rig.Notifier.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Play_once_on_a_busy_player_plays_now_and_the_ended_once_video_pushes_no_request()
    {
        Rig rig = await PlayingFirstWithSecondQueuedAsync();
        rig.PlayerStore.PeekNext(Broadcaster)
            .Should()
            .BeNull("the waiting request is not pushed to the player ahead of time");

        Result once = await rig.Music.PlayTrackOnceAsync(Broadcaster.ToString(), Watch(Third));

        once.IsSuccess.Should().BeTrue(once.ErrorMessage);
        rig.Notifier.PlayedVideoIds.Should().Equal(First, Third);
        rig.Notifier.Sent.Last().Widget.Should().Be(OwnerWidget);
        rig.PlayerStore.PeekNext(Broadcaster).Should().BeNull();
        QueuedUris(rig).Should().Equal([Watch(Second)]);

        await ReportAsync(rig, Third, "playing");
        await ReportAsync(rig, Third, "ended");

        // The once video is no request, so its end removes nothing from the queue. With no resume pending the
        // director has nothing playing and a request waits, so it starts that request, exactly once.
        rig.Engines.YouTube!.StartedVideoIds.Should()
            .Equal(
                [First, Second],
                "the waiting request starts once, the once video starts nothing"
            );
        rig.Notifier.PlayedVideoIds.Should()
            .Equal(
                [First, Third, Second],
                "the once video ending hands the player to the waiting request"
            );
        QueuedUris(rig)
            .Should()
            .Equal([Watch(Second)], "the started request leaves the queue only when it plays");
    }

    [Fact]
    public async Task Seek_sends_one_youtube_seek_with_the_position_to_the_owner_only()
    {
        Rig rig = await PlayingFirstWithSecondQueuedAsync();

        Result sought = await rig.Music.SeekAsync(Broadcaster.ToString(), 42_000);

        sought.IsSuccess.Should().BeTrue(sought.ErrorMessage);
        rig.Notifier.Of("youtube.seek").Should().Equal([(OwnerWidget, "youtube.seek")]);
        rig.Notifier.Sent.Single(s => s.EventType == "youtube.seek")
            .Data.Should()
            .Be(new YouTubeSeekWidgetPayload(42_000));
    }

    [Fact]
    public async Task Volume_sends_one_youtube_volume_with_the_percent_to_the_owner_only()
    {
        Rig rig = await PlayingFirstWithSecondQueuedAsync();

        Result changed = await rig.Music.SetVolumeAsync(Broadcaster.ToString(), 70);

        changed.IsSuccess.Should().BeTrue(changed.ErrorMessage);
        rig.Notifier.Of("youtube.volume").Should().Equal([(OwnerWidget, "youtube.volume")]);
        rig.Notifier.Sent.Single(s => s.EventType == "youtube.volume")
            .Data.Should()
            .Be(new YouTubeVolumeWidgetPayload(70));
    }

    [Fact]
    public async Task Volume_with_no_player_open_says_nothing_is_playing_and_sends_nothing()
    {
        Rig rig = await BuildAsync(playerOpen: false);

        Result changed = await rig.Music.SetVolumeAsync(Broadcaster.ToString(), 70);

        changed.IsSuccess.Should().BeFalse();
        changed.ErrorCode.Should().Be("NO_ACTIVE_DEVICE");
        rig.Notifier.Of("youtube.volume").Should().BeEmpty();
    }

    [Fact]
    public async Task Play_once_names_the_once_video_and_resumes_nothing_while_it_plays()
    {
        Rig rig = await PlayingFirstWithSecondQueuedAsync();
        await ReportAsync(rig, First, "playing", 42_000);

        Result once = await PlayOnceAsync(rig, Third);

        once.IsSuccess.Should().BeTrue(once.ErrorMessage);
        rig.Bus.Published.OfType<PlaybackStateChangedEvent>()
            .Last()
            .TrackUri.Should()
            .Be(Watch(Third), "the publish after play once names the pushed once video");
        rig.Notifier.PlayedVideoIds.Should()
            .Equal([First, Third], "the interrupted video is not replayed over it");
        rig.Notifier.Of("youtube.seek").Should().BeEmpty();
        rig.ResumeTracker.TryPeek(Broadcaster, out PlayOnceResumeState pending).Should().BeTrue();
        pending.PriorTrackUri.Should().Be(Watch(First));
        pending.PriorProgressMs.Should().Be(42_000);
    }

    [Fact]
    public async Task Play_once_video_ending_resumes_the_interrupted_one_at_its_position_then_the_waiting_request_plays()
    {
        Rig rig = await PlayingFirstWithSecondQueuedAsync();
        await ReportAsync(rig, First, "playing", 42_000);
        (await PlayOnceAsync(rig, Third)).IsSuccess.Should().BeTrue();
        await ReportAsync(rig, Third, "playing", 500);

        await ReportAsync(rig, Third, "ended", 180_000);

        // The resume handler restarts the interrupted video; the director must not also start the waiting request.
        rig.Engines.YouTube!.StartedVideoIds.Should()
            .Equal(
                [First],
                "the director starts no video when the once video ends with a resume pending"
            );
        rig.Notifier.PlayedVideoIds.Should()
            .Equal(
                [First, Third, First],
                "the interrupted video comes back before the waiting request"
            );
        rig.Notifier.Of("youtube.seek").Should().Equal([(OwnerWidget, "youtube.seek")]);
        rig.Notifier.Sent.Single(s => s.EventType == "youtube.seek")
            .Data.Should()
            .Be(new YouTubeSeekWidgetPayload(42_000));
        rig.ResumeTracker.TryPeek(Broadcaster, out _).Should().BeFalse();
        rig.PlayerStore.PeekNext(Broadcaster)
            .Should()
            .BeNull("the waiting request stays in the fair queue until the director starts it");
        QueuedUris(rig).Should().Equal([Watch(Second)]);

        await ReportAsync(rig, First, "playing", 43_000);
        await ReportAsync(rig, First, "ended", 200_000);

        // The resumed video is no request: when it ends the director, not this path, starts the waiting one.
        rig.Engines.YouTube!.StartedVideoIds.Should().Equal([First, Second]);
        rig.Notifier.PlayedVideoIds.Should().Equal([First, Third, First, Second]);
        QueuedUris(rig).Should().Equal([Watch(Second)]);
    }

    [Fact]
    public async Task Play_once_video_ending_with_a_failed_resume_still_plays_the_waiting_request()
    {
        Rig rig = await PlayingFirstWithSecondQueuedAsync();
        await ReportAsync(rig, First, "playing", 42_000);
        (await PlayOnceAsync(rig, Third)).IsSuccess.Should().BeTrue();
        await ReportAsync(rig, Third, "playing", 500);
        IMusicService failing = Substitute.For<IMusicService>();
        failing
            .PlayTrackOnceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure("The resume failed.", "PROVIDER_ERROR"));
        IPlaybackInputSink director = Substitute.For<IPlaybackInputSink>();
        PlayOnceResumeHandler failingHandler = new(
            rig.ResumeTracker,
            failing,
            director,
            NullLogger<PlayOnceResumeHandler>.Instance
        );
        int seen = rig.Bus.Published.Count;
        (await rig.Reports.ReportAsync(Broadcaster, OwnerWidget, Third, "ended", 180_000))
            .IsSuccess.Should()
            .BeTrue();
        rig.Notifier.PlayedVideoIds.Should().Equal([First, Third], "the resume is still pending");

        foreach (
            PlaybackStateChangedEvent changed in rig
                .Bus.Published.Skip(seen)
                .OfType<PlaybackStateChangedEvent>()
                .ToList()
        )
            await failingHandler.HandleAsync(changed);

        // The handler pushes nothing itself: it tells the director a request waits, so it is not stranded.
        rig.Notifier.PlayedVideoIds.Should().Equal([First, Third]);
        await director
            .Received(1)
            .SendAsync(Broadcaster, Arg.Any<RequestAdded>(), Arg.Any<CancellationToken>());
        rig.Notifier.Of("youtube.seek").Should().BeEmpty();
    }
}
