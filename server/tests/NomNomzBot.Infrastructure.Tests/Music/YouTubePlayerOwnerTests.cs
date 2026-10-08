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
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// The Audio page owns YouTube playback even when display widgets are attached.
/// </summary>
public sealed class YouTubePlayerOwnerTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192b000-0000-7000-8000-0000000a7d01");

    // The earlier widget has the HIGHER id, so "earliest created" and "lowest id" pick different widgets.
    private static readonly Guid Early = Guid.Parse("0192b000-0000-7000-8000-0000000a7d09");
    private static readonly Guid Late = Guid.Parse("0192b000-0000-7000-8000-0000000a7d02");
    private static readonly Guid Audio = Guid.Parse("0192b000-0000-7000-8000-0000000a7d03");

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
    }

    private sealed class Rig
    {
        public required YouTubeMusicProvider Provider { get; init; }
        public required YouTubePlayerReportService Reports { get; init; }
        public required YouTubePlayerStateStore Store { get; init; }
        public required YouTubePlayerDispatcher Players { get; init; }
        public required RecordingWidgetEventNotifier Notifier { get; init; }
        public required RecordingEventBus Bus { get; init; }
        public required FakeTimeProvider Clock { get; init; }
        public required HashSet<Guid> Attached { get; init; }
    }

    private static Widget Player(Guid id, DateTime createdAt) =>
        new()
        {
            Id = id,
            BroadcasterId = Broadcaster,
            Name = "now playing",
            IsEnabled = true,
            EventSubscriptions = ["now_playing"],
            CreatedAt = createdAt,
        };

    private static async Task<Rig> BuildAsync()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
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
        db.Widgets.Add(Player(Early, new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc)));
        db.Widgets.Add(Player(Late, new DateTime(2026, 10, 2, 10, 0, 0, DateTimeKind.Utc)));
        db.Widgets.Add(
            new Widget
            {
                Id = Audio,
                BroadcasterId = Broadcaster,
                Name = "Audio",
                IsEnabled = true,
                EventSubscriptions = ["tts_speak"],
            }
        );
        await db.SaveChangesAsync();

        HashSet<Guid> attached = [Early, Late, Audio];
        IOverlayPresenceRegistry presence = Substitute.For<IOverlayPresenceRegistry>();
        presence
            .IsWidgetAttached(Broadcaster, Arg.Any<Guid>())
            .Returns(call => attached.Contains(call.ArgAt<Guid>(1)));
        presence
            .GetAudioWidgetId(Broadcaster)
            .Returns(_ => attached.Contains(Audio) ? Audio : null);

        FakeTimeProvider clock = new(DateTimeOffset.Parse("2026-10-04T12:00:00Z"));
        RecordingWidgetEventNotifier notifier = new();
        RecordingEventBus bus = new();
        YouTubePlayerStateStore store = new(clock);
        YouTubePlayerDispatcher players = new(db, presence, notifier, store);
        Rig rig = new()
        {
            Provider = YouTubeProviderFactory.Create(db: db, playerState: store, players: players),
            Reports = new YouTubePlayerReportService(
                store,
                players,
                bus,
                new PlayOnceResumeTracker()
            ),
            Store = store,
            Players = players,
            Notifier = notifier,
            Bus = bus,
            Clock = clock,
            Attached = attached,
        };
        return rig;
    }

    private static string Watch(string videoId) => $"https://www.youtube.com/watch?v={videoId}";

    private static Task<Result> ReportAsync(Rig rig, Guid widget, string videoId, string state) =>
        rig.Reports.ReportAsync(Broadcaster, widget, videoId, state, 1000);

    private static string VideoOf(object? data) => ((YouTubePlayWidgetPayload)data!).VideoId;

    [Fact]
    public async Task A_new_audio_page_resumes_the_current_video_at_its_reported_position()
    {
        Rig rig = await BuildAsync();
        rig.Store.Report(Broadcaster, Audio, First, YouTubePlayerState.Playing, 42_000);
        rig.Store.SetNext(Broadcaster, new YouTubeQueuedTrack(Second, "Second", "Artist"));

        (await rig.Players.ReplayCurrentAsync(Broadcaster, allowStale: false)).Should().BeTrue();

        rig.Notifier.Sent.Select(s => s.EventType).Should().Equal("youtube.play", "youtube.seek");
        rig.Notifier.Sent.Should().OnlyContain(s => s.Widget == Audio);
        VideoOf(rig.Notifier.Sent[0].Data).Should().Be(First);
        rig.Notifier.Sent[1].Data.Should().BeEquivalentTo(new YouTubeSeekWidgetPayload(42_000));
        rig.Store.PeekNext(Broadcaster)!.VideoId.Should().Be(Second);
    }

    [Fact]
    public async Task A_stale_report_is_replayed_only_during_an_active_audio_page_transfer()
    {
        Rig rig = await BuildAsync();
        rig.Store.Report(Broadcaster, Audio, First, YouTubePlayerState.Playing, 42_000);
        rig.Clock.Advance(TimeSpan.FromSeconds(16));

        (await rig.Players.ReplayCurrentAsync(Broadcaster, allowStale: false)).Should().BeFalse();
        rig.Notifier.Sent.Should().BeEmpty();

        (await rig.Players.ReplayCurrentAsync(Broadcaster, allowStale: true)).Should().BeTrue();
        rig.Notifier.Sent.Select(s => s.EventType).Should().Equal("youtube.play", "youtube.seek");
    }

    [Fact]
    public async Task Two_attached_displays_get_youtube_play_only_on_the_audio_widget()
    {
        Rig rig = await BuildAsync();

        bool accepted = await rig.Provider.AddToQueueAsync(Broadcaster, Watch(First));

        accepted.Should().BeTrue();
        rig.Notifier.Sent.Should().ContainSingle();
        (Guid widget, string eventType, object? data) = rig.Notifier.Sent[0];
        widget.Should().Be(Audio);
        eventType.Should().Be("youtube.play");
        VideoOf(data).Should().Be(First);
    }

    [Fact]
    public async Task The_audio_page_reports_and_receives_the_next_hand_over_alone()
    {
        Rig rig = await BuildAsync();
        (await ReportAsync(rig, Audio, First, "playing")).IsSuccess.Should().BeTrue();
        await rig.Provider.AddToQueueAsync(Broadcaster, Watch(Second));
        (await ReportAsync(rig, Audio, First, "ended")).IsSuccess.Should().BeTrue();

        rig.Notifier.Sent.Should().ContainSingle("only the owner is told to play");
        rig.Notifier.Sent[0].Widget.Should().Be(Audio);
        VideoOf(rig.Notifier.Sent[0].Data).Should().Be(Second);
    }

    [Fact]
    public async Task A_report_from_another_widget_is_refused_while_the_owner_is_attached_and_playing()
    {
        Rig rig = await BuildAsync();
        (await ReportAsync(rig, Audio, First, "playing")).IsSuccess.Should().BeTrue();
        YouTubePlayerReport? before = rig.Store.GetFresh(Broadcaster);
        int published = rig.Bus.Published.Count;

        Result stray = await ReportAsync(rig, Late, Second, "playing");

        stray.IsSuccess.Should().BeFalse();
        rig.Store.GetFresh(Broadcaster).Should().Be(before);
        rig.Bus.Published.Should().HaveCount(published, "a refused report publishes nothing");
        await rig.Provider.AddToQueueAsync(Broadcaster, Watch(Second));
        (await ReportAsync(rig, Audio, First, "ended")).IsSuccess.Should().BeTrue();
        rig.Notifier.Sent.Should()
            .ContainSingle("the stray page never became the owner")
            .Which.Widget.Should()
            .Be(Audio);
    }

    [Fact]
    public async Task A_stray_ended_report_does_not_start_the_buffered_video()
    {
        Rig rig = await BuildAsync();
        await ReportAsync(rig, Audio, First, "playing");
        await rig.Provider.AddToQueueAsync(Broadcaster, Watch(Second));

        Result stray = await ReportAsync(rig, Late, First, "ended");

        stray.IsSuccess.Should().BeFalse();
        rig.Notifier.Sent.Should().BeEmpty("the owner is busy and the stray report was refused");
        rig.Store.PeekNext(Broadcaster)!.VideoId.Should().Be(Second);
    }

    [Fact]
    public async Task When_the_audio_page_closes_the_request_does_not_go_to_a_display_widget()
    {
        Rig rig = await BuildAsync();
        await ReportAsync(rig, Audio, First, "playing");
        await rig.Provider.AddToQueueAsync(Broadcaster, Watch(Second));
        rig.Attached.Remove(Audio);

        Result ended = await ReportAsync(rig, Late, First, "ended");

        ended.IsSuccess.Should().BeTrue();
        rig.Notifier.Sent.Should().BeEmpty();
        rig.Store.PeekNext(Broadcaster)!.VideoId.Should().Be(Second);
    }
}
