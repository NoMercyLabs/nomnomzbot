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
using NomNomzBot.Infrastructure.Moderation;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// The per-channel follow baseline (spam-defense.md §L3). A spike chooses a window to look at; these
/// tests pin when that window opens, what it contains, and that it never opens on a quiet channel.
/// </summary>
public class FollowSpikeTrackerTests
{
    private static readonly Guid Channel = Guid.Parse("0199c000-0000-7000-8000-0000000000f1");
    private static readonly DateTimeOffset Start = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    private const double Factor = 5;

    private readonly FollowSpikeTracker _tracker = new();

    private static FollowObservation Follow(string id, DateTimeOffset at) =>
        new(id, $"viewer{id}", $"Viewer{id}", at);

    /// <summary>One follow a minute for twelve minutes: a steady, ordinary channel.</summary>
    private void WarmUpQuietChannel()
    {
        for (int minute = 0; minute < 12; minute++)
            _tracker
                .Observe(Channel, Follow($"calm{minute}", Start.AddMinutes(minute)), Factor)
                .Should()
                .BeNull("one follow a minute is the channel's normal");
    }

    [Fact]
    public void ABusyMinuteOnAFreshChannel_IsNotASpike_BecauseThereIsNoBaselineYet()
    {
        for (int i = 0; i < 40; i++)
            _tracker
                .Observe(Channel, Follow($"n{i}", Start.AddSeconds(i)), Factor)
                .Should()
                .BeNull("a brand-new channel's opening night must not read as an attack");
    }

    [Fact]
    public void AFewExtraFollows_AreNotASpike_EvenAtSeveralTimesTheBaseline()
    {
        WarmUpQuietChannel();
        DateTimeOffset minute = Start.AddMinutes(12);

        // Four in one minute is four times a baseline of one, and still under the five-a-minute floor.
        for (int i = 0; i < 4; i++)
            _tracker
                .Observe(Channel, Follow($"few{i}", minute.AddSeconds(i)), Factor)
                .Should()
                .BeNull();
    }

    [Fact]
    public void ARateAboveTheChannelsOwnBaseline_OpensTheWindowWithEveryFollowOfThatMinute()
    {
        WarmUpQuietChannel();
        DateTimeOffset minute = Start.AddMinutes(12);

        for (int i = 0; i < 5; i++)
            _tracker
                .Observe(Channel, Follow($"b{i}", minute.AddSeconds(i)), Factor)
                .Should()
                .BeNull();
        FollowSpikeWindow? window = _tracker.Observe(
            Channel,
            Follow("b5", minute.AddSeconds(5)),
            Factor
        );

        window.Should().NotBeNull();
        window!.Unexamined.Select(f => f.UserId).Should().Equal("b0", "b1", "b2", "b3", "b4", "b5");
        window.WindowSize.Should().Be(6);
    }

    [Fact]
    public void FollowsAfterTheSpikeOpened_JoinTheSameBatch_AndAreNotExaminedTwice()
    {
        WarmUpQuietChannel();
        DateTimeOffset minute = Start.AddMinutes(12);
        FollowSpikeWindow? first = null;
        for (int i = 0; i < 6; i++)
            first = _tracker.Observe(Channel, Follow($"b{i}", minute.AddSeconds(i)), Factor);

        FollowSpikeWindow? next = _tracker.Observe(
            Channel,
            Follow("b6", minute.AddSeconds(6)),
            Factor
        );

        next.Should().NotBeNull();
        next!.BatchId.Should().Be(first!.BatchId, "one spike is one reversible batch");
        next.Unexamined.Select(f => f.UserId).Should().Equal("b6");
        next.WindowSize.Should().Be(7);
    }

    [Fact]
    public void ANewSpikeInALaterMinute_IsAnotherBatch()
    {
        WarmUpQuietChannel();
        DateTimeOffset minute = Start.AddMinutes(12);
        FollowSpikeWindow? first = null;
        for (int i = 0; i < 6; i++)
            first = _tracker.Observe(Channel, Follow($"a{i}", minute.AddSeconds(i)), Factor);

        FollowSpikeWindow? second = null;
        for (int i = 0; i < 6; i++)
            second = _tracker.Observe(
                Channel,
                Follow($"c{i}", minute.AddMinutes(3).AddSeconds(i)),
                Factor
            );

        second.Should().NotBeNull();
        second!.BatchId.Should().NotBe(first!.BatchId);
    }

    [Fact]
    public void ASpikeMinute_DoesNotTeachTheBaselineThatAttacksAreNormal()
    {
        WarmUpQuietChannel();
        DateTimeOffset minute = Start.AddMinutes(12);
        for (int i = 0; i < 30; i++)
            _tracker.Observe(Channel, Follow($"a{i}", minute.AddSeconds(i)), Factor);

        // Had the 30-follow minute been recorded the mean would be about 3 and 12 would be no spike.
        FollowSpikeWindow? window = null;
        for (int i = 0; i < 12; i++)
            window = _tracker.Observe(
                Channel,
                Follow($"d{i}", minute.AddMinutes(1).AddSeconds(i)),
                Factor
            );

        window.Should().NotBeNull();
    }

    [Fact]
    public void ARepeatedFollowEventForTheSameAccount_CountsOnce()
    {
        WarmUpQuietChannel();
        DateTimeOffset minute = Start.AddMinutes(12);

        for (int i = 0; i < 30; i++)
            _tracker
                .Observe(Channel, Follow("same", minute.AddSeconds(i)), Factor)
                .Should()
                .BeNull("one account re-following is one follow, not thirty");
    }

    [Fact]
    public void ChannelsKeepSeparateBaselines()
    {
        Guid other = Guid.Parse("0199c000-0000-7000-8000-0000000000f2");
        WarmUpQuietChannel();
        DateTimeOffset minute = Start.AddMinutes(12);

        FollowSpikeWindow? otherWindow = null;
        for (int i = 0; i < 12; i++)
            otherWindow = _tracker.Observe(other, Follow($"o{i}", minute.AddSeconds(i)), Factor);

        otherWindow
            .Should()
            .BeNull("the other channel has no history of its own to compare against");
    }
}
