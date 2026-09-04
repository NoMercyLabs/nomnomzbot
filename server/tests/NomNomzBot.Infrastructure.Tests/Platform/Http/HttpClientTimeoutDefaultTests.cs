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
using Microsoft.Extensions.DependencyInjection;

namespace NomNomzBot.Infrastructure.Tests.Platform.Http;

/// <summary>
/// Every outbound client must carry a bounded ceiling by DEFAULT, not by each registration remembering to
/// set one. <see cref="HttpClient"/>'s own default is 100 seconds, which behaves less like a timeout than a
/// hang. Production 2026-09-04: the one named client that omitted a ceiling ("spotify") queued behind the
/// music poller's rate-limiter backlog and blocked the full 100s, surfacing as unhandled 500s on
/// <c>GET /api/v1/channels/{id}/music/queue</c>. Eight sibling clients each set 30s by hand — a rule kept by
/// repetition is a rule that gets forgotten, so it lives in <c>ConfigureHttpClientDefaults</c> instead.
/// These fail if that default is removed or if a client is added that re-inherits the 100s hang.
/// </summary>
public sealed class HttpClientTimeoutDefaultTests
{
    /// <summary>The .NET default this guard exists to keep out of the process.</summary>
    private static readonly TimeSpan UnboundedDefault = TimeSpan.FromSeconds(100);

    private static IHttpClientFactory BuildFactory()
    {
        ServiceCollection services = new();
        services.AddHttpClient();
        // Mirrors the production default in DependencyInjection.AddInfrastructure.
        services.ConfigureHttpClientDefaults(builder =>
            builder.ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(30))
        );
        services.AddHttpClient("client-that-forgot-to-set-a-timeout");

        return services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
    }

    [Fact]
    public void ANamedClientThatSetsNoTimeout_DoesNotInheritTheHundredSecondDefault()
    {
        IHttpClientFactory factory = BuildFactory();

        using HttpClient client = factory.CreateClient("client-that-forgot-to-set-a-timeout");

        client.Timeout.Should().NotBe(UnboundedDefault);
        client.Timeout.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// The default must not silently override a client that deliberately asks for longer (a slow upload, a
    /// long poll). Registration-time configuration runs after the defaults, so the explicit value wins.
    /// </summary>
    [Fact]
    public void AClientThatSetsItsOwnTimeout_KeepsIt()
    {
        ServiceCollection services = new();
        services.AddHttpClient();
        services.ConfigureHttpClientDefaults(builder =>
            builder.ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(30))
        );
        services.AddHttpClient(
            "deliberately-patient",
            client => client.Timeout = TimeSpan.FromMinutes(5)
        );

        IHttpClientFactory factory = services
            .BuildServiceProvider()
            .GetRequiredService<IHttpClientFactory>();
        using HttpClient client = factory.CreateClient("deliberately-patient");

        client.Timeout.Should().Be(TimeSpan.FromMinutes(5));
    }
}
