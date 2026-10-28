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

using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Resilience;

namespace NomNomzBot.Infrastructure.Tests.Platform.Transport.Helix;

/// <summary>
/// Proves the real Polly pipeline on the named <c>twitch-helix</c> client retries a transient 5xx but does
/// NOT retry client errors (400) or 429 (owned by the rate-limit handler) — asserted by the number of times
/// the wire handler is actually hit.
/// </summary>
public class TwitchResilienceTests
{
    private static (HttpClient Client, RecordingHelixHandler Wire) BuildClient(
        IEnumerable<Func<HttpResponseMessage>> responses
    )
    {
        RecordingHelixHandler wire = new(responses);

        ServiceCollection services = new();
        services.AddSingleton<IEventBus, CapturingEventBus>();
        services
            .AddHttpClient("twitch-helix")
            .AddTwitchResilienceHandler()
            .ConfigurePrimaryHttpMessageHandler(() => wire);

        ServiceProvider provider = services.BuildServiceProvider();
        HttpClient client = provider
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient("twitch-helix");
        return (client, wire);
    }

    [Fact]
    public async Task Pipeline_RetriesTransient5xx_ThenSucceeds()
    {
        // First two attempts 503, third 200 — the retry strategy must reach the success.
        (HttpClient client, RecordingHelixHandler wire) = BuildClient([
            () => new(HttpStatusCode.ServiceUnavailable),
            () => new(HttpStatusCode.ServiceUnavailable),
            () => RecordingHelixHandler.Json(HttpStatusCode.OK, "{\"data\":[]}"),
        ]);

        HttpResponseMessage response = await client.GetAsync("https://api.twitch.tv/helix/users");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        wire.CallCount.Should().Be(3); // 1 original + 2 retries
    }

    [Fact]
    public async Task Pipeline_DoesNotRetry_ClientError400()
    {
        (HttpClient client, RecordingHelixHandler wire) = BuildClient([
            () => new(HttpStatusCode.BadRequest),
        ]);

        HttpResponseMessage response = await client.GetAsync("https://api.twitch.tv/helix/users");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        wire.CallCount.Should().Be(1); // no retry on 4xx
    }

    [Fact]
    public async Task Pipeline_DoesNotRetry_429()
    {
        // 429 is the rate-limit handler's job (honour Ratelimit-Reset), never a blind resilience retry.
        (HttpClient client, RecordingHelixHandler wire) = BuildClient([
            () => new(HttpStatusCode.TooManyRequests),
        ]);

        HttpResponseMessage response = await client.GetAsync("https://api.twitch.tv/helix/users");

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        wire.CallCount.Should().Be(1);
    }
}
