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
using NomNomzBot.Api.Authorization;

namespace NomNomzBot.Api.Tests.Authorization;

/// <summary>
/// Proves the OAuth open-redirect boundary (§5): only blank, the app scheme, and RFC-8252 loopback callbacks may
/// receive the post-auth tokens; every arbitrary host, host-spoof, and non-http loopback is rejected.
/// </summary>
public class ClientRedirectPolicyTests
{
    [Theory]
    [InlineData(null)] // blank → the web flow (no redirect)
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nomnomzbot://callback")] // mobile deep-link (app's own scheme)
    [InlineData("NOMNOMZBOT://callback")] // scheme match is case-insensitive
    [InlineData("http://127.0.0.1:5173/cb")] // desktop RFC-8252 loopback, ephemeral port
    [InlineData("http://localhost:54321/cb")]
    [InlineData("http://[::1]:8080/cb")] // IPv6 loopback
    [InlineData("http://127.0.0.1/cb")] // loopback without an explicit port
    public void Allows_blank_app_scheme_and_http_loopback(string? redirectUri) =>
        ClientRedirectPolicy.IsAllowed(redirectUri).Should().BeTrue();

    [Theory]
    [InlineData("http://evil.com/cb")] // arbitrary host
    [InlineData("https://evil.com/cb")]
    [InlineData("http://127.0.0.1.evil.com/cb")] // spoof — parses to a non-loopback DNS host
    [InlineData("http://localhost.evil.com/cb")] // spoof
    [InlineData("https://127.0.0.1:5000/cb")] // loopback but https — only http loopback is the RFC-8252 exception
    [InlineData("http://169.254.169.254/latest")] // cloud metadata endpoint, not loopback
    [InlineData("http://10.0.0.5/cb")] // private LAN, not loopback
    [InlineData("http://0.0.0.0/cb")] // unspecified address, not loopback
    [InlineData("ftp://127.0.0.1/cb")] // non-http scheme on loopback
    [InlineData("javascript:alert(1)")]
    [InlineData("not a uri")]
    public void Rejects_arbitrary_hosts_spoofs_and_non_http_loopback(string redirectUri) =>
        ClientRedirectPolicy.IsAllowed(redirectUri).Should().BeFalse();
}
