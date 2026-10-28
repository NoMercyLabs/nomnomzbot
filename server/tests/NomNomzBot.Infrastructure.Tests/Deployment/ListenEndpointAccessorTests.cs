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
using NomNomzBot.Infrastructure.Platform.Deployment;

namespace NomNomzBot.Infrastructure.Tests.Deployment;

/// <summary>
/// Proves the bound-port carrier the API host publishes for the mDNS advertiser (deployment-distribution §6) is
/// fail-closed: reading the port before it is set throws, and an out-of-range port is rejected, so the advertiser
/// can never announce a placeholder or invalid port.
/// </summary>
public sealed class ListenEndpointAccessorTests
{
    [Fact]
    public void Is_unresolved_until_a_port_is_set()
    {
        ListenEndpointAccessor accessor = new();

        accessor.IsResolved.Should().BeFalse();
        accessor.Invoking(a => a.Port).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Set_port_makes_it_resolved_and_readable()
    {
        ListenEndpointAccessor accessor = new();

        accessor.SetPort(51234);

        accessor.IsResolved.Should().BeTrue();
        accessor.Port.Should().Be(51234);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(70000)]
    public void Rejects_an_out_of_range_port(int port)
    {
        ListenEndpointAccessor accessor = new();

        accessor.Invoking(a => a.SetPort(port)).Should().Throw<ArgumentOutOfRangeException>();
    }
}
