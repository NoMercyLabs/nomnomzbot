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
using NomNomzBot.Domain.Community.Events;
using NomNomzBot.Domain.Rewards.Events;
using NomNomzBot.Infrastructure.EventStore;

namespace NomNomzBot.Infrastructure.Tests.EventStore;

/// <summary>
/// Proves the replay type registry resolves real domain event names to their CLR type and, critically, that
/// an unrecognized name is a clean miss (null) rather than any kind of dynamic type resolution — a crafted
/// import file can only ever name something in this closed, pre-built set or get nothing back.
/// </summary>
public sealed class DomainEventTypeRegistryTests
{
    [Fact]
    public void Resolve_KnownDomainEventTypeName_ReturnsItsClrType()
    {
        DomainEventTypeRegistry registry = new();

        registry.Resolve(nameof(FollowEvent)).Should().Be(typeof(FollowEvent));
        registry.Resolve(nameof(RewardRedeemedEvent)).Should().Be(typeof(RewardRedeemedEvent));
    }

    [Fact]
    public void Resolve_UnknownName_ReturnsNullRatherThanResolvingAnyType()
    {
        DomainEventTypeRegistry registry = new();

        registry.Resolve("NotARealEventType").Should().BeNull();
        registry
            .Resolve("System.Diagnostics.Process, System.Diagnostics.Process")
            .Should()
            .BeNull();
    }
}
