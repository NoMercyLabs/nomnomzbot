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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// S-YOUTUBE-NOW-PLAYING slice A: the YouTube provider hands a request to the channel's own overlay player.
/// Idle player: the video is pushed as <c>youtube.play</c>. Busy player: it waits in the one-slot "next"
/// buffer, which the queue read shows, until the player reports the current video ended. The player's own
/// reports are the now-playing truth, and only while they are fresh.
/// </summary>
public sealed class YouTubePlayerHandOverTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192b000-0000-7000-8000-0000000a7b01");
    private static readonly Guid PlayerWidget = Guid.Parse("0192b000-0000-7000-8000-0000000a7b02");

    private sealed class RecordingWidgetEventNotifier : IWidgetEventNotifier
    {
        public List<(Guid Broadcaster, Guid Widget, string EventType, object? Data)> Sent { get; } =
        [];

        public Task SendWidgetEventAsync(
            Guid broadcasterId,
            Guid widgetId,
            string eventType,
            object? data,
            CancellationToken ct = default
        )
        {
            Sent.Add((broadcasterId, widgetId, eventType, data));
            return Task.CompletedTask;
        }
    }

    private sealed record Rig(
        YouTubeMusicProvider Provider,
        YouTubePlayerReportService Reports,
        RecordingWidgetEventNotifier Notifier,
        RecordingEventBus Bus,
        FakeTimeProvider Clock
    );

    private static async Task<Rig> BuildAsync(
        bool pageAttached = true,
        string? apiKey = null,
        RecordingHttpHandler? handler = null
    )
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            new()
            {
                Id = Broadcaster,
                OwnerUserId = Guid.NewGuid(),
                TwitchChannelId = "yt1",
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
        await db.SaveChangesAsync();

        IOverlayPresenceRegistry presence = Substitute.For<IOverlayPresenceRegistry>();
        presence.IsWidgetAttached(Broadcaster, PlayerWidget).Returns(pageAttached);

        FakeTimeProvider clock = new(DateTimeOffset.Parse("2026-10-04T12:00:00Z"));
        RecordingWidgetEventNotifier notifier = new();
        RecordingEventBus bus = new();
        YouTubePlayerStateStore store = new(clock);
        YouTubePlayerDispatcher players = new(db, presence, notifier);

        YouTubeMusicProvider provider = YouTubeProviderFactory.Create(
            apiKey: apiKey,
            handler: handler,
            db: db,
            playerState: store,
            players: players
        );
        YouTubePlayerReportService reports = new(store, players, bus);
        return new(provider, reports, notifier, bus, clock);
    }

    private static string Watch(string videoId) => $"https://www.youtube.com/watch?v={videoId}";

    [Fact]
    public async Task An_idle_player_gets_the_video_pushed_as_youtube_play()
    {
        Rig rig = await BuildAsync();

        bool accepted = await rig.Provider.AddToQueueAsync(Broadcaster, Watch("dQw4w9WgXcQ"));

        accepted.Should().BeTrue();
        rig.Notifier.Sent.Should().ContainSingle();
        (Guid broadcaster, Guid widget, string eventType, object? data) = rig.Notifier.Sent[0];
        broadcaster.Should().Be(Broadcaster);
        widget.Should().Be(PlayerWidget);
        eventType.Should().Be("youtube.play");
        data.Should()
            .BeEquivalentTo(new YouTubePlayWidgetPayload("dQw4w9WgXcQ", Watch("dQw4w9WgXcQ")));
        IReadOnlyList<TrackInfo>? queue = await rig.Provider.GetQueueAsync(Broadcaster);
        queue.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task A_busy_player_buffers_the_video_as_next_and_the_queue_read_shows_it()
    {
        Rig rig = await BuildAsync();
        await rig.Provider.AddToQueueAsync(Broadcaster, Watch("dQw4w9WgXcQ"));

        bool accepted = await rig.Provider.AddToQueueAsync(Broadcaster, Watch("9bZkp7q19f0"));

        accepted.Should().BeTrue();
        rig.Notifier.Sent.Should()
            .ContainSingle("the second video waits; only the first was pushed");
        IReadOnlyList<TrackInfo>? queue = await rig.Provider.GetQueueAsync(Broadcaster);
        queue.Should().NotBeNull();
        queue.Should().ContainSingle().Which.TrackUri.Should().Be(Watch("9bZkp7q19f0"));
        queue[0].ProviderTrackId.Should().Be("9bZkp7q19f0");
        queue[0].Provider.Should().Be("youtube");
    }

    [Fact]
    public async Task An_ended_report_pushes_the_buffered_video_and_empties_the_buffer()
    {
        Rig rig = await BuildAsync();
        await rig.Provider.AddToQueueAsync(Broadcaster, Watch("dQw4w9WgXcQ"));
        await rig.Provider.AddToQueueAsync(Broadcaster, Watch("9bZkp7q19f0"));

        Result reported = await rig.Reports.ReportAsync(
            Broadcaster,
            "dQw4w9WgXcQ",
            "ended",
            213000
        );

        reported.IsSuccess.Should().BeTrue();
        rig.Notifier.Sent.Should().HaveCount(2);
        rig.Notifier.Sent[1].EventType.Should().Be("youtube.play");
        rig.Notifier.Sent[1]
            .Data.Should()
            .BeEquivalentTo(new YouTubePlayWidgetPayload("9bZkp7q19f0", Watch("9bZkp7q19f0")));
        (await rig.Provider.GetQueueAsync(Broadcaster)).Should().NotBeNull().And.BeEmpty();
        PlaybackStateChangedEvent changed = rig
            .Bus.Published.OfType<PlaybackStateChangedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        changed.BroadcasterId.Should().Be(Broadcaster);
        changed.IsPlaying.Should().BeFalse();
        changed.TrackUri.Should().Be(Watch("dQw4w9WgXcQ"));
    }

    [Fact]
    public async Task A_playing_report_is_the_current_track_and_is_published()
    {
        Rig rig = await BuildAsync();
        await rig.Provider.AddToQueueAsync(Broadcaster, Watch("dQw4w9WgXcQ"));

        Result reported = await rig.Reports.ReportAsync(
            Broadcaster,
            "dQw4w9WgXcQ",
            "playing",
            42000
        );

        reported.IsSuccess.Should().BeTrue();
        TrackInfo? current = await rig.Provider.GetCurrentTrackAsync(Broadcaster);
        current.Should().NotBeNull();
        current.TrackUri.Should().Be(Watch("dQw4w9WgXcQ"));
        current.ProviderTrackId.Should().Be("dQw4w9WgXcQ");
        current.Provider.Should().Be("youtube");
        current.IsPlaying.Should().BeTrue();
        current.ProgressMs.Should().Be(42000);
        PlaybackStateChangedEvent changed = rig
            .Bus.Published.OfType<PlaybackStateChangedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        changed.IsPlaying.Should().BeTrue();
        changed.Provider.Should().Be("youtube");
        changed.TrackUri.Should().Be(Watch("dQw4w9WgXcQ"));
        changed.ProgressMs.Should().Be(42000);
    }

    [Fact]
    public async Task A_stale_report_is_no_current_track_and_the_player_is_idle_again()
    {
        Rig rig = await BuildAsync();
        await rig.Provider.AddToQueueAsync(Broadcaster, Watch("dQw4w9WgXcQ"));
        await rig.Reports.ReportAsync(Broadcaster, "dQw4w9WgXcQ", "playing", 1000);

        rig.Clock.Advance(TimeSpan.FromSeconds(16));

        (await rig.Provider.GetCurrentTrackAsync(Broadcaster)).Should().BeNull();
        await rig.Provider.AddToQueueAsync(Broadcaster, Watch("9bZkp7q19f0"));
        rig.Notifier.Sent.Should().HaveCount(2, "a silent player is idle, so the next video plays");
    }

    [Fact]
    public async Task Without_an_attached_player_page_the_hand_over_is_refused_and_nothing_is_pushed()
    {
        Rig rig = await BuildAsync(pageAttached: false);

        bool accepted = await rig.Provider.AddToQueueAsync(Broadcaster, Watch("dQw4w9WgXcQ"));

        accepted.Should().BeFalse();
        rig.Notifier.Sent.Should().BeEmpty();
        (await rig.Provider.GetQueueAsync(Broadcaster)).Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task An_unknown_state_is_refused_and_changes_nothing()
    {
        Rig rig = await BuildAsync();

        Result reported = await rig.Reports.ReportAsync(Broadcaster, "dQw4w9WgXcQ", "rewinding", 0);

        reported.IsSuccess.Should().BeFalse();
        (await rig.Provider.GetCurrentTrackAsync(Broadcaster)).Should().BeNull();
        rig.Bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task A_playing_report_for_a_queued_video_publishes_the_resolved_title_and_channel()
    {
        RecordingHttpHandler handler = new();
        handler.RespondWhen(
            request => request.RequestUri!.AbsolutePath.EndsWith("/videos"),
            System.Net.HttpStatusCode.OK,
            """
            {"items":[{"id":"dQw4w9WgXcQ",
              "snippet":{"title":"Never Gonna Give You Up","channelTitle":"Rick Astley",
                "liveBroadcastContent":"none"},
              "contentDetails":{"duration":"PT4M13S"},
              "status":{"embeddable":true}}]}
            """
        );
        Rig rig = await BuildAsync(apiKey: "test-key", handler: handler);
        await rig.Provider.AddToQueueAsync(Broadcaster, Watch("dQw4w9WgXcQ"));

        await rig.Reports.ReportAsync(Broadcaster, "dQw4w9WgXcQ", "playing", 5000);

        PlaybackStateChangedEvent changed = rig
            .Bus.Published.OfType<PlaybackStateChangedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        changed.TrackName.Should().Be("Never Gonna Give You Up");
        changed.Artist.Should().Be("Rick Astley");
        TrackInfo? current = await rig.Provider.GetCurrentTrackAsync(Broadcaster);
        current.Should().NotBeNull();
        current.TrackName.Should().Be("Never Gonna Give You Up");
        current.Artist.Should().Be("Rick Astley");
    }
}
