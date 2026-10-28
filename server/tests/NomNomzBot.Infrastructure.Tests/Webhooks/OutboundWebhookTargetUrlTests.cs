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
using NomNomzBot.Infrastructure.Webhooks;

namespace NomNomzBot.Infrastructure.Tests.Webhooks;

/// <summary>
/// Proves the outbound delivery URL stays pinned to the allowlisted host (webhooks.md H.7). The URL is built as
/// <c>https://{fqdn}{path}</c>, so a hostile path must not be able to introduce a userinfo segment, a second
/// host, a different scheme, or otherwise move the request off the allowlisted FQDN.
/// </summary>
public sealed class OutboundWebhookTargetUrlTests
{
    private const string Fqdn = "hooks.example.com";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("/webhooks/incoming")]
    [InlineData("/path?token=abc&x=1")]
    public void Accepts_a_plain_relative_path_and_keeps_the_allowlisted_host(string? path)
    {
        bool ok = OutboundWebhookTargetUrl.TryBuild(Fqdn, path, out Uri? uri);

        ok.Should().BeTrue();
        uri!.Scheme.Should().Be("https");
        uri.Host.Should().Be(Fqdn);
        uri.UserInfo.Should().BeEmpty();
    }

    [Theory]
    // userinfo authority hijack: https://hooks.example.com@evil.com → host evil.com
    [InlineData("@evil.com/")]
    [InlineData("@evil.com")]
    [InlineData(":pw@evil.com/steal")]
    // a path that is itself an absolute URL would re-anchor the authority
    [InlineData("@evil.com:443/")]
    public void Rejects_any_path_that_moves_the_request_off_the_allowlisted_host(string path)
    {
        bool ok = OutboundWebhookTargetUrl.TryBuild(Fqdn, path, out Uri? uri);

        ok.Should().BeFalse();
        uri.Should().BeNull();
    }

    [Fact]
    public void The_exfiltration_payload_would_otherwise_reach_the_attacker_host()
    {
        // Demonstrates the bug the guard prevents: the naive concatenation parses to the attacker's host.
        Uri naive = new($"https://{Fqdn}@evil.com/collect");
        naive.Host.Should().Be("evil.com");

        // The guard refuses to produce that URL.
        OutboundWebhookTargetUrl.TryBuild(Fqdn, "@evil.com/collect", out _).Should().BeFalse();
    }
}
