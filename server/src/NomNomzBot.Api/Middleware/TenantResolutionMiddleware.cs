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
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Identity.Services;

namespace NomNomzBot.Api.Middleware;

/// <summary>
/// Resolves the current tenant (channel) for the request.
///
/// Security: an authenticated caller may only resolve to a channel they are authorized for —
/// their own channel, one they moderate, or (for admins) any. An unauthorized attempt fails
/// closed with 403 and the request is not processed further. Anonymous requests may select a
/// channel by id for public endpoints only; private endpoints are gated by [Authorize] and are
/// never reached here with an unauthenticated principal.
/// </summary>
public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;

    public TenantResolutionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ICurrentTenantService tenantService,
        IChannelAccessService channelAccess
    )
    {
        string? requestedChannelId = ResolveRequestedChannelId(context);

        string? userId =
            context.User.Identity?.IsAuthenticated == true
                ? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                : null;

        if (!string.IsNullOrEmpty(requestedChannelId))
        {
            if (string.IsNullOrEmpty(userId))
            {
                // Anonymous request → public-endpoint channel selector (public data only).
                tenantService.SetTenant(requestedChannelId);
            }
            else if (
                await channelAccess.CanResolveTenantAsync(
                    userId,
                    requestedChannelId,
                    context.RequestAborted
                )
            )
            {
                tenantService.SetTenant(requestedChannelId);
            }
            else
            {
                // Authenticated caller asked to act as a channel they do not control. Fail closed —
                // do not set the tenant and do not continue the pipeline.
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
        }
        else if (!string.IsNullOrEmpty(userId))
        {
            // No explicit channel → default to the authenticated caller's own channel.
            tenantService.SetTenant(userId);
        }

        await _next(context);
    }

    private static string? ResolveRequestedChannelId(HttpContext context)
    {
        // 1. Route value: /channels/{channelId}/...
        if (
            context.Request.RouteValues.TryGetValue("channelId", out object? routeVal)
            && routeVal is string routeStr
            && !string.IsNullOrEmpty(routeStr)
        )
        {
            return routeStr;
        }

        // 2. Custom header
        if (
            context.Request.Headers.TryGetValue("X-Channel-Id", out StringValues headerVal)
            && !string.IsNullOrEmpty(headerVal)
        )
        {
            return headerVal!;
        }

        // 3. Query string
        if (
            context.Request.Query.TryGetValue("channelId", out StringValues queryVal)
            && !string.IsNullOrEmpty(queryVal)
        )
        {
            return queryVal!;
        }

        return null;
    }
}
