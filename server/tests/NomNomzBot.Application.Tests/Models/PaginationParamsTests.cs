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
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Tests.Models;

/// <summary>
/// Proves paging requests are bounded on construction so a client cannot force an unbounded
/// <c>.Take(PageSize)</c> and materialise a huge result set (DoS).
/// </summary>
public class PaginationParamsTests
{
    [Fact]
    public void PageSize_above_the_cap_is_clamped_to_MaxPageSize()
    {
        PaginationParams p = new(Page: 1, PageSize: 1_000_000);

        p.PageSize.Should().Be(PaginationParams.MaxPageSize);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void PageSize_at_or_below_zero_is_clamped_to_one(int requested)
    {
        new PaginationParams(PageSize: requested).PageSize.Should().Be(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Page_below_one_is_clamped_to_one(int requested)
    {
        new PaginationParams(Page: requested).Page.Should().Be(1);
    }

    [Fact]
    public void Valid_values_within_range_pass_through_unchanged()
    {
        PaginationParams p = new(Page: 3, PageSize: 50, SortBy: "createdAt", Order: "desc");

        p.Page.Should().Be(3);
        p.PageSize.Should().Be(50);
        p.SortBy.Should().Be("createdAt");
        p.Order.Should().Be("desc");
    }
}
