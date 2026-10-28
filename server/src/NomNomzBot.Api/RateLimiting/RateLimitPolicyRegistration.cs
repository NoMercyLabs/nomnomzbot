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

using System.Threading.RateLimiting;
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
        options.AddPolicy(SessionRateLimitPolicy.PolicyName, SessionRateLimitPolicy.Partition);
        options.AddPolicy(
            DevicePollRateLimitPolicy.PolicyName,
            DevicePollRateLimitPolicy.Partition
        );
        options.AddPolicy(AnonymousRateLimitPolicy.PolicyName, AnonymousRateLimitPolicy.Partition);
        options.AddPolicy(AdminRateLimitPolicy.PolicyName, AdminRateLimitPolicy.Partition);
        options.AddPolicy(
            ViewerChatSendRateLimitPolicy.PolicyName,
            ViewerChatSendRateLimitPolicy.Partition
        );
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
