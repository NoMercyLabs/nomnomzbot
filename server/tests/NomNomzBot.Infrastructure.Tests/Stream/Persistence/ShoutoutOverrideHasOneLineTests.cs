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

using System.Reflection;
using FluentAssertions;
using NomNomzBot.Application.Community.Dtos;
using NomNomzBot.Domain.Stream.Entities;

namespace NomNomzBot.Infrastructure.Tests.Stream.Persistence;

/// <summary>
/// A person has ONE custom shoutout line per channel. The raid path reuses that line (the same lookup in
/// ShoutoutSender), so no entity, DTO or constant may carry a second "kind" of line.
/// </summary>
public sealed class ShoutoutOverrideHasOneLineTests
{
    [Fact]
    public void The_override_row_carries_no_kind_column()
    {
        typeof(ShoutoutOverride).GetProperty("Kind").Should().BeNull();
    }

    [Fact]
    public void There_is_no_closed_set_of_override_kinds()
    {
        typeof(ShoutoutOverride)
            .Assembly.GetTypes()
            .Where(t => t.Name == "ShoutoutOverrideKinds")
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void The_viewer_profile_exposes_one_shoutout_line_and_no_raid_line()
    {
        PropertyInfo[] props = typeof(ViewerOverridesDto).GetProperties();

        props.Select(p => p.Name).Should().Contain("ShoutoutMessageTemplate");
        props.Select(p => p.Name).Should().NotContain("RaidMessageTemplate");
    }
}
