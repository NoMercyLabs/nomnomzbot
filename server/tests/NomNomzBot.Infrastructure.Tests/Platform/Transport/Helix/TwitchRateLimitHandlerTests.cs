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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Twitch.Events;
using NomNomzBot.Infrastructure.Platform.Transport.Helix;

namespace NomNomzBot.Infrastructure.Tests.Platform.Transport.Helix;

/// <summary>
/// Proves the rate-limit delegating handler feeds the observed <c>Ratelimit-*</c> headers into the limiter
/// (so the NEXT call genuinely throttles) and that a real 429 hard-blocks the bucket and emits the
/// rate-limited domain event — behaviour, not header parsing alone.
/// </summary>
public class TwitchRateLimitHandlerTests
{
    private const string Bucket = "helix:abc";
    private static readonly DateTimeOffset Origin = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static HttpRequestMessage Request()
    {
        HttpRequestMessage request = new(HttpMethod.Get, "https://api.twitch.tv/helix/users");
        request.Options.Set(HelixRequestOptions.TokenBucketKey, Bucket);
        request.Options.Set(HelixRequestOptions.BroadcasterId, (Guid?)null);
        return request;
    }

    private static HttpClient BuildClient(
        RecordingHelixHandler wire,
        ITwitchRateLimiter limiter,
        CapturingEventBus bus,
        TimeProvider clock
    )
    {
        TwitchRateLimitHandler handler = new(limiter, bus, clock) { InnerHandler = wire };
        return new HttpClient(handler);
    }

    [Fact]
    public async Task SendAsync_ObservedRemainingZero_ThrottlesTheNextAcquire()
    {
        FakeTimeProvider clock = new(Origin);
        TwitchRateLimiter limiter = new(clock);
        CapturingEventBus bus = new();

        // The response says the bucket is now spent, resetting 30s out.
        DateTimeOffset reset = Origin.AddSeconds(30);
        RecordingHelixHandler wire = new([
            () =>
                RecordingHelixHandler.WithRateLimitHeaders(
                    RecordingHelixHandler.Json(HttpStatusCode.OK, "{\"data\":[]}"),
                    limit: 800,
                    remaining: 0,
                    resetsAt: reset
                ),
        ]);
        HttpClient client = BuildClient(wire, limiter, bus, clock);

        await client.SendAsync(Request());

        // The handler must have fed Remaining=0 into the limiter: a fresh acquire on the same bucket now blocks.
        Task<ITwitchRateLease> blocked = limiter.AcquireAsync(
            Bucket,
            TwitchCallPriority.UserInteractive
        );
        clock.Advance(TimeSpan.FromSeconds(29));
        await Task.Yield();
        blocked.IsCompleted.Should().BeFalse();

        clock.Advance(TimeSpan.FromSeconds(1));
        (await blocked.WaitAsync(TimeSpan.FromSeconds(5))).Should().NotBeNull();
    }

    [Fact]
    public async Task SendAsync_HardLimit429_EmitsRateLimitedEventWithHardFlag()
    {
        FakeTimeProvider clock = new(Origin);
        TwitchRateLimiter limiter = new(clock);
        CapturingEventBus bus = new();

        DateTimeOffset reset = Origin.AddSeconds(15);
        RecordingHelixHandler wire = new([
            () =>
                RecordingHelixHandler.WithRateLimitHeaders(
                    RecordingHelixHandler.Json(HttpStatusCode.TooManyRequests, "{}"),
                    limit: 800,
                    remaining: 0,
                    resetsAt: reset
                ),
        ]);
        HttpClient client = BuildClient(wire, limiter, bus, clock);

        HttpResponseMessage response = await client.SendAsync(Request());

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        TwitchHelixRateLimitedEvent evt = bus.EventsOf<TwitchHelixRateLimitedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        evt.WasHardLimited.Should().BeTrue();
        evt.TokenBucketKey.Should().Be(Bucket);
        evt.ResetsAt.Should().Be(reset);
    }

    [Fact]
    public async Task SendAsync_SuccessUnderBudget_EmitsNoEventAndDoesNotThrottle()
    {
        FakeTimeProvider clock = new(Origin);
        TwitchRateLimiter limiter = new(clock);
        CapturingEventBus bus = new();

        RecordingHelixHandler wire = new([
            () =>
                RecordingHelixHandler.WithRateLimitHeaders(
                    RecordingHelixHandler.Json(HttpStatusCode.OK, "{\"data\":[]}"),
                    limit: 800,
                    remaining: 700,
                    resetsAt: Origin.AddSeconds(60)
                ),
        ]);
        HttpClient client = BuildClient(wire, limiter, bus, clock);

        await client.SendAsync(Request());

        bus.EventsOf<TwitchHelixRateLimitedEvent>().Should().BeEmpty();
        // Budget remains, so a follow-up acquire is immediate.
        ITwitchRateLease lease = await limiter
            .AcquireAsync(Bucket, TwitchCallPriority.Background)
            .WaitAsync(TimeSpan.FromSeconds(5));
        lease.Should().NotBeNull();
    }
}
