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
using NomNomzBot.Api.Controllers.V1;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves the home screen's supporter-money number is honest: a single-currency day sums to its minor-unit
/// total; a MIXED-currency day reports null (a cross-currency sum is meaningless, and the event count still
/// shows); amount-less events (a TreatStream treat) never invent money and don't block the total of the
/// events that do carry amounts.
/// </summary>
public sealed class DashboardStatsAggregationTests
{
    [Fact]
    public void SingleCurrencyDay_SumsToItsMinorUnitTotal()
    {
        (long? amount, string? currency) = DashboardController.AggregateSupporterAmounts([
            (500, "USD"),
            (1337, "usd"),
            (null, null),
        ]);

        amount.Should().Be(1837, "500 + 1337 minor units, case-insensitive same currency");
        currency.Should().Be("USD");
    }

    [Fact]
    public void MixedCurrencyDay_ReportsNull_NeverACrossCurrencySum()
    {
        (long? amount, string? currency) = DashboardController.AggregateSupporterAmounts([
            (500, "USD"),
            (500, "EUR"),
        ]);

        amount.Should().BeNull();
        currency.Should().BeNull();
    }

    [Fact]
    public void AmountlessDay_ReportsNull_NeverZeroMoney()
    {
        // e.g. only TreatStream treats today — items, not money.
        (long? amount, string? currency) = DashboardController.AggregateSupporterAmounts([
            (null, null),
            (null, null),
        ]);

        amount.Should().BeNull();
        currency.Should().BeNull();
    }

    [Fact]
    public void EmptyDay_ReportsNull()
    {
        (long? amount, string? currency) = DashboardController.AggregateSupporterAmounts([]);

        amount.Should().BeNull();
        currency.Should().BeNull();
    }
}
