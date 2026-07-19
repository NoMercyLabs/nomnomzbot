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
using NomNomzBot.Api.Models;

namespace NomNomzBot.Api.Tests.Models;

/// <summary>
/// Proves <c>pageSize</c> and <c>Take</c> are the same value on <see cref="PageRequestDto"/>. The whole
/// dashboard sends <c>?page=&amp;pageSize=</c> (the REST convention); before the alias, <c>pageSize</c> bound to
/// nothing and <c>Take</c> silently fell back to 25, so every list was capped at 25 rows. Model binding maps a
/// query key to the property of the same name (case-insensitive), so aliasing the property aliases the binding.
/// </summary>
public sealed class PageRequestDtoTests
{
    [Fact]
    public void PageSize_sets_Take()
    {
        PageRequestDto request = new() { PageSize = 100 };

        request.Take.Should().Be(100);
    }

    [Fact]
    public void Take_sets_PageSize()
    {
        PageRequestDto request = new() { Take = 50 };

        request.PageSize.Should().Be(50);
    }

    [Fact]
    public void Default_page_size_is_25()
    {
        PageRequestDto request = new();

        request.Take.Should().Be(25);
        request.PageSize.Should().Be(25);
        request.Page.Should().Be(1);
    }
}
