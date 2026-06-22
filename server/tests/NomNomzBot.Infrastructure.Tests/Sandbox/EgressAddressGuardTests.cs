// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net;
using FluentAssertions;
using NomNomzBot.Infrastructure.Sandbox;

namespace NomNomzBot.Infrastructure.Tests.Sandbox;

/// <summary>
/// Proves the SSRF resolved-IP guard (code-execution-sandbox.md §7.1 step 4) — the gate every egress IP must pass.
/// Blocks loopback / private / link-local / cloud-metadata / CGNAT / ULA / unspecified addresses, including the
/// IPv4-mapped-IPv6 smuggling form; allows genuinely public addresses (with boundary cases just outside each
/// blocked range).
/// </summary>
public sealed class EgressAddressGuardTests
{
    [Theory]
    [InlineData("127.0.0.1")] // loopback
    [InlineData("10.0.0.5")] // private
    [InlineData("172.16.0.1")] // private (lower edge)
    [InlineData("172.31.255.254")] // private (upper edge)
    [InlineData("192.168.1.1")] // private
    [InlineData("169.254.169.254")] // cloud metadata
    [InlineData("100.64.0.1")] // CGNAT
    [InlineData("0.0.0.0")] // this-network
    [InlineData("255.255.255.255")] // broadcast
    [InlineData("::1")] // IPv6 loopback
    [InlineData("fc00::1")] // unique-local
    [InlineData("fd12:3456::1")] // unique-local
    [InlineData("fe80::1")] // link-local
    [InlineData("::ffff:169.254.169.254")] // v4-mapped metadata bypass attempt
    [InlineData("::ffff:10.0.0.1")] // v4-mapped private bypass attempt
    public void Blocks_internal_and_metadata_addresses(string ip)
    {
        EgressAddressGuard.IsBlocked(IPAddress.Parse(ip)).Should().BeTrue();
    }

    [Theory]
    [InlineData("8.8.8.8")] // Google DNS
    [InlineData("1.1.1.1")] // Cloudflare DNS
    [InlineData("93.184.216.34")] // example.com
    [InlineData("2606:4700:4700::1111")] // Cloudflare IPv6
    [InlineData("172.15.0.1")] // just below 172.16.0.0/12
    [InlineData("172.32.0.1")] // just above 172.16.0.0/12
    [InlineData("100.63.255.255")] // just below 100.64.0.0/10 CGNAT
    [InlineData("100.128.0.1")] // just above 100.64.0.0/10 CGNAT
    public void Allows_genuinely_public_addresses(string ip)
    {
        EgressAddressGuard.IsBlocked(IPAddress.Parse(ip)).Should().BeFalse();
    }
}
