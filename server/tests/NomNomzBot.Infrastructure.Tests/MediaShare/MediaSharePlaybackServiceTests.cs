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
using NomNomzBot.Application.MediaShare.Dtos;
using NomNomzBot.Application.MediaShare.Services;
using NomNomzBot.Domain.MediaShare.Entities;
using NomNomzBot.Domain.MediaShare.Events;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.MediaShare;
using NomNomzBot.Infrastructure.MediaShare.Playback;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.MediaShare;

/// <summary>
/// Proves the server owns the media-share player: each command moves the queue rows and tells the overlay
/// (a command event), and an overlay report of ended or error advances the queue to the next item.
/// </summary>
public sealed class MediaSharePlaybackServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192d000-0000-7000-8000-00000000c001");
    private static readonly Guid Viewer = Guid.Parse("0192d000-0000-7000-8000-00000000a001");

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
        MediaSharePlaybackService Sut,
        MediaShareService Queue,
        MediaShareTestDbContext Db,
        MediaPlayerStateStore Store,
        RecordingBus Bus
    );

    private static Harness Build()
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
        MediaPlayerStateStore store = new();
        MediaSharePlaybackService sut = new(queue, store, bus, clock);
        return new(sut, queue, db, store, bus);
    }

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

    private static MediaPlayerCommandIssuedEvent OnlyCommand(Harness h) =>
        h.Bus.Published.OfType<MediaPlayerCommandIssuedEvent>().Should().ContainSingle().Subject;

    private static MediaPlaybackCommandRequest Cmd(
        string command,
        int? position = null,
        int? volume = null,
        Guid? id = null
    ) => new(command, position, volume, id);

    [Fact]
    public async Task Play_LoadsTheFrontApprovedItem_AsPlaying_AndTellsTheOverlayToLoadIt()
    {
        Harness h = Build();
        Guid first = Seed(h, "first", MediaShareStatus.Approved, 1);
        Guid second = Seed(h, "second", MediaShareStatus.Approved, 2);

        Result<MediaPlayerStateDto> result = await h.Sut.SendCommandAsync(
            Channel,
            Cmd(MediaPlaybackCommands.Play)
        );

        result.IsSuccess.Should().BeTrue();
        StatusOf(h, first).Should().Be(MediaShareStatus.Playing);
        StatusOf(h, second).Should().Be(MediaShareStatus.Approved);
        MediaPlayerCommandIssuedEvent load = OnlyCommand(h);
        load.Command.Should().Be("load");
        load.RequestId.Should().Be(first);
        load.SourceType.Should().Be(MediaShareSourceType.TwitchClip);
        load.MediaRef.Should().Be("first");
        load.DurationSeconds.Should().Be(30);
        load.BroadcasterId.Should().Be(Channel);
    }

    [Fact]
    public async Task Play_WithAnEmptyQueue_FailsAndSendsNothing()
    {
        Harness h = Build();

        Result<MediaPlayerStateDto> result = await h.Sut.SendCommandAsync(
            Channel,
            Cmd(MediaPlaybackCommands.Play)
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
        h.Bus.Published.OfType<MediaPlayerCommandIssuedEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task Play_WhilePaused_ResumesTheSameItem_WithoutAdvancingTheQueue()
    {
        Harness h = Build();
        Guid current = Seed(h, "current", MediaShareStatus.Playing, null);
        Guid waiting = Seed(h, "waiting", MediaShareStatus.Approved, 1);
        h.Store.Set(Channel, new("paused", current, "current", "Alice", null, 12, 30, 100));

        Result<MediaPlayerStateDto> result = await h.Sut.SendCommandAsync(
            Channel,
            Cmd(MediaPlaybackCommands.Play)
        );

        result.IsSuccess.Should().BeTrue();
        OnlyCommand(h).Command.Should().Be("play");
        StatusOf(h, current).Should().Be(MediaShareStatus.Playing);
        StatusOf(h, waiting).Should().Be(MediaShareStatus.Approved);
    }

    [Fact]
    public async Task Pause_TellsTheOverlayToPause()
    {
        Harness h = Build();
        Seed(h, "current", MediaShareStatus.Playing, null);

        Result<MediaPlayerStateDto> result = await h.Sut.SendCommandAsync(
            Channel,
            Cmd(MediaPlaybackCommands.Pause)
        );

        result.IsSuccess.Should().BeTrue();
        OnlyCommand(h).Command.Should().Be("pause");
    }

    [Fact]
    public async Task Seek_SendsThePosition_AndRejectsANegativeOne()
    {
        Harness h = Build();
        Seed(h, "current", MediaShareStatus.Playing, null);

        Result<MediaPlayerStateDto> bad = await h.Sut.SendCommandAsync(
            Channel,
            Cmd(MediaPlaybackCommands.Seek, position: -1)
        );
        Result<MediaPlayerStateDto> good = await h.Sut.SendCommandAsync(
            Channel,
            Cmd(MediaPlaybackCommands.Seek, position: 42)
        );

        bad.IsFailure.Should().BeTrue();
        good.IsSuccess.Should().BeTrue();
        MediaPlayerCommandIssuedEvent seek = OnlyCommand(h);
        seek.Command.Should().Be("seek");
        seek.PositionSeconds.Should().Be(42);
    }

    [Fact]
    public async Task Volume_SendsThePercent_AndRejectsOutOfRange()
    {
        Harness h = Build();

        Result<MediaPlayerStateDto> bad = await h.Sut.SendCommandAsync(
            Channel,
            Cmd(MediaPlaybackCommands.Volume, volume: 101)
        );
        Result<MediaPlayerStateDto> good = await h.Sut.SendCommandAsync(
            Channel,
            Cmd(MediaPlaybackCommands.Volume, volume: 35)
        );

        bad.IsFailure.Should().BeTrue();
        good.Value.Volume.Should().Be(35);
        OnlyCommand(h).Volume.Should().Be(35);
        h.Sut.GetState(Channel).Volume.Should().Be(35);
    }

    [Fact]
    public async Task Skip_SkipsTheCurrentItem_AndLoadsTheNextOne()
    {
        Harness h = Build();
        Guid current = Seed(h, "current", MediaShareStatus.Playing, null);
        Guid next = Seed(h, "next", MediaShareStatus.Approved, 1);

        Result<MediaPlayerStateDto> result = await h.Sut.SendCommandAsync(
            Channel,
            Cmd(MediaPlaybackCommands.Skip)
        );

        result.IsSuccess.Should().BeTrue();
        StatusOf(h, current).Should().Be(MediaShareStatus.Skipped);
        StatusOf(h, next).Should().Be(MediaShareStatus.Playing);
        OnlyCommand(h).RequestId.Should().Be(next);
    }

    [Fact]
    public async Task Skip_WithNothingBehindIt_SendsStop()
    {
        Harness h = Build();
        Guid current = Seed(h, "current", MediaShareStatus.Playing, null);

        Result<MediaPlayerStateDto> result = await h.Sut.SendCommandAsync(
            Channel,
            Cmd(MediaPlaybackCommands.Skip)
        );

        result.IsSuccess.Should().BeTrue();
        StatusOf(h, current).Should().Be(MediaShareStatus.Skipped);
        OnlyCommand(h).Command.Should().Be("stop");
        result.Value.State.Should().Be(MediaPlayerStates.Idle);
    }

    [Fact]
    public async Task Stop_MarksTheCurrentItemPlayed_AndTellsTheOverlayToStop()
    {
        Harness h = Build();
        Guid current = Seed(h, "current", MediaShareStatus.Playing, null);
        Guid waiting = Seed(h, "waiting", MediaShareStatus.Approved, 1);

        Result<MediaPlayerStateDto> result = await h.Sut.SendCommandAsync(
            Channel,
            Cmd(MediaPlaybackCommands.Stop)
        );

        result.IsSuccess.Should().BeTrue();
        StatusOf(h, current).Should().Be(MediaShareStatus.Played);
        StatusOf(h, waiting).Should().Be(MediaShareStatus.Approved);
        OnlyCommand(h).Command.Should().Be("stop");
    }

    [Fact]
    public async Task Replay_WithoutAnId_RestartsTheCurrentItemFromZero()
    {
        Harness h = Build();
        Guid current = Seed(h, "current", MediaShareStatus.Playing, null);

        Result<MediaPlayerStateDto> result = await h.Sut.SendCommandAsync(
            Channel,
            Cmd(MediaPlaybackCommands.Replay)
        );

        result.IsSuccess.Should().BeTrue();
        MediaPlayerCommandIssuedEvent seek = OnlyCommand(h);
        seek.Command.Should().Be("seek");
        seek.PositionSeconds.Should().Be(0);
        StatusOf(h, current).Should().Be(MediaShareStatus.Playing);
    }

    [Fact]
    public async Task Replay_OfAPlayedItem_QueuesItFirst_AndLoadsItWhenIdle()
    {
        Harness h = Build();
        Guid done = Seed(h, "done", MediaShareStatus.Played, null);
        Guid waiting = Seed(h, "waiting", MediaShareStatus.Approved, 1);

        Result<MediaPlayerStateDto> result = await h.Sut.SendCommandAsync(
            Channel,
            Cmd(MediaPlaybackCommands.Replay, id: done)
        );

        result.IsSuccess.Should().BeTrue();
        StatusOf(h, done).Should().Be(MediaShareStatus.Playing);
        StatusOf(h, waiting).Should().Be(MediaShareStatus.Approved);
        OnlyCommand(h).RequestId.Should().Be(done);
    }

    [Fact]
    public async Task PlayNow_MovesTheItemToTheFront_SkipsTheCurrentOne_AndLoadsIt()
    {
        Harness h = Build();
        Guid current = Seed(h, "current", MediaShareStatus.Playing, null);
        Seed(h, "first", MediaShareStatus.Approved, 1);
        Guid chosen = Seed(h, "chosen", MediaShareStatus.Approved, 2);

        Result<MediaPlayerStateDto> result = await h.Sut.SendCommandAsync(
            Channel,
            Cmd(MediaPlaybackCommands.PlayNow, id: chosen)
        );

        result.IsSuccess.Should().BeTrue();
        StatusOf(h, current).Should().Be(MediaShareStatus.Skipped);
        StatusOf(h, chosen).Should().Be(MediaShareStatus.Playing);
        OnlyCommand(h).RequestId.Should().Be(chosen);
    }

    [Fact]
    public async Task PlayNow_WithoutAnId_Fails()
    {
        Harness h = Build();

        Result<MediaPlayerStateDto> result = await h.Sut.SendCommandAsync(
            Channel,
            Cmd(MediaPlaybackCommands.PlayNow)
        );

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task UnknownCommand_Fails()
    {
        Harness h = Build();

        Result<MediaPlayerStateDto> result = await h.Sut.SendCommandAsync(Channel, Cmd("explode"));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task ReportPlaying_StoresTheStateWithTheItemDetails_AndTellsTheDashboard()
    {
        Harness h = Build();
        Guid current = Seed(h, "current", MediaShareStatus.Playing, null);

        Result result = await h.Sut.ReportStateAsync(
            Channel,
            new(current, MediaPlayerStates.Playing, 7, 30, 80)
        );

        result.IsSuccess.Should().BeTrue();
        MediaPlayerStateDto state = h.Sut.GetState(Channel);
        state.State.Should().Be(MediaPlayerStates.Playing);
        state.RequestId.Should().Be(current);
        state.Title.Should().Be("current");
        state.SharerName.Should().Be("Alice");
        state.PositionSeconds.Should().Be(7);
        state.Volume.Should().Be(80);
        MediaPlayerStateChangedEvent changed = h
            .Bus.Published.OfType<MediaPlayerStateChangedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        changed.State.Should().Be(MediaPlayerStates.Playing);
        changed.BroadcasterId.Should().Be(Channel);
        h.Bus.Published.OfType<MediaPlayerCommandIssuedEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task ReportEnded_MarksItPlayed_AndLoadsTheNextItem()
    {
        Harness h = Build();
        Guid current = Seed(h, "current", MediaShareStatus.Playing, null);
        Guid next = Seed(h, "next", MediaShareStatus.Approved, 1);

        Result result = await h.Sut.ReportStateAsync(
            Channel,
            new(current, MediaPlayerStates.Ended, 30, 30, 100)
        );

        result.IsSuccess.Should().BeTrue();
        StatusOf(h, current).Should().Be(MediaShareStatus.Played);
        StatusOf(h, next).Should().Be(MediaShareStatus.Playing);
        OnlyCommand(h).RequestId.Should().Be(next);
        h.Sut.GetState(Channel).RequestId.Should().Be(next);
    }

    [Fact]
    public async Task ReportEnded_WithAnEmptyQueue_GoesIdle_AndLoadsNothing()
    {
        Harness h = Build();
        Guid current = Seed(h, "current", MediaShareStatus.Playing, null);

        await h.Sut.ReportStateAsync(Channel, new(current, MediaPlayerStates.Ended, 30, 30, 100));

        StatusOf(h, current).Should().Be(MediaShareStatus.Played);
        h.Sut.GetState(Channel).State.Should().Be(MediaPlayerStates.Idle);
        h.Bus.Published.OfType<MediaPlayerCommandIssuedEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task ReportError_SkipsTheBrokenItem_AndLoadsTheNextItem()
    {
        Harness h = Build();
        Guid broken = Seed(h, "broken", MediaShareStatus.Playing, null);
        Guid next = Seed(h, "next", MediaShareStatus.Approved, 1);

        await h.Sut.ReportStateAsync(Channel, new(broken, MediaPlayerStates.Error, 0, 0, 100));

        StatusOf(h, broken).Should().Be(MediaShareStatus.Skipped);
        StatusOf(h, next).Should().Be(MediaShareStatus.Playing);
        OnlyCommand(h).RequestId.Should().Be(next);
    }

    [Fact]
    public async Task ReportEnded_ForAnItemThatIsNotCurrent_DoesNotAdvanceTheQueue()
    {
        Harness h = Build();
        Guid current = Seed(h, "current", MediaShareStatus.Playing, null);
        Guid waiting = Seed(h, "waiting", MediaShareStatus.Approved, 1);

        await h.Sut.ReportStateAsync(
            Channel,
            new(Guid.NewGuid(), MediaPlayerStates.Ended, 30, 30, 100)
        );

        StatusOf(h, current).Should().Be(MediaShareStatus.Playing);
        StatusOf(h, waiting).Should().Be(MediaShareStatus.Approved);
        h.Bus.Published.OfType<MediaPlayerCommandIssuedEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task Report_WithAnUnknownState_Fails()
    {
        Harness h = Build();

        Result result = await h.Sut.ReportStateAsync(Channel, new(null, "dancing", 0, 0, 100));

        result.IsFailure.Should().BeTrue();
        h.Bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task Replay_ServiceMovesAPlayedItemToFirst_AndShiftsTheRest()
    {
        Harness h = Build();
        Guid done = Seed(h, "done", MediaShareStatus.Played, null);
        Guid waiting = Seed(h, "waiting", MediaShareStatus.Approved, 1);

        Result<MediaShareRequestDto> result = await h.Queue.ReplayAsync(Channel, done);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(MediaShareStatus.Approved);
        result.Value.QueuePosition.Should().Be(1);
        h.Db.MediaShareRequests.AsNoTracking()
            .Single(r => r.Id == waiting)
            .QueuePosition.Should()
            .Be(2);
        h.Bus.Published.OfType<MediaSharePlaybackChangedEvent>()
            .Should()
            .ContainSingle(e => e.RequestId == done && e.Status == MediaShareStatus.Approved);
    }

    [Fact]
    public async Task Replay_ServiceRefusesAnItemThatNeverFinished()
    {
        Harness h = Build();
        Guid pending = Seed(h, "pending", MediaShareStatus.Pending, null);

        Result<MediaShareRequestDto> result = await h.Queue.ReplayAsync(Channel, pending);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
    }
}
