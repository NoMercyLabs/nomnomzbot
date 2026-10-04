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
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// S-YOUTUBE-NOW-PLAYING: a YouTube request that ENDED leaves the song request queue and the next one plays.
/// The real music service, YouTube provider, player state store, report service and queue reconciler run
/// together; only the widget notifier, the overlay presence and the clock are fakes. The reconciler is fed
/// the events the report service publishes, as the event bus would deliver them.
/// </summary>
public sealed class YouTubeEndedAdvancesQueueTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192b000-0000-7000-8000-0000000a7c01");
    private static readonly Guid PlayerWidget = Guid.Parse("0192b000-0000-7000-8000-0000000a7c02");
    private const string First = "dQw4w9WgXcQ";
    private const string Second = "9bZkp7q19f0";

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
        SongRequestQueueReconciler Reconciler,
        SongRequestQueueStore Queues,
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

    private static async Task<Rig> BuildAsync()
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

        FakeTimeProvider clock = new(DateTimeOffset.Parse("2026-10-04T12:00:00Z"));
        RecordingWidgetEventNotifier notifier = new();
        RecordingEventBus bus = new();
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
        YouTubeMusicProvider provider = YouTubeProviderFactory.Create(
            apiKey: "test-key",
            handler: handler,
            db: db,
            playerState: playerStore,
            players: players
        );

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
            Substitute.For<IUserIdentityService>()
        );
        YouTubePlayerReportService reports = new(
            playerStore,
            players,
            bus,
            new PlayOnceResumeTracker()
        );
        SongRequestQueueReconciler reconciler = new(
            queues,
            music,
            new NoOpSongRequestQueuePersistence(),
            bus
        );
        return new(music, provider, reports, reconciler, queues, notifier, bus);
    }

    private static string Watch(string videoId) => $"https://www.youtube.com/watch?v={videoId}";

    private static async Task ReportAsync(Rig rig, string videoId, string state)
    {
        int seen = rig.Bus.Published.Count;
        Result reported = await rig.Reports.ReportAsync(
            Broadcaster,
            PlayerWidget,
            videoId,
            state,
            1000
        );
        reported.IsSuccess.Should().BeTrue();
        List<PlaybackStateChangedEvent> changes = rig
            .Bus.Published.Skip(seen)
            .OfType<PlaybackStateChangedEvent>()
            .ToList();
        foreach (PlaybackStateChangedEvent changed in changes)
            await rig.Reconciler.HandleAsync(changed);
    }

    private static List<string> QueuedUris(Rig rig) =>
        rig
            .Queues.TryGet(Broadcaster.ToString())!
            .GetSnapshot()
            .Select(e => e.Item.TrackUri)
            .ToList();

    private static async Task<Rig> TwoQueuedAsync()
    {
        Rig rig = await BuildAsync();
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
        rig.Queues.GetInFlight(Broadcaster.ToString())!.TrackUri.Should().Be(Watch(Second));
        rig.Notifier.PlayedVideoIds.Should()
            .Equal([First], "the player is busy, so the second waits");
        (await rig.Provider.GetQueueAsync(Broadcaster))!
            .Select(t => t.TrackUri)
            .Should()
            .Equal(Watch(Second));
    }

    [Fact]
    public async Task An_ended_report_plays_the_next_request_exactly_once_and_it_then_leaves_the_queue()
    {
        Rig rig = await TwoQueuedAsync();
        await ReportAsync(rig, First, "playing");

        await ReportAsync(rig, First, "ended");

        rig.Notifier.PlayedVideoIds.Should().Equal(First, Second);
        QueuedUris(rig)
            .Should()
            .Equal([Watch(Second)], "the second is handed over, not yet playing");
        rig.Queues.GetInFlight(Broadcaster.ToString())!.TrackUri.Should().Be(Watch(Second));

        await ReportAsync(rig, Second, "playing");

        QueuedUris(rig).Should().BeEmpty();
        rig.Queues.GetInFlight(Broadcaster.ToString()).Should().BeNull();
        (await rig.Provider.GetCurrentTrackAsync(Broadcaster))!.TrackUri.Should().Be(Watch(Second));

        await ReportAsync(rig, Second, "ended");

        rig.Notifier.PlayedVideoIds.Should().Equal(First, Second);
        QueuedUris(rig).Should().BeEmpty();
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
}
