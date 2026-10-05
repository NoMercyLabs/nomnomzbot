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
using NomNomzBot.Application.Commands.Builtin;

namespace NomNomzBot.Infrastructure.Tests.Commands.Builtins;

/// <summary>
/// The duration wording of the old bot's <c>!accountage</c> and <c>!followage</c> (legacy AccountAge.cs and
/// Followage.cs FormatDuration), pinned with the exact strings the old bot produced.
/// </summary>
public sealed class LegacyAgeTextTests
{
    [Theory]
    [InlineData(365, 0, "1 year")]
    [InlineData(400, 0, "1 year, 1 month, 5 days")]
    [InlineData(731, 0, "2 years, 1 day")]
    [InlineData(31, 0, "1 month, 1 day")]
    [InlineData(45, 0, "1 month, 15 days")]
    [InlineData(60, 0, "2 months")]
    [InlineData(1, 0, "1 day")]
    [InlineData(5, 0, "5 days")]
    [InlineData(0, 60, "1 hour")]
    [InlineData(0, 180, "3 hours")]
    [InlineData(0, 30, "30 minutes")]
    [InlineData(0, 0, "1 minute")]
    public void AccountAge_words_a_span_the_way_the_old_bot_did(
        int days,
        int minutes,
        string expected
    ) => LegacyAgeText.AccountAge(new TimeSpan(days, 0, minutes, 0)).Should().Be(expected);

    [Theory]
    [InlineData(365, 0, "1 year")]
    [InlineData(400, 0, "1 year and 35 days")]
    [InlineData(800, 0, "2 years and 70 days")]
    [InlineData(30, 0, "1 month")]
    [InlineData(31, 0, "1 month and 1 day")]
    [InlineData(45, 0, "1 month and 15 days")]
    [InlineData(1, 0, "1 day")]
    [InlineData(5, 0, "5 days")]
    [InlineData(0, 60, "1 hour")]
    [InlineData(0, 300, "5 hours")]
    [InlineData(0, 10, "10 minutes")]
    [InlineData(0, 0, "1 minute")]
    public void FollowAge_words_a_span_the_way_the_old_bot_did(
        int days,
        int minutes,
        string expected
    ) => LegacyAgeText.FollowAge(new TimeSpan(days, 0, minutes, 0)).Should().Be(expected);
}
