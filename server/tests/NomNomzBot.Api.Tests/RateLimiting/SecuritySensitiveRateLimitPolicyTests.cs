// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Reflection;
using System.Security.Claims;
using System.Threading.RateLimiting;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.RateLimiting;

namespace NomNomzBot.Api.Tests.RateLimiting;

/// <summary>
/// Proves the KEK-rotation admin action (S098e follow-up) carries its OWN strict rate-limit policy rather
/// than relying on <c>BaseController</c>'s general 120/min "api" policy — an operation that walks and
/// re-wraps every stored DEK must not be spammable even by an authenticated platform admin. There is no
/// WebApplicationFactory in this test project (every other controller test drives the action directly), so
/// the rejection behavior is proven by running the SAME partitioning function the real middleware uses
/// through a real <see cref="PartitionedRateLimiter{TResource}"/> — request N+1 for one caller is rejected.
/// </summary>
public sealed class SecuritySensitiveRateLimitPolicyTests
{
    [Fact]
    public void RotateEncryptionKeyAction_CarriesItsOwnEnableRateLimitingAttribute()
    {
        MethodInfo action = typeof(AdminController).GetMethod(
            nameof(AdminController.RotateEncryptionKey)
        )!;

        EnableRateLimitingAttribute? attribute =
            action.GetCustomAttribute<EnableRateLimitingAttribute>(inherit: false);

        attribute.Should().NotBeNull("a KEK-material action must declare its own, tighter policy");
        attribute!.PolicyName.Should().Be(SecuritySensitiveRateLimitPolicy.PolicyName);
        SecuritySensitiveRateLimitPolicy
            .PolicyName.Should()
            .NotBe(
                "api",
                "the inherited BaseController "
                    + "policy (120/min) is far too loose for an operation that re-wraps every stored DEK"
            );
    }

    [Fact]
    public async Task Partition_RejectsTheCallAfterThePermitLimit_ForTheSameCaller()
    {
        using PartitionedRateLimiter<HttpContext> limiter = PartitionedRateLimiter.Create<
            HttpContext,
            string
        >(SecuritySensitiveRateLimitPolicy.Partition);

        HttpContext ContextForAdmin()
        {
            DefaultHttpContext context = new();
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "admin-under-test")],
                    "test"
                )
            );
            return context;
        }

        List<bool> acquired = [];
        for (int i = 0; i < SecuritySensitiveRateLimitPolicy.PermitLimit; i++)
        {
            using RateLimitLease lease = await limiter.AcquireAsync(ContextForAdmin());
            acquired.Add(lease.IsAcquired);
        }

        acquired
            .Should()
            .AllSatisfy(
                x => x.Should().BeTrue(),
                "every call within the permit limit " + "must succeed"
            );

        // The (PermitLimit + 1)th call from the SAME caller within the window must be rejected.
        using RateLimitLease overLimit = await limiter.AcquireAsync(ContextForAdmin());
        overLimit.IsAcquired.Should().BeFalse();

        // A DIFFERENT caller is a separate partition and is unaffected.
        DefaultHttpContext otherCaller = new();
        otherCaller.User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "another-admin")], "test")
        );
        using RateLimitLease otherLease = await limiter.AcquireAsync(otherCaller);
        otherLease.IsAcquired.Should().BeTrue();
    }
}
