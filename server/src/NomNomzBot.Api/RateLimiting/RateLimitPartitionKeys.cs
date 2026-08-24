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
using Microsoft.Extensions.Primitives;

namespace NomNomzBot.Api.RateLimiting;

/// <summary>Shared partition-key extraction used by every tier's rate-limit policy (S114).</summary>
internal static class RateLimitPartitionKeys
{
    public static string PrincipalOrIp(HttpContext context) =>
        context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? Ip(context);

    public static string Ip(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";

    /// <summary>
    /// Mirrors <c>TenantResolutionMiddleware</c>'s channel-id resolution order (route → header → query)
    /// so an expensive-write bucket is keyed per channel rather than per caller — a moderator acting on
    /// several channels gets a separate budget for each one instead of one shared budget.
    /// </summary>
    public static string ChannelOrCaller(HttpContext context)
    {
        if (
            context.Request.RouteValues.TryGetValue("channelId", out object? routeVal)
            && routeVal is string routeStr
            && !string.IsNullOrEmpty(routeStr)
        )
        {
            return routeStr;
        }

        if (
            context.Request.Headers.TryGetValue("X-Channel-Id", out StringValues headerVal)
            && !string.IsNullOrEmpty(headerVal)
        )
        {
            return headerVal!;
        }

        if (
            context.Request.Query.TryGetValue("channelId", out StringValues queryVal)
            && !string.IsNullOrEmpty(queryVal)
        )
        {
            return queryVal!;
        }

        return PrincipalOrIp(context);
    }
}
