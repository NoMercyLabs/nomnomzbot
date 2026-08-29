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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Infrastructure.Chat.Kick;
using NomNomzBot.Infrastructure.Platform.Resilience;

namespace NomNomzBot.Infrastructure.Tests.Chat.Kick;

/// <summary>
/// Proves the "kick" named <see cref="HttpClient"/> carries the SAME resilience shape as its Spotify/Discord
/// siblings (<see cref="ResiliencePolicies.AddKickResilienceHandler"/>): a transient 5xx/429 is retried with
/// backoff rather than failing the call on the first attempt. Exercises the REAL production registration
/// (<c>AddHttpClient("kick").AddKickResilienceHandler()</c>, mirrored from <c>DependencyInjection.cs</c>) over a
/// counting handler, so a regression that drops the resilience wiring fails this test.
/// </summary>
public sealed class KickApiClientResilienceTests
{
    /// <summary>Records every attempt and returns a scripted sequence of responses — proves an actual
    /// RETRY happened (attempt count &gt; 1), not merely that the call "eventually succeeded".</summary>
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> _script;
        public int AttemptCount { get; private set; }

        public ScriptedHandler(params HttpStatusCode[] script) => _script = new(script);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            AttemptCount++;
            HttpStatusCode status = _script.Count > 0 ? _script.Dequeue() : HttpStatusCode.OK;
            HttpResponseMessage response = new(status);
            // A 200 needs a body the client can deserialize — SendMessageAsync reads `data.message_id`.
            if (status == HttpStatusCode.OK)
                response.Content = new StringContent(
                    """{"data":{"message_id":"m1"}}""",
                    System.Text.Encoding.UTF8,
                    "application/json"
                );
            return Task.FromResult(response);
        }
    }

    private static KickApiClient NewClient(HttpMessageHandler handler)
    {
        ServiceCollection services = new();
        // The REAL production registration (DependencyInjection.cs): named "kick" client + the Kick
        // resilience handler, over the test's scripted primary handler.
        services
            .AddHttpClient("kick")
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddKickResilienceHandler();
        ServiceProvider provider = services.BuildServiceProvider();
        IHttpClientFactory factory = provider.GetRequiredService<IHttpClientFactory>();
        return new(factory, NullLogger<KickApiClient>.Instance);
    }

    [Fact]
    public async Task A_transient_503_is_retried_not_failed_immediately()
    {
        ScriptedHandler handler = new(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        KickApiClient client = NewClient(handler);

        Result<string> result = await client.SendMessageAsync("token", 12345, "hello");

        handler
            .AttemptCount.Should()
            .Be(
                2,
                "the first 503 must be retried by the resilience pipeline, not surfaced as an immediate failure"
            );
        result.IsSuccess.Should().BeTrue("the retry's second attempt succeeded");
    }

    [Fact]
    public async Task A_transient_429_is_retried_not_failed_immediately()
    {
        ScriptedHandler handler = new(HttpStatusCode.TooManyRequests, HttpStatusCode.OK);
        KickApiClient client = NewClient(handler);

        Result<string> result = await client.SendMessageAsync("token", 12345, "hello");

        handler
            .AttemptCount.Should()
            .Be(2, "a 429 must back off and retry, not fail on the first attempt");
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task A_persistent_503_exhausts_retries_then_fails_as_a_result_not_an_exception()
    {
        // 1 initial attempt + 2 retries (MaxRetryAttempts=2) = 3 attempts total, all 503.
        ScriptedHandler handler = new(
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.ServiceUnavailable
        );
        KickApiClient client = NewClient(handler);

        Result<string> result = await client.SendMessageAsync("token", 12345, "hello");

        handler
            .AttemptCount.Should()
            .Be(3, "the pipeline retries exactly MaxRetryAttempts times, then gives up");
        result
            .IsFailure.Should()
            .BeTrue("exhausted retries degrade to a typed Result, never an unhandled throw");
    }
}
