// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace NomNomzBot.Api.RateLimiting;

/// <summary>
/// Registers every named rate-limit policy tier (S114) plus the global 429 handler that stamps
/// <c>Retry-After</c> so a throttled client knows when to try again. Kept in one file so
/// <c>Program.cs</c> only needs a single call site. Each tier's own partitioning function lives in its
/// own testable static class beside this one (mirrors <see cref="SecuritySensitiveRateLimitPolicy"/>) so
/// its rejection behavior can be exercised through a real <see cref="PartitionedRateLimiter{TResource}"/>
/// without a full ASP.NET pipeline.
/// </summary>
public static class RateLimitPolicyRegistration
{
    public static void AddNomNomzRateLimitPolicies(this RateLimiterOptions options)
    {
        options.AddPolicy(ReadRateLimitPolicy.PolicyName, ReadRateLimitPolicy.Partition);
        options.AddPolicy(
            WriteCheapRateLimitPolicy.PolicyName,
            WriteCheapRateLimitPolicy.Partition
        );
        options.AddPolicy(
            WriteExpensiveRateLimitPolicy.PolicyName,
            WriteExpensiveRateLimitPolicy.Partition
        );
        options.AddPolicy(AuthRateLimitPolicy.PolicyName, AuthRateLimitPolicy.Partition);
        options.AddPolicy(
            DevicePollRateLimitPolicy.PolicyName,
            DevicePollRateLimitPolicy.Partition
        );
        options.AddPolicy(AnonymousRateLimitPolicy.PolicyName, AnonymousRateLimitPolicy.Partition);
        options.AddPolicy(AdminRateLimitPolicy.PolicyName, AdminRateLimitPolicy.Partition);
        options.AddPolicy(
            SecuritySensitiveRateLimitPolicy.PolicyName,
            SecuritySensitiveRateLimitPolicy.Partition
        );

        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        // Every 429 carries Retry-After so a throttled client knows when to try again, regardless of
        // which tier rejected it or how much of that tier's window remains.
        options.OnRejected = (rejectedContext, _) =>
        {
            TimeSpan retryAfter = rejectedContext.Lease.TryGetMetadata(
                MetadataName.RetryAfter,
                out TimeSpan lease
            )
                ? lease
                : TimeSpan.FromSeconds(60);
            rejectedContext.HttpContext.Response.Headers.RetryAfter = (
                (int)Math.Ceiling(retryAfter.TotalSeconds)
            ).ToString();
            return ValueTask.CompletedTask;
        };
    }
}
