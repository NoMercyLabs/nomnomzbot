// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Claims;
using System.Threading.RateLimiting;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using NomNomzBot.Api.RateLimiting;
using Xunit;

namespace NomNomzBot.Api.Tests.RateLimiting;

/// <summary>
/// S114 — proves the tiered rate limiters actually behave differently per task type, through a real
/// <see cref="PartitionedRateLimiter{TResource}"/> exercising each tier's own partitioning function (the
/// same functions <c>RateLimitPolicyRegistration</c> wires into the real middleware), not attribute
/// reflection alone. There is no WebApplicationFactory in this test project, so this mirrors the existing
/// <c>SecuritySensitiveRateLimitPolicyTests</c> pattern.
/// </summary>
public sealed class RateLimitTierTests
{
    private static HttpContext ContextForUser(string userId)
    {
        DefaultHttpContext context = new();
        context.User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "test")
        );
        return context;
    }

    private static HttpContext ContextForIp(string ip)
    {
        DefaultHttpContext context = new();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(ip);
        return context;
    }

    [Fact]
    public async Task WriteCheap_Allows50TogglesInAMinuteByOneUser_NoneRejected()
    {
        using PartitionedRateLimiter<HttpContext> limiter = PartitionedRateLimiter.Create<
            HttpContext,
            string
        >(WriteCheapRateLimitPolicy.Partition);

        HttpContext Context() => ContextForUser("toggle-happy-streamer");

        List<bool> acquired = [];
        for (int i = 0; i < 50; i++)
        {
            using RateLimitLease lease = await limiter.AcquireAsync(Context());
            acquired.Add(lease.IsAcquired);
        }

        acquired
            .Should()
            .AllSatisfy(
                x => x.Should().BeTrue(),
                "toggling 50 cheap config switches in a minute must never 429 — the owner's actual complaint"
            );
    }

    [Fact]
    public async Task WriteCheap_DoesNotContendWithRead_EvenWhenBothBucketsAreNearFull()
    {
        // The bug this slice fixes: a single "api" bucket meant a caller's background dashboard polling
        // (reads) competed with their own cheap toggles (writes) for the same budget. Proving the two
        // tiers are SEPARATE partitions: exhausting "read" for a caller must not affect "write-cheap" for
        // the same caller.
        using PartitionedRateLimiter<HttpContext> readLimiter = PartitionedRateLimiter.Create<
            HttpContext,
            string
        >(ReadRateLimitPolicy.Partition);
        using PartitionedRateLimiter<HttpContext> writeCheapLimiter = PartitionedRateLimiter.Create<
            HttpContext,
            string
        >(WriteCheapRateLimitPolicy.Partition);

        HttpContext Context() => ContextForUser("polling-and-toggling-streamer");

        for (int i = 0; i < ReadRateLimitPolicy.PermitLimit; i++)
        {
            using RateLimitLease lease = await readLimiter.AcquireAsync(Context());
            lease.IsAcquired.Should().BeTrue();
        }

        // The read bucket for this caller is now exhausted...
        using RateLimitLease exhaustedRead = await readLimiter.AcquireAsync(Context());
        exhaustedRead.IsAcquired.Should().BeFalse();

        // ...but the write-cheap bucket for the SAME caller is untouched — separate partitions.
        using RateLimitLease stillOpenWrite = await writeCheapLimiter.AcquireAsync(Context());
        stillOpenWrite.IsAcquired.Should().BeTrue();
    }

    [Fact]
    public async Task Auth_Rejects50LoginAttemptsInAMinuteFromOneIp()
    {
        using PartitionedRateLimiter<HttpContext> limiter = PartitionedRateLimiter.Create<
            HttpContext,
            string
        >(AuthRateLimitPolicy.Partition);

        HttpContext Context() => ContextForIp("203.0.113.7");

        List<bool> acquired = [];
        for (int i = 0; i < 50; i++)
        {
            using RateLimitLease lease = await limiter.AcquireAsync(Context());
            acquired.Add(lease.IsAcquired);
        }

        acquired
            .Count(x => x)
            .Should()
            .Be(
                AuthRateLimitPolicy.PermitLimit,
                "only the strict per-minute budget of login attempts may succeed"
            );
        acquired
            .Count(x => !x)
            .Should()
            .Be(
                50 - AuthRateLimitPolicy.PermitLimit,
                "brute-forcing login must be rejected well before 50 attempts"
            );
    }

    [Fact]
    public async Task SecuritySensitive_RejectsDestructiveAdminActionWellBefore50Calls()
    {
        using PartitionedRateLimiter<HttpContext> limiter = PartitionedRateLimiter.Create<
            HttpContext,
            string
        >(SecuritySensitiveRateLimitPolicy.Partition);

        HttpContext Context() => ContextForUser("platform-admin-under-test");

        int rejectedBefore50 = 0;
        for (int i = 0; i < 50; i++)
        {
            using RateLimitLease lease = await limiter.AcquireAsync(Context());
            if (!lease.IsAcquired)
                rejectedBefore50++;
        }

        rejectedBefore50
            .Should()
            .BeGreaterThan(
                0,
                "a destructive admin action (impersonate/suspend/deactivate/refund/erasure) must hit its "
                    + "3/min limit long before 50 calls in the same minute"
            );
        rejectedBefore50.Should().Be(50 - SecuritySensitiveRateLimitPolicy.PermitLimit);
    }

    [Fact]
    public async Task WriteExpensive_PartitionsByChannelNotByCaller()
    {
        using PartitionedRateLimiter<HttpContext> limiter = PartitionedRateLimiter.Create<
            HttpContext,
            string
        >(WriteExpensiveRateLimitPolicy.Partition);

        HttpContext ContextForChannel(string channelId)
        {
            DefaultHttpContext context = new();
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "moderator-of-two-channels")],
                    "test"
                )
            );
            context.Request.Headers["X-Channel-Id"] = channelId;
            return context;
        }

        for (int i = 0; i < WriteExpensiveRateLimitPolicy.PermitLimit; i++)
        {
            using RateLimitLease lease = await limiter.AcquireAsync(ContextForChannel("channel-a"));
            lease.IsAcquired.Should().BeTrue();
        }

        using RateLimitLease exhausted = await limiter.AcquireAsync(ContextForChannel("channel-a"));
        exhausted.IsAcquired.Should().BeFalse("channel-a's expensive-write budget is now spent");

        // The SAME caller acting on a DIFFERENT channel is a separate partition and is unaffected.
        using RateLimitLease otherChannel = await limiter.AcquireAsync(
            ContextForChannel("channel-b")
        );
        otherChannel.IsAcquired.Should().BeTrue();
    }

    [Fact]
    public async Task OnRejected_StampsRetryAfterHeader()
    {
        Microsoft.AspNetCore.RateLimiting.RateLimiterOptions options = new();
        options.AddNomNomzRateLimitPolicies();

        DefaultHttpContext httpContext = new();
        RateLimitLease lease = new FixedRateLimitLeaseWithRetryAfter(TimeSpan.FromSeconds(42));
        OnRejectedContext rejectedContext = new() { HttpContext = httpContext, Lease = lease };

        await options.OnRejected!(rejectedContext, CancellationToken.None);

        httpContext.Response.Headers.RetryAfter.ToString().Should().Be("42");
    }

    /// <summary>Minimal fake lease exposing only a Retry-After metadata value, for the OnRejected test above.</summary>
    private sealed class FixedRateLimitLeaseWithRetryAfter(TimeSpan retryAfter) : RateLimitLease
    {
        public override bool IsAcquired => false;

        public override IEnumerable<string> MetadataNames => [MetadataName.RetryAfter.Name];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            if (metadataName == MetadataName.RetryAfter.Name)
            {
                metadata = retryAfter;
                return true;
            }

            metadata = null;
            return false;
        }
    }
}
