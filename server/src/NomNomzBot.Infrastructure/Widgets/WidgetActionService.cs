// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;

namespace NomNomzBot.Infrastructure.Widgets;

/// <inheritdoc cref="IWidgetActionService"/>
public sealed class WidgetActionService(
    IApplicationDbContext db,
    IOwnerActionService ownerActions,
    IRateLimiterPartitionStore rateLimiter
) : IWidgetActionService
{
    internal const int MaxClaimKeyLength = 200;

    // Every copy of an overlay receives an event within moments of the others; ten minutes outlasts any retry.
    private static readonly TimeSpan ClaimWindow = TimeSpan.FromMinutes(10);

    public async Task<Result<WidgetActionOutcome>> InvokeAsync(
        WidgetActionRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var widget = await db
            .Widgets.AsNoTracking()
            .Where(w => w.Id == request.WidgetId && w.BroadcasterId == request.BroadcasterId)
            .Select(w => new { w.Name, w.IsEnabled })
            .FirstOrDefaultAsync(cancellationToken);
        if (widget is null)
            return Errors.NotFound<WidgetActionOutcome>("Widget", request.WidgetId.ToString());
        if (!widget.IsEnabled)
            return Result.Failure<WidgetActionOutcome>("This widget is turned off.", "FORBIDDEN");

        return await ownerActions.RunAsync(
            new(
                request.BroadcasterId,
                request.ActionType,
                request.Parameters,
                request.Variables,
                widget.Name,
                $"widget-action:{request.WidgetId}"
            ),
            cancellationToken
        );
    }

    public async Task<Result<bool>> ClaimAsync(
        Guid broadcasterId,
        Guid widgetId,
        string key,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > MaxClaimKeyLength)
            return Result.Failure<bool>(
                $"A claim key is 1 to {MaxClaimKeyLength} characters.",
                "VALIDATION_FAILED"
            );

        bool inChannel = await db
            .Widgets.AsNoTracking()
            .AnyAsync(w => w.Id == widgetId && w.BroadcasterId == broadcasterId, cancellationToken);
        if (!inChannel)
            return Errors.NotFound<bool>("Widget", widgetId.ToString());

        // A one-permit window: the first copy takes the only permit, every later copy is refused it.
        RateLimitLease lease = await rateLimiter.AcquireAsync(
            $"widget-claim:{widgetId}:{key}",
            1,
            ClaimWindow,
            cancellationToken
        );
        return Result.Success(lease.IsAcquired);
    }
}
