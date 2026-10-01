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
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;

namespace NomNomzBot.Infrastructure.Widgets;

/// <inheritdoc cref="IWidgetActionService"/>
public sealed class WidgetActionService(
    IApplicationDbContext db,
    IEnumerable<ICommandAction> actions,
    IActionAuthorizationService authorization,
    IRateLimiterPartitionStore rateLimiter,
    ILogger<WidgetActionService> logger
) : IWidgetActionService
{
    // Running an arbitrary action is what the dashboard's pipeline test-run does, so it needs the same permission.
    internal const string RequiredActionKey = "pipelines:write";

    // A widget runs a handful of actions per alert (synthesize, pause, resume, reply). The cap only stops a
    // looping widget from flooding chat or the music provider.
    internal const int InvocationsPerMinute = 60;

    // One action never runs longer than the longest single step a pipeline allows (wait caps at 30 s).
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(45);

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

        ICommandAction? action = actions.FirstOrDefault(a =>
            string.Equals(a.ActionType, request.ActionType, StringComparison.OrdinalIgnoreCase)
        );
        if (action is null)
            return Errors.NotFound<WidgetActionOutcome>("Action", request.ActionType);

        Guid? ownerUserId = await db
            .Channels.AsNoTracking()
            .Where(c => c.Id == request.BroadcasterId)
            .Select(c => (Guid?)c.OwnerUserId)
            .FirstOrDefaultAsync(cancellationToken);
        if (ownerUserId is null)
            return Errors.NotFound<WidgetActionOutcome>(
                "Channel",
                request.BroadcasterId.ToString()
            );

        Result<bool> allowed = await authorization.AuthorizeActionAsync(
            ownerUserId.Value,
            request.BroadcasterId,
            RequiredActionKey,
            cancellationToken
        );
        if (allowed is not { IsSuccess: true, Value: true })
            return Result.Failure<WidgetActionOutcome>(
                $"The channel owner may not run '{RequiredActionKey}' actions.",
                "FORBIDDEN"
            );

        RateLimitLease lease = await rateLimiter.AcquireAsync(
            $"widget-action:{request.WidgetId}",
            InvocationsPerMinute,
            TimeSpan.FromMinutes(1),
            cancellationToken
        );
        if (!lease.IsAcquired)
            return Result.Failure<WidgetActionOutcome>(
                "This widget runs too many actions. Try again in a minute.",
                "RATE_LIMITED"
            );

        return Result.Success(
            await RunAsync(action, request, ownerUserId.Value, widget.Name, cancellationToken)
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

    private async Task<WidgetActionOutcome> RunAsync(
        ICommandAction action,
        WidgetActionRequest request,
        Guid ownerUserId,
        string widgetName,
        CancellationToken cancellationToken
    )
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        timeout.CancelAfter(RunTimeout);

        PipelineExecutionContext context = new()
        {
            BroadcasterId = request.BroadcasterId,
            TriggeredByUserId = ownerUserId.ToString(),
            TriggeredByDisplayName = widgetName,
            MessageId = string.Empty,
            RawMessage = string.Empty,
            CancellationToken = timeout.Token,
        };
        foreach (
            (string key, string value) in request.Variables ?? new Dictionary<string, string>()
        )
            context.Variables[key] = value;

        ActionDefinition definition = new()
        {
            Type = action.ActionType,
            Parameters = request.Parameters?.ToDictionary(p => p.Key, p => p.Value),
        };

        try
        {
            ActionResult result = await action.ExecuteAsync(context, definition);
            if (result.Suspended)
                return Outcome(
                    false,
                    null,
                    $"'{action.ActionType}' can only run inside a pipeline.",
                    context
                );
            return Outcome(result.Succeeded, result.Output, result.ErrorMessage, context);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Outcome(false, null, $"'{action.ActionType}' took too long.", context);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Widget {Widget} action {Action} failed for channel {Channel}",
                request.WidgetId,
                action.ActionType,
                request.BroadcasterId
            );
            return Outcome(false, null, ex.Message, context);
        }
    }

    private static WidgetActionOutcome Outcome(
        bool succeeded,
        string? output,
        string? error,
        PipelineExecutionContext context
    ) => new(succeeded, output, error, new Dictionary<string, string>(context.Variables));
}
