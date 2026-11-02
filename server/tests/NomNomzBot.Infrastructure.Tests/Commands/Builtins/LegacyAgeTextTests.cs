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
using NomNomzBot.Application.Commands.Builtin;

namespace NomNomzBot.Infrastructure.Tests.Commands.Builtins;

/// <summary>
/// The duration wording of the old bot's <c>!accountage</c> and <c>!followage</c>, counted in real calendar
/// months and years. The old bot counted every month as 30 days and every year as 365, so its "days" part drifted
/// from the calendar by up to four days; the words stay, the counting is the calendar's.
/// </summary>
public sealed class LegacyAgeTextTests
{
    private static DateTimeOffset At(int year, int month, int day, int hour = 0, int minute = 0) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);

    [Fact]
    public void FollowAge_counts_calendar_months_so_191_days_is_6_months_and_7_days()
    {
        // The span a viewer saw in chat on 2026-11-02: StreamElements said "6 months 7 days 23 hours"; the old
        // 30-day count said "6 months and 11 days".
        string text = LegacyAgeText.FollowAge(At(2026, 4, 25, 1), At(2026, 11, 2));

        text.Should().Be("6 months and 7 days");
    }

    [Theory]
    [InlineData(2026, 1, 1, 2027, 1, 1, "1 year")]
    [InlineData(2026, 1, 1, 2027, 2, 5, "1 year and 35 days")]
    [InlineData(2026, 1, 1, 2028, 3, 12, "2 years and 71 days")]
    [InlineData(2026, 1, 1, 2026, 2, 1, "1 month")]
    [InlineData(2026, 1, 1, 2026, 2, 2, "1 month and 1 day")]
    [InlineData(2026, 1, 1, 2026, 2, 16, "1 month and 15 days")]
    [InlineData(2026, 1, 31, 2026, 2, 27, "27 days")]
    [InlineData(2026, 1, 31, 2026, 2, 28, "1 month")]
    [InlineData(2026, 1, 31, 2026, 3, 1, "1 month and 1 day")]
    [InlineData(2026, 3, 1, 2026, 3, 2, "1 day")]
    [InlineData(2026, 3, 1, 2026, 3, 6, "5 days")]
    public void FollowAge_words_a_calendar_span_in_months_and_days(
        int fromYear,
        int fromMonth,
        int fromDay,
        int toYear,
        int toMonth,
        int toDay,
        string expected
    ) =>
        LegacyAgeText
            .FollowAge(At(fromYear, fromMonth, fromDay), At(toYear, toMonth, toDay))
            .Should()
            .Be(expected);

    [Theory]
    [InlineData(1, 0, "1 hour")]
    [InlineData(5, 0, "5 hours")]
    [InlineData(0, 10, "10 minutes")]
    [InlineData(0, 0, "1 minute")]
    public void FollowAge_and_AccountAge_word_a_span_under_a_day_in_hours_or_minutes(
        int hours,
        int minutes,
        string expected
    )
    {
        DateTimeOffset since = At(2026, 5, 1, 12);
        DateTimeOffset now = since.AddHours(hours).AddMinutes(minutes);

        LegacyAgeText.FollowAge(since, now).Should().Be(expected);
        LegacyAgeText.AccountAge(since, now).Should().Be(expected);
    }

    [Theory]
    [InlineData(2026, 1, 1, 2027, 1, 1, "1 year")]
    [InlineData(2025, 1, 1, 2026, 4, 6, "1 year, 3 months, 5 days")]
    [InlineData(2024, 5, 31, 2026, 6, 2, "2 years, 2 days")]
    [InlineData(2026, 1, 1, 2026, 2, 1, "1 month")]
    [InlineData(2026, 1, 1, 2026, 2, 2, "1 month, 1 day")]
    [InlineData(2026, 1, 1, 2026, 3, 1, "2 months")]
    [InlineData(2026, 4, 25, 2026, 11, 2, "6 months, 8 days")]
    [InlineData(2026, 3, 1, 2026, 3, 6, "5 days")]
    public void AccountAge_words_a_calendar_span_in_years_months_and_days(
        int fromYear,
        int fromMonth,
        int fromDay,
        int toYear,
        int toMonth,
        int toDay,
        string expected
    ) =>
        LegacyAgeText
            .AccountAge(At(fromYear, fromMonth, fromDay), At(toYear, toMonth, toDay))
            .Should()
            .Be(expected);

    [Fact]
    public void A_start_after_now_reads_as_one_minute_never_a_negative_span()
    {
        LegacyAgeText.FollowAge(At(2026, 6, 1), At(2026, 5, 1)).Should().Be("1 minute");
        LegacyAgeText.AccountAge(At(2026, 6, 1), At(2026, 5, 1)).Should().Be("1 minute");
    }
}
