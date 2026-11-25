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
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Infrastructure.Tts;
using Xunit;

namespace NomNomzBot.Infrastructure.Tests.Tts;

/// <summary>
/// The budget migration filled the columns of every existing config with an empty string. Reading one
/// threw, so every speech request of that channel failed (a code command that speaks faulted).
/// </summary>
public sealed class TtsBudgetPlanParseTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_config_saved_before_budgets_existed_has_no_budgets_and_no_fallback(string stored)
    {
        TtsBudgetPlan.Parse(stored).Should().BeEmpty();
        TtsBudgetPlan.ParseFallback(stored).Should().BeEmpty();
    }

    [Fact]
    public void A_saved_fallback_order_reads_back_in_order()
    {
        IReadOnlyList<string> fallback = TtsBudgetPlan.ParseFallback("[\"edge\",\"azure\"]");

        fallback.Should().Equal("edge", "azure");
    }
}
