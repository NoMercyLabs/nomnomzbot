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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.MediaShare.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.MediaShare.Entities;
using NomNomzBot.Domain.MediaShare.Events;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.MediaShare;
using NomNomzBot.Infrastructure.MediaShare.Playback;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.MediaShare;

/// <summary>
/// An item that becomes approved starts playing on its own when nothing plays and the media_share overlay
/// is attached; with no overlay it stays queued, and an item that is already playing is never reloaded.
/// </summary>
public sealed class MediaShareAutoStartHandlerTests
{
    private static readonly Guid Channel = Guid.Parse("0192d000-0000-7000-8000-00000000c001");
    private static readonly Guid Viewer = Guid.Parse("0192d000-0000-7000-8000-00000000a001");
    private static readonly Guid WidgetId = Guid.Parse("0192d000-0000-7000-8000-00000000b001");

    private sealed class RecordingBus : IEventBus
    {
        public List<IDomainEvent> Published { get; } = [];

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
            where TEvent : class, IDomainEvent
        {
            Published.Add(@event);
            return Task.CompletedTask;
        }

        public void PublishFireAndForget<TEvent>(TEvent @event)
            where TEvent : class, IDomainEvent => Published.Add(@event);
    }

    private sealed record Harness(
        MediaShareAutoStartHandler Sut,
        MediaShareTestDbContext Db,
        RecordingBus Bus
    );

    private static Harness Build(bool attached)
    {
        MediaShareTestDbContext db = MediaShareTestDbContext.New();
        db.Users.Add(
            new()
            {
                Id = Viewer,
                TwitchUserId = "111",
                Username = "alice",
                UsernameNormalized = "alice",
                DisplayName = "Alice",
            }
        );
        db.SaveChanges();

        RecordingBus bus = new();
        FakeTimeProvider clock = new(new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero));
        MediaShareService queue = new(
            db,
            Substitute.For<IMediaSourceResolver>(),
            Substitute.For<ICurrencyAccountService>(),
            bus,
            clock
        );
        MediaSharePlaybackService playback = new(queue, new MediaPlayerStateStore(), bus, clock);

        IWidgetService widgets = Substitute.For<IWidgetService>();
        widgets
            .EnsureSystemWidgetAsync(
                Channel.ToString(),
                "media_share",
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(Widget(true)));

        IOverlayPresenceRegistry presence = Substitute.For<IOverlayPresenceRegistry>();
        presence.IsWidgetAttached(Channel, WidgetId).Returns(attached);

        return new(new(queue, playback, widgets, presence), db, bus);
    }

    private static WidgetDetail Widget(bool enabled) =>
        new(
            WidgetId,
            "Media Share",
            null,
            "vue",
            "",
            enabled,
            null,
            null,
            null,
            [],
            [],
            null,
            null,
            DateTime.UnixEpoch,
            DateTime.UnixEpoch,
            false,
            false,
            false
        );

    private static Guid Seed(Harness h, string title, string status, int? position)
    {
        Guid id = Guid.NewGuid();
        h.Db.MediaShareRequests.Add(
            new()
            {
                Id = id,
                BroadcasterId = Channel,
                RequesterUserId = Viewer,
                RequesterTwitchUserId = "hash",
                SourceType = MediaShareSourceType.TwitchClip,
                SourceUrl = "https://clips.twitch.tv/" + title,
                MediaRef = title,
                Title = title,
                DurationSeconds = 30,
                Status = status,
                QueuePosition = position,
                RequestedAt = new(2026, 7, 11, 11, 0, (position ?? 0) % 60, DateTimeKind.Utc),
            }
        );
        h.Db.SaveChanges();
        return id;
    }

    private static string StatusOf(Harness h, Guid id) =>
        h.Db.MediaShareRequests.AsNoTracking().Single(r => r.Id == id).Status;

    private static MediaSharePlaybackChangedEvent Event(Guid id, string status) =>
        new()
        {
            BroadcasterId = Channel,
            RequestId = id,
            Status = status,
            OccurredAt = DateTime.UnixEpoch,
        };

    [Fact]
    public async Task An_approved_item_with_nothing_playing_and_the_overlay_attached_starts_playing()
    {
        Harness h = Build(attached: true);
        Guid id = Seed(h, "first", MediaShareStatus.Approved, 1);

        await h.Sut.HandleAsync(Event(id, MediaShareStatus.Approved));

        StatusOf(h, id).Should().Be(MediaShareStatus.Playing);
        MediaPlayerCommandIssuedEvent load = h
            .Bus.Published.OfType<MediaPlayerCommandIssuedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        load.Command.Should().Be("load");
        load.RequestId.Should().Be(id);
    }

    [Fact]
    public async Task An_approved_item_with_no_overlay_attached_stays_queued_and_sends_nothing()
    {
        Harness h = Build(attached: false);
        Guid id = Seed(h, "first", MediaShareStatus.Approved, 1);

        await h.Sut.HandleAsync(Event(id, MediaShareStatus.Approved));

        StatusOf(h, id).Should().Be(MediaShareStatus.Approved);
        h.Bus.Published.OfType<MediaPlayerCommandIssuedEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task An_approved_item_while_another_plays_is_not_loaded_over_it()
    {
        Harness h = Build(attached: true);
        Guid playing = Seed(h, "playing", MediaShareStatus.Playing, null);
        Guid waiting = Seed(h, "waiting", MediaShareStatus.Approved, 1);

        await h.Sut.HandleAsync(Event(waiting, MediaShareStatus.Approved));

        StatusOf(h, playing).Should().Be(MediaShareStatus.Playing);
        StatusOf(h, waiting).Should().Be(MediaShareStatus.Approved);
        h.Bus.Published.OfType<MediaPlayerCommandIssuedEvent>().Should().BeEmpty();
    }

    [Theory]
    [InlineData(MediaShareStatus.Playing)]
    [InlineData(MediaShareStatus.Played)]
    [InlineData(MediaShareStatus.Skipped)]
    public async Task A_status_other_than_approved_never_starts_playback(string status)
    {
        Harness h = Build(attached: true);
        Guid id = Seed(h, "first", MediaShareStatus.Approved, 1);

        await h.Sut.HandleAsync(Event(id, status));

        StatusOf(h, id).Should().Be(MediaShareStatus.Approved);
        h.Bus.Published.OfType<MediaPlayerCommandIssuedEvent>().Should().BeEmpty();
    }
}
