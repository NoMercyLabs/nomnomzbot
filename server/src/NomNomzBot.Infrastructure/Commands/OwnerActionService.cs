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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Widgets.Dtos;

namespace NomNomzBot.Infrastructure.Commands;

/// <inheritdoc cref="IOwnerActionService"/>
public sealed class OwnerActionService(
    IApplicationDbContext db,
    IServiceProvider services,
    IActionAuthorizationService authorization,
    IRateLimiterPartitionStore rateLimiter,
    ILogger<OwnerActionService> logger
) : IOwnerActionService
{
    // Running an arbitrary action is what the dashboard's pipeline test-run does, so it needs the same permission.
    internal const string RequiredActionKey = "pipelines:write";

    // A caller runs a handful of actions per alert (synthesize, pause, resume, reply). The cap only stops a
    // looping widget or script from flooding chat or the music provider.
    internal const int InvocationsPerMinute = 60;

    // One action never runs longer than the longest single step a pipeline allows (wait caps at 30 s).
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(45);

    public async Task<Result<WidgetActionOutcome>> RunAsync(
        OwnerActionRequest request,
        CancellationToken cancellationToken = default
    )
    {
        // Resolved per call, not injected: run_code is itself an action and the script bridge reaches this
        // service, so an IEnumerable<ICommandAction> dependency is a DI cycle (as in RunPipelineAction).
        ICommandAction? action = services
            .GetServices<ICommandAction>()
            .FirstOrDefault(a =>
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
            request.RateLimitPartition,
            InvocationsPerMinute,
            TimeSpan.FromMinutes(1),
            cancellationToken
        );
        if (!lease.IsAcquired)
            return Result.Failure<WidgetActionOutcome>(
                "Too many actions were run. Try again in a minute.",
                "RATE_LIMITED"
            );

        return Result.Success(
            await ExecuteAsync(action, request, ownerUserId.Value, cancellationToken)
        );
    }

    private async Task<WidgetActionOutcome> ExecuteAsync(
        ICommandAction action,
        OwnerActionRequest request,
        Guid ownerUserId,
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
            TriggeredByDisplayName = request.Caller,
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
                "Action {Action} run by {Caller} failed for channel {Channel}",
                action.ActionType,
                request.Caller,
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
