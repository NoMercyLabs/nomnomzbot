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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

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
        RecordingEventBus Bus
    );

    private static string VideoJson(string id, string title) =>
        $$$"""
            {"items":[{"id":"{{{id}}}",
              "snippet":{"title":"{{{title}}}","channelTitle":"Some Channel","liveBroadcastContent":"none"},
              "contentDetails":{"duration":"PT3M"},
              "status":{"embeddable":true}}]}
            """;

    private static async Task<Rig> BuildAsync(bool playerOpen = true)
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

        FakeTimeProvider clock = new(DateTimeOffset.Parse("2026-10-04T12:00:00Z"));
        RecordingWidgetEventNotifier notifier = new();
        RecordingEventBus bus = new();
        YouTubePlayerStateStore playerStore = new(clock);
        YouTubePlayerDispatcher players = new(db, presence, notifier, playerStore);

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
            players: players
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
            Substitute.For<IForeignLinkTitleLookup>()
        );
        PlayOnceResumeTracker resumeTracker = new();
        YouTubePlayerReportService reports = new(playerStore, players, bus, resumeTracker);
        SongRequestQueueReconciler reconciler = new(
            queues,
            music,
            new NoOpSongRequestQueuePersistence(),
            bus
        );
        PlayOnceResumeHandler resumeHandler = new(
            resumeTracker,
            music,
            players,
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
            bus
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
        rig.Notifier.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Skip_plays_the_second_request_on_the_owner_exactly_once_and_the_first_leaves_the_queue()
    {
        Rig rig = await PlayingFirstWithSecondQueuedAsync();
        rig.Notifier.PlayedVideoIds.Should().Equal(First);

        Result skipped = await rig.Music.SkipAsync(Broadcaster.ToString(), "viewer3");

        skipped.IsSuccess.Should().BeTrue(skipped.ErrorMessage);
        rig.Notifier.PlayedVideoIds.Should().Equal(First, Second);
        rig.Notifier.Sent.Last().Widget.Should().Be(OwnerWidget);
        rig.Notifier.Sent.Last().EventType.Should().Be("youtube.play");
        QueuedUris(rig).Should().Equal([Watch(Second)], "the skipped first request is gone");

        await ReportAsync(rig, Second, "playing");

        QueuedUris(rig).Should().BeEmpty();
        rig.Notifier.PlayedVideoIds.Should().Equal([First, Second], "the hand-over ran once");
    }

    [Fact]
    public async Task Skip_with_nothing_next_stops_the_owner_and_the_player_ends_like_an_ended_video()
    {
        Rig rig = await BuildAsync();
        (await rig.Music.AddToQueueAsync(Broadcaster.ToString(), Watch(First), "viewer1"))
            .IsSuccess.Should()
            .BeTrue();
        await ReportAsync(rig, First, "playing");
        rig.PlayerStore.GetFresh(Broadcaster)!.State.Should().Be(YouTubePlayerState.Playing);

        Result skipped = await rig.Music.SkipAsync(Broadcaster.ToString(), "viewer3");

        skipped.IsSuccess.Should().BeTrue(skipped.ErrorMessage);
        rig.Notifier.Of("youtube.stop").Should().Equal([(OwnerWidget, "youtube.stop")]);
        rig.Notifier.PlayedVideoIds.Should().Equal(First);

        await ReportAsync(rig, First, "ended");

        rig.PlayerStore.GetFresh(Broadcaster)!.State.Should().Be(YouTubePlayerState.Ended);
        rig.PlayerStore.IsBusy(Broadcaster).Should().BeFalse();
        QueuedUris(rig).Should().BeEmpty();
    }

    [Fact]
    public async Task Skip_with_nothing_next_and_no_open_player_sends_nothing()
    {
        Rig rig = await BuildAsync(playerOpen: false);

        await rig.Music.SkipAsync(Broadcaster.ToString(), "viewer3");

        rig.Notifier.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Play_once_on_a_busy_player_plays_now_and_the_waiting_request_still_plays_after_it()
    {
        Rig rig = await PlayingFirstWithSecondQueuedAsync();
        rig.PlayerStore.PeekNext(Broadcaster)!.VideoId.Should().Be(Second);

        Result once = await rig.Music.PlayTrackOnceAsync(Broadcaster.ToString(), Watch(Third));

        once.IsSuccess.Should().BeTrue(once.ErrorMessage);
        rig.Notifier.PlayedVideoIds.Should().Equal(First, Third);
        rig.Notifier.Sent.Last().Widget.Should().Be(OwnerWidget);
        rig.PlayerStore.PeekNext(Broadcaster)!.VideoId.Should().Be(Second);
        QueuedUris(rig).Should().Equal([Watch(Second)]);

        await ReportAsync(rig, Third, "playing");
        await ReportAsync(rig, Third, "ended");

        rig.Notifier.PlayedVideoIds.Should().Equal([First, Third, Second]);
        await ReportAsync(rig, Second, "playing");
        QueuedUris(rig).Should().BeEmpty();
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
        rig.PlayerStore.PeekNext(Broadcaster)!.VideoId.Should().Be(Second);

        await ReportAsync(rig, First, "playing", 43_000);
        await ReportAsync(rig, First, "ended", 200_000);

        rig.Notifier.PlayedVideoIds.Should()
            .Equal([First, Third, First, Second], "every request played once, none lost");
        await ReportAsync(rig, Second, "playing");
        QueuedUris(rig).Should().BeEmpty();
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
        PlayOnceResumeHandler failingHandler = new(
            rig.ResumeTracker,
            failing,
            rig.Players,
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

        rig.Notifier.PlayedVideoIds.Should()
            .Equal([First, Third, Second], "the waiting request is handed over, not stranded");
        rig.Notifier.Of("youtube.seek").Should().BeEmpty();
        rig.PlayerStore.PeekNext(Broadcaster).Should().BeNull();
    }
}
