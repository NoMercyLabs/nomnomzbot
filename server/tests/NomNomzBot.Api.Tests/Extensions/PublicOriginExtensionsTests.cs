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
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using NomNomzBot.Api.Extensions;

namespace NomNomzBot.Api.Tests.Extensions;

/// <summary>
/// Proves the one shared public-origin resolver (deployment-distribution §6) that every OAuth redirect_uri and
/// the credential-card "register this URL" copy derive from: an explicit non-loopback <c>App:BaseUrl</c> wins;
/// otherwise the forwarded request origin (the tunnel/domain the dashboard was served from); otherwise the
/// loopback fallback. The localhost value <c>ListenPortBootstrap</c> auto-writes must NOT override a real
/// forwarded host — that exact override was the redirect_uri bug.
/// </summary>
public sealed class PublicOriginExtensionsTests
{
    [Fact]
    public void ForwardedTunnelHost_Wins_OverAutoSetLoopbackBaseUrl()
    {
        // ListenPortBootstrap auto-wrote App:BaseUrl=http://localhost:5080 (loopback ⇒ "not explicit").
        IConfiguration config = Config("http://localhost:5080");
        HttpRequest request = RequestWithForwarded("bot-dev.nomercy.tv", "https");

        string origin = request.ResolvePublicOrigin(config);

        origin.Should().Be("https://bot-dev.nomercy.tv");
    }

    [Fact]
    public void PlainLocalhostRequest_ResolvesToLoopbackOrigin()
    {
        IConfiguration config = Config("http://localhost:5080");
        HttpRequest request = DirectRequest("localhost:5080", "http");

        string origin = request.ResolvePublicOrigin(config);

        origin.Should().Be("http://localhost:5080");
    }

    [Fact]
    public void ExplicitExternalBaseUrl_Wins_OverForwardedHost()
    {
        // A deliberately-configured public URL owns the redirect — even a forwarded host cannot move it.
        IConfiguration config = Config("https://api.nomnomz.bot");
        HttpRequest request = RequestWithForwarded("bot-dev.nomercy.tv", "https");

        string origin = request.ResolvePublicOrigin(config);

        origin.Should().Be("https://api.nomnomz.bot");
    }

    [Fact]
    public void ExplicitBaseUrl_IsTrimmedOfTrailingSlash()
    {
        IConfiguration config = Config("https://api.nomnomz.bot/");
        HttpRequest request = DirectRequest("localhost:5080", "http");

        request.ResolvePublicOrigin(config).Should().Be("https://api.nomnomz.bot");
    }

    [Fact]
    public void NoBaseUrl_NoForwarded_UsesDirectRequestOrigin()
    {
        IConfiguration config = Config(baseUrl: null);
        HttpRequest request = DirectRequest("lan-box.local:5080", "http");

        request.ResolvePublicOrigin(config).Should().Be("http://lan-box.local:5080");
    }

    // ─── scaffolding ───────────────────────────────────────────────────────────

    private static IConfiguration Config(string? baseUrl) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["App:BaseUrl"] = baseUrl })
            .Build();

    /// <summary>A request that arrived through a reverse proxy / tunnel: the framework binds loopback, but the proxy forwarded the real public host.</summary>
    private static HttpRequest RequestWithForwarded(string host, string proto)
    {
        DefaultHttpContext context = new();
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost", 5080);
        context.Request.Headers["X-Forwarded-Host"] = host;
        context.Request.Headers["X-Forwarded-Proto"] = proto;
        return context.Request;
    }

    /// <summary>A direct same-host request with no forwarded headers.</summary>
    private static HttpRequest DirectRequest(string host, string scheme)
    {
        DefaultHttpContext context = new();
        context.Request.Scheme = scheme;
        context.Request.Host = new HostString(host);
        return context.Request;
    }
}
