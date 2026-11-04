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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.Enums;
using NomNomzBot.Infrastructure.Moderation;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// The held-text check against a real queue table: only a pending (or denied, actioned, expired) row of the same
/// viewer with the same text inside the time window holds the text back.
/// </summary>
public sealed class HeldTextServiceTests
{
    private static readonly Guid Channel = Guid.Parse("019f2900-0000-7000-8000-0000000000a1");
    private const string Viewer = "1403633423";
    private const string Text = "Ebola Ebola bola";

    private static readonly DateTimeOffset Start = new(2026, 11, 3, 18, 9, 45, TimeSpan.Zero);

    private static ModerationQueueItem Row(
        ModerationQueueStatus status,
        string? twitchUserId = Viewer,
        string content = Text,
        TimeSpan? age = null
    ) =>
        new()
        {
            BroadcasterId = Channel,
            Source = ModerationQueueSource.AutoMod,
            Status = status,
            TargetTwitchUserId = twitchUserId,
            AutoModMessageId = Guid.NewGuid().ToString(),
            MessageContentSnapshot = content,
            CreatedAt = (Start - (age ?? TimeSpan.FromSeconds(1))).UtcDateTime,
        };

    private static (
        HeldTextService Service,
        ModerationServiceTestDbContext Db,
        FakeTimeProvider Time
    ) Build(params ModerationQueueItem[] rows)
    {
        ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        db.ModerationQueueItems.AddRange(rows);
        db.SaveChanges();
        FakeTimeProvider time = new(Start);
        return (new(db, time), db, time);
    }

    /// <summary>Runs the check while moving the fake clock, so the bounded wait finishes without sleeping.</summary>
    private static async Task<bool> RunAsync(
        Task<bool> check,
        FakeTimeProvider time,
        Func<Task>? afterFirstPoll = null
    )
    {
        await Task.Delay(50);
        if (afterFirstPoll is not null)
            await afterFirstPoll();
        while (!check.IsCompleted)
        {
            time.Advance(TimeSpan.FromMilliseconds(250));
            await Task.Delay(2);
        }
        return await check;
    }

    [Theory]
    [InlineData(ModerationQueueStatus.Pending)]
    [InlineData(ModerationQueueStatus.Denied)]
    [InlineData(ModerationQueueStatus.Actioned)]
    [InlineData(ModerationQueueStatus.Expired)]
    public async Task A_matching_row_that_is_not_approved_holds_the_text_without_waiting(
        ModerationQueueStatus status
    )
    {
        (HeldTextService service, _, FakeTimeProvider time) = Build(Row(status));

        bool held = await service.IsTextHeldAsync(Channel, Viewer, Text);

        held.Should().BeTrue();
        time.GetUtcNow().Should().Be(Start, "a found row decides at once");
    }

    [Fact]
    public async Task A_matching_approved_row_lets_the_text_through_without_waiting()
    {
        (HeldTextService service, _, FakeTimeProvider time) = Build(
            Row(ModerationQueueStatus.Approved)
        );

        bool held = await service.IsTextHeldAsync(Channel, Viewer, Text);

        held.Should().BeFalse();
        time.GetUtcNow().Should().Be(Start);
    }

    [Fact]
    public async Task Replay_matches_a_denied_row_at_the_original_redemption_time()
    {
        (HeldTextService service, _, FakeTimeProvider time) = Build(
            Row(ModerationQueueStatus.Denied)
        );
        time.Advance(TimeSpan.FromDays(7));

        bool held = await service.IsTextHeldAsync(Channel, Viewer, Text, occurredAt: Start);

        held.Should().BeTrue();
    }

    [Fact]
    public async Task The_text_matches_after_trimming_and_collapsing_whitespace()
    {
        (HeldTextService service, _, _) = Build(
            Row(ModerationQueueStatus.Pending, content: "  Ebola   Ebola\tbola \n")
        );

        bool held = await service.IsTextHeldAsync(Channel, Viewer, "Ebola Ebola   bola");

        held.Should().BeTrue();
    }

    [Fact]
    public async Task With_no_row_the_check_waits_the_bounded_time_and_then_lets_the_text_through()
    {
        (HeldTextService service, _, FakeTimeProvider time) = Build();

        bool held = await RunAsync(service.IsTextHeldAsync(Channel, Viewer, Text), time);

        held.Should().BeFalse();
        (time.GetUtcNow() - Start)
            .Should()
            .BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(3))
            .And.BeLessThan(TimeSpan.FromSeconds(4));
    }

    [Fact]
    public async Task A_row_of_another_viewer_does_not_hold_the_text()
    {
        (HeldTextService service, _, FakeTimeProvider time) = Build(
            Row(ModerationQueueStatus.Pending, twitchUserId: "999")
        );

        bool held = await RunAsync(service.IsTextHeldAsync(Channel, Viewer, Text), time);

        held.Should().BeFalse();
    }

    [Fact]
    public async Task A_row_with_other_text_does_not_hold_the_text()
    {
        (HeldTextService service, _, FakeTimeProvider time) = Build(
            Row(ModerationQueueStatus.Pending, content: "something else entirely")
        );

        bool held = await RunAsync(service.IsTextHeldAsync(Channel, Viewer, Text), time);

        held.Should().BeFalse();
    }

    [Fact]
    public async Task A_row_of_another_channel_does_not_hold_the_text()
    {
        ModerationQueueItem other = Row(ModerationQueueStatus.Pending);
        other.BroadcasterId = Guid.Parse("019f2900-0000-7000-8000-0000000000b2");
        (HeldTextService service, _, FakeTimeProvider time) = Build(other);

        bool held = await RunAsync(service.IsTextHeldAsync(Channel, Viewer, Text), time);

        held.Should().BeFalse();
    }

    [Fact]
    public async Task A_row_older_than_two_minutes_does_not_hold_the_text()
    {
        (HeldTextService service, _, FakeTimeProvider time) = Build(
            Row(ModerationQueueStatus.Pending, age: TimeSpan.FromMinutes(3))
        );

        bool held = await RunAsync(service.IsTextHeldAsync(Channel, Viewer, Text), time);

        held.Should().BeFalse();
    }

    [Fact]
    public async Task A_row_that_arrives_during_the_wait_holds_the_text()
    {
        (HeldTextService service, ModerationServiceTestDbContext db, FakeTimeProvider time) =
            Build();

        bool held = await RunAsync(
            service.IsTextHeldAsync(Channel, Viewer, Text),
            time,
            async () =>
            {
                db.ModerationQueueItems.Add(Row(ModerationQueueStatus.Pending));
                await db.SaveChangesAsync();
            }
        );

        held.Should().BeTrue();
        (time.GetUtcNow() - Start).Should().BeLessThan(TimeSpan.FromSeconds(3));
    }
}
