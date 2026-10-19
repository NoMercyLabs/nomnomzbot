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
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Domain.Commands.Enums;
using NomNomzBot.Domain.Commands.ValueObjects;
using NomNomzBot.Domain.Identity;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Chat.EventHandlers;
using PipelineEntity = NomNomzBot.Domain.Commands.Entities.Pipeline;

namespace NomNomzBot.Infrastructure.Commands;

/// <inheritdoc cref="ICommandInvoker"/>
public sealed class CommandInvoker(
    IApplicationDbContext db,
    IPipelineEngine pipelineEngine,
    IOwnerActionService ownerActions,
    IChannelTwitchBotResolver bots
) : ICommandInvoker
{
    // With no dedicated bot account the streamer's own account speaks (IChannelTwitchBotResolver), so the
    // system invoker then carries the channel id and this display name.
    private const string FallbackBotName = "NomNomzBot";

    public Task<Result<InvocationOutcome>> InvokeAsync(
        Guid broadcasterId,
        BindingTarget target,
        InvocationContext context,
        CancellationToken cancellationToken = default
    ) =>
        target.Kind switch
        {
            BindingTargetKind.Pipeline => RunPipelineAsync(
                broadcasterId,
                target,
                context,
                cancellationToken
            ),
            BindingTargetKind.Action => RunActionAsync(
                broadcasterId,
                target,
                context,
                cancellationToken
            ),
            _ => Task.FromResult(
                Result.Failure<InvocationOutcome>(
                    $"A '{target.Kind}' target cannot be run directly yet.",
                    "NOT_SUPPORTED"
                )
            ),
        };

    private async Task<Result<InvocationOutcome>> RunPipelineAsync(
        Guid broadcasterId,
        BindingTarget target,
        InvocationContext context,
        CancellationToken cancellationToken
    )
    {
        if (!Guid.TryParse(target.Id, out Guid pipelineId))
            return Result.Failure<InvocationOutcome>(
                "A pipeline target needs a pipeline id.",
                "VALIDATION_FAILED"
            );

        PipelineEntity? pipeline = await db
            .Pipelines.AsNoTracking()
            .FirstOrDefaultAsync(
                p => p.Id == pipelineId && p.BroadcasterId == broadcasterId && p.IsEnabled,
                cancellationToken
            );
        if (pipeline is null)
            return Errors.NotFound<InvocationOutcome>("Pipeline", target.Id!);

        Invoker invoker = await ResolveInvokerAsync(broadcasterId, context, cancellationToken);
        PipelineRequest request = new()
        {
            BroadcasterId = broadcasterId,
            PipelineId = pipeline.Id,
            PipelineJson = pipeline.GraphJsonCache ?? "{}",
            TriggeredByUserId = invoker.UserId,
            TriggeredByDisplayName = invoker.DisplayName,
            RawMessage = context.Args,
            InitialVariables = BuildVariables(context, invoker),
        };

        PipelineExecutionResult result = await pipelineEngine.ExecuteAsync(
            request,
            cancellationToken
        );

        bool succeeded =
            result.Outcome
            is PipelineOutcome.Completed
                or PipelineOutcome.Stopped
                or PipelineOutcome.Suspended;
        return Result.Success(
            new InvocationOutcome(
                succeeded,
                result.Outcome.ToString(),
                succeeded ? null : result.ErrorMessage ?? result.Outcome.ToString()
            )
        );
    }

    // The action runner gates on the channel owner's IAM looked up from the channel, not on a signed-in owner
    // session, so a timer or schedule with no human reaches it the same as a widget does.
    private async Task<Result<InvocationOutcome>> RunActionAsync(
        Guid broadcasterId,
        BindingTarget target,
        InvocationContext context,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(target.ActionType))
            return Result.Failure<InvocationOutcome>(
                "An action target needs an action type.",
                "VALIDATION_FAILED"
            );

        Invoker invoker = await ResolveInvokerAsync(broadcasterId, context, cancellationToken);
        Result<WidgetActionOutcome> run = await ownerActions.RunAsync(
            new OwnerActionRequest(
                broadcasterId,
                target.ActionType,
                target.Params,
                BuildVariables(context, invoker),
                context.TriggerSource,
                $"binding-action:{broadcasterId}"
            ),
            cancellationToken
        );
        if (run.IsFailure)
            return Result.Failure<InvocationOutcome>(
                run.ErrorMessage!,
                run.ErrorCode!,
                run.ErrorDetail
            );

        WidgetActionOutcome outcome = run.Value!;
        return Result.Success(
            new InvocationOutcome(outcome.Succeeded, outcome.Output, outcome.Error)
        );
    }

    private async Task<Invoker> ResolveInvokerAsync(
        Guid broadcasterId,
        InvocationContext context,
        CancellationToken cancellationToken
    )
    {
        if (!string.IsNullOrWhiteSpace(context.UserId))
            return new Invoker(
                context.UserId,
                context.DisplayName ?? context.UserId,
                ChatRole.ToToken(ChatRole.Parse(context.EffectiveRole))
            );

        ChannelTwitchBot? bot = await bots.ResolveAsync(broadcasterId, cancellationToken);
        return new Invoker(
            bot?.TwitchUserId ?? broadcasterId.ToString(),
            bot?.Username ?? FallbackBotName,
            ChatRole.ToToken(PermissionLevel.Broadcaster)
        );
    }

    private static Dictionary<string, string> BuildVariables(
        InvocationContext context,
        Invoker invoker
    )
    {
        Dictionary<string, string> variables = ChatMessageHandler.BuildArgumentVariables(
            context.Args
        );
        variables["user"] = invoker.DisplayName;
        variables["user.id"] = invoker.UserId;
        variables["user.name"] = invoker.DisplayName;
        variables["user.role"] = invoker.Role;
        variables["trigger.source"] = context.TriggerSource;
        return variables;
    }

    private sealed record Invoker(string UserId, string DisplayName, string Role);
}
