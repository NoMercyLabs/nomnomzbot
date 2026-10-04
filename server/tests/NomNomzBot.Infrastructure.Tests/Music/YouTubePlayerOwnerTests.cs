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
/// S-YOUTUBE-NOW-PLAYING, one player widget owns the YouTube audio: with two open now playing pages,
/// <c>youtube.play</c> goes to exactly one of them. The owner is the widget whose report was last accepted
/// while it is still attached; with no owner it is the attached player widget created first (lowest id on a
/// tie). A report from another widget while the owner is attached and playing is refused and stores nothing.
/// </summary>
public sealed class YouTubePlayerOwnerTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192b000-0000-7000-8000-0000000a7d01");

    // The earlier widget has the HIGHER id, so "earliest created" and "lowest id" pick different widgets.
    private static readonly Guid Early = Guid.Parse("0192b000-0000-7000-8000-0000000a7d09");
    private static readonly Guid Late = Guid.Parse("0192b000-0000-7000-8000-0000000a7d02");

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
        public required RecordingWidgetEventNotifier Notifier { get; init; }
        public required RecordingEventBus Bus { get; init; }
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
        await db.SaveChangesAsync();

        HashSet<Guid> attached = [Early, Late];
        IOverlayPresenceRegistry presence = Substitute.For<IOverlayPresenceRegistry>();
        presence
            .IsWidgetAttached(Broadcaster, Arg.Any<Guid>())
            .Returns(call => attached.Contains(call.ArgAt<Guid>(1)));

        FakeTimeProvider clock = new(DateTimeOffset.Parse("2026-10-04T12:00:00Z"));
        RecordingWidgetEventNotifier notifier = new();
        RecordingEventBus bus = new();
        YouTubePlayerStateStore store = new(clock);
        YouTubePlayerDispatcher players = new(db, presence, notifier, store);
        Rig rig = new()
        {
            Provider = YouTubeProviderFactory.Create(db: db, playerState: store, players: players),
            Reports = new YouTubePlayerReportService(store, players, bus),
            Store = store,
            Notifier = notifier,
            Bus = bus,
            Attached = attached,
        };
        return rig;
    }

    private static string Watch(string videoId) => $"https://www.youtube.com/watch?v={videoId}";

    private static Task<Result> ReportAsync(Rig rig, Guid widget, string videoId, string state) =>
        rig.Reports.ReportAsync(Broadcaster, widget, videoId, state, 1000);

    private static string VideoOf(object? data) => ((YouTubePlayWidgetPayload)data!).VideoId;

    [Fact]
    public async Task Two_attached_players_get_youtube_play_on_exactly_one_the_earliest_created()
    {
        Rig rig = await BuildAsync();

        bool accepted = await rig.Provider.AddToQueueAsync(Broadcaster, Watch(First));

        accepted.Should().BeTrue();
        rig.Notifier.Sent.Should().ContainSingle();
        (Guid widget, string eventType, object? data) = rig.Notifier.Sent[0];
        widget.Should().Be(Early);
        eventType.Should().Be("youtube.play");
        VideoOf(data).Should().Be(First);
    }

    [Fact]
    public async Task The_widget_whose_report_was_accepted_owns_the_next_hand_over_alone()
    {
        Rig rig = await BuildAsync();
        rig.Attached.Remove(Early);
        (await ReportAsync(rig, Late, First, "playing")).IsSuccess.Should().BeTrue();
        await rig.Provider.AddToQueueAsync(Broadcaster, Watch(Second));
        rig.Attached.Add(Early);

        (await ReportAsync(rig, Late, First, "ended")).IsSuccess.Should().BeTrue();

        rig.Notifier.Sent.Should().ContainSingle("only the owner is told to play");
        rig.Notifier.Sent[0].Widget.Should().Be(Late);
        VideoOf(rig.Notifier.Sent[0].Data).Should().Be(Second);
    }

    [Fact]
    public async Task A_report_from_another_widget_is_refused_while_the_owner_is_attached_and_playing()
    {
        Rig rig = await BuildAsync();
        (await ReportAsync(rig, Early, First, "playing")).IsSuccess.Should().BeTrue();
        YouTubePlayerReport? before = rig.Store.GetFresh(Broadcaster);
        int published = rig.Bus.Published.Count;

        Result stray = await ReportAsync(rig, Late, Second, "playing");

        stray.IsSuccess.Should().BeFalse();
        rig.Store.GetFresh(Broadcaster).Should().Be(before);
        rig.Bus.Published.Should().HaveCount(published, "a refused report publishes nothing");
        await rig.Provider.AddToQueueAsync(Broadcaster, Watch(Second));
        (await ReportAsync(rig, Early, First, "ended")).IsSuccess.Should().BeTrue();
        rig.Notifier.Sent.Should()
            .ContainSingle("the stray page never became the owner")
            .Which.Widget.Should()
            .Be(Early);
    }

    [Fact]
    public async Task A_stray_ended_report_does_not_start_the_buffered_video()
    {
        Rig rig = await BuildAsync();
        await ReportAsync(rig, Early, First, "playing");
        await rig.Provider.AddToQueueAsync(Broadcaster, Watch(Second));

        Result stray = await ReportAsync(rig, Late, First, "ended");

        stray.IsSuccess.Should().BeFalse();
        rig.Notifier.Sent.Should().BeEmpty("the owner is busy and the stray report was refused");
        rig.Store.PeekNext(Broadcaster)!.VideoId.Should().Be(Second);
    }

    [Fact]
    public async Task When_the_owners_page_closes_the_next_hand_over_goes_to_the_remaining_widget()
    {
        Rig rig = await BuildAsync();
        await ReportAsync(rig, Early, First, "playing");
        await rig.Provider.AddToQueueAsync(Broadcaster, Watch(Second));
        rig.Attached.Remove(Early);

        Result ended = await ReportAsync(rig, Late, First, "ended");

        ended.IsSuccess.Should().BeTrue("the owner is gone, so the remaining page may report");
        rig.Notifier.Sent.Should().ContainSingle();
        rig.Notifier.Sent[0].Widget.Should().Be(Late);
        VideoOf(rig.Notifier.Sent[0].Data).Should().Be(Second);
    }
}
