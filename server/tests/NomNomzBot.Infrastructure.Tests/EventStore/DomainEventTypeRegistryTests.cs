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
