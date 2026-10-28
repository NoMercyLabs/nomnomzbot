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
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Domain.Moderation.ChatSafety;
using NomNomzBot.Domain.Moderation.SpamDefense;
using NomNomzBot.Infrastructure.Moderation.ChatSafety;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// The chat safety meter: four sliding 60-second windows and the level read from them, driven through
/// Calm, Watch, Raid and Over by moving a fake clock.
/// </summary>
public class ChatSafetyMeterTests
{
    private static readonly Guid Channel = Guid.Parse("0199c000-0000-7000-8000-0000000000a1");

    private readonly FakeTimeProvider _time = new(
        new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero)
    );
    private readonly ChatSafetyMeter _meter;

    public ChatSafetyMeterTests() => _meter = new ChatSafetyMeter(_time);

    private void WarmBaseline(int linesPerMinute, int minutes)
    {
        for (int i = 0; i < minutes; i++)
        {
            for (int line = 0; line < linesPerMinute; line++)
                _meter.RecordChatLine(Channel);

            _time.Advance(TimeSpan.FromSeconds(59));
            _meter.Snapshot(Channel);
            _time.Advance(TimeSpan.FromSeconds(1));
        }
    }

    [Fact]
    public void Meters_count_only_the_last_60_seconds()
    {
        _meter.RecordChatLine(Channel);
        _meter.RecordFollow(Channel);
        _meter.RecordNewAccount(Channel);
        _meter.RecordFlaggedLine(Channel);
        _time.Advance(TimeSpan.FromSeconds(59));

        ChatSafetyStatusDto inside = _meter.Snapshot(Channel);
        _time.Advance(TimeSpan.FromSeconds(1));
        ChatSafetyStatusDto outside = _meter.Snapshot(Channel);

        inside
            .Should()
            .Be(new ChatSafetyStatusDto(ChatSafetyLevel.Calm, Math.Round(1 / 60.0, 2), 1, 1, 1));
        outside.Should().Be(new ChatSafetyStatusDto(ChatSafetyLevel.Calm, 0, 0, 0, 0));
    }

    [Fact]
    public void Level_moves_from_Calm_to_Watch_to_Raid_to_Over_and_back_to_Calm()
    {
        WarmBaseline(linesPerMinute: 30, minutes: 25);
        _meter.Snapshot(Channel).Level.Should().Be(ChatSafetyLevel.Calm);

        for (int i = 0; i < 100; i++)
            _meter.RecordChatLine(Channel);
        ChatSafetyStatusDto watch = _meter.Snapshot(Channel);
        watch.Level.Should().Be(ChatSafetyLevel.Watch);
        watch.MessagesPerSecond.Should().Be(Math.Round(100 / 60.0, 2));

        _meter.ObserveCohort(
            Channel,
            "buy followers",
            CohortVerdict.Campaign,
            8,
            _time.GetUtcNow().AddMinutes(10)
        );
        _meter.Snapshot(Channel).Level.Should().Be(ChatSafetyLevel.Raid);

        _time.Advance(TimeSpan.FromMinutes(11));
        _meter.Snapshot(Channel).Level.Should().Be(ChatSafetyLevel.Over);

        _time.Advance(TimeSpan.FromMinutes(4));
        _meter.Snapshot(Channel).Level.Should().Be(ChatSafetyLevel.Over);

        _time.Advance(TimeSpan.FromMinutes(1.1));
        _meter.Snapshot(Channel).Level.Should().Be(ChatSafetyLevel.Calm);
    }

    [Fact]
    public void A_follow_spike_over_the_baseline_reads_as_Raid()
    {
        WarmBaseline(linesPerMinute: 0, minutes: 25);
        for (int i = 0; i < 6; i++)
            _meter.RecordFollow(Channel);

        _meter.Snapshot(Channel).Level.Should().Be(ChatSafetyLevel.Raid);
    }

    [Fact]
    public void Lockdown_and_shield_mode_read_as_Raid_until_they_end()
    {
        _meter.SetLockdown(Channel, true);
        _meter.Snapshot(Channel).Level.Should().Be(ChatSafetyLevel.Raid);
        _meter.SetLockdown(Channel, false);
        _meter.SetShieldMode(Channel, true);
        _meter.Snapshot(Channel).Level.Should().Be(ChatSafetyLevel.Raid);
        _meter.SetShieldMode(Channel, false);

        _meter.Snapshot(Channel).Level.Should().Be(ChatSafetyLevel.Over);
    }

    [Fact]
    public void A_watching_cohort_of_two_accounts_reads_as_Watch_and_one_account_does_not()
    {
        _meter.ObserveCohort(
            Channel,
            "hello",
            CohortVerdict.Watching,
            1,
            _time.GetUtcNow().AddMinutes(10)
        );
        _meter.Snapshot(Channel).Level.Should().Be(ChatSafetyLevel.Calm);

        _meter.ObserveCohort(
            Channel,
            "hello",
            CohortVerdict.Watching,
            2,
            _time.GetUtcNow().AddMinutes(10)
        );
        _meter.Snapshot(Channel).Level.Should().Be(ChatSafetyLevel.Watch);
    }

    [Fact]
    public void An_account_counts_as_new_only_below_seven_days_and_only_once_per_stream()
    {
        DateTimeOffset now = _time.GetUtcNow();

        ChatSafetyMeter.IsNewAccount(now.UtcDateTime.AddDays(-6), now).Should().BeTrue();
        ChatSafetyMeter.IsNewAccount(now.UtcDateTime.AddDays(-7), now).Should().BeFalse();
        ChatSafetyMeter.IsNewAccount(null, now).Should().BeFalse();

        _meter.MarkFirstLine(Channel, "42").Should().BeTrue();
        _meter.MarkFirstLine(Channel, "42").Should().BeFalse();
        _meter.ResetStream(Channel);
        _meter.MarkFirstLine(Channel, "42").Should().BeTrue();
    }
}
