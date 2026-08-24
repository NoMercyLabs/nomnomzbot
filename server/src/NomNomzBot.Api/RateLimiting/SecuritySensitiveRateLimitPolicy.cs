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

namespace NomNomzBot.Api.RateLimiting;

/// <summary>
/// Rate-limit policy for admin actions that handle key-custody material (e.g. the KEK-rotation re-wrap
/// pass) — far stricter than the general "api" policy (120/min), because even an authenticated platform
/// admin must not be able to spam an operation that walks and re-wraps every stored DEK. Keyed per caller
/// (falls back to IP for an unauthenticated request, though every action using this policy also requires
/// <c>[Authorize]</c>). Extracted as a testable static so the rejection behavior can be exercised without a
/// full ASP.NET pipeline.
/// </summary>
public static class SecuritySensitiveRateLimitPolicy
{
    public const string PolicyName = "security-sensitive";
    public const int PermitLimit = 3;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public static RateLimitPartition<string> Partition(HttpContext context)
    {
        string key =
            context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(
            $"{PolicyName}:{key}",
            _ =>
                new()
                {
                    PermitLimit = PermitLimit,
                    Window = Window,
                    QueueLimit = 0,
                }
        );
    }
}
