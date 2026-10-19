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
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Common.Picking;
using NomNomzBot.Application.Contracts.Security;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Commands.Enums;
using NomNomzBot.Domain.Commands.ValueObjects;
using NomNomzBot.Domain.Identity;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Chat.EventHandlers;
using NomNomzBot.Infrastructure.Tts.PipelineActions;
using PipelineEntity = NomNomzBot.Domain.Commands.Entities.Pipeline;

namespace NomNomzBot.Infrastructure.Commands;

/// <inheritdoc cref="ICommandInvoker"/>
public sealed class CommandInvoker(
    IApplicationDbContext db,
    IPipelineEngine pipelineEngine,
    IOwnerActionService ownerActions,
    IChannelTwitchBotResolver bots,
    ITemplateResolver templateResolver,
    IChatProvider chat,
    IBuiltinCommandCatalog builtins,
    IOutboundSanctionAccessor sanctions
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
            BindingTargetKind.Command => RunCommandAsync(
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

        return await ExecutePipelineAsync(
            broadcasterId,
            pipeline.Id,
            pipeline.GraphJsonCache,
            false,
            context,
            cancellationToken
        );
    }

    // Direct runs skip the cooldown and permission gates (Stoney 2026-10-19, Q1): nothing here consults them.
    private async Task<Result<InvocationOutcome>> RunCommandAsync(
        Guid broadcasterId,
        BindingTarget target,
        InvocationContext context,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(target.Id))
            return Result.Failure<InvocationOutcome>(
                "A command target needs a command id.",
                "VALIDATION_FAILED"
            );

        // A built-in has no command row (it lives in code), so its target id is its catalogue key, not a guid.
        if (!Guid.TryParse(target.Id, out Guid commandId))
            return await RunBuiltinAsync(broadcasterId, target.Id, context, cancellationToken);

        Command? command = await db
            .Commands.AsNoTracking()
            .FirstOrDefaultAsync(
                c => c.Id == commandId && c.BroadcasterId == broadcasterId && c.IsEnabled,
                cancellationToken
            );
        if (command is null)
            return Errors.NotFound<InvocationOutcome>("Command", target.Id!);

        return command.Tier switch
        {
            "template" => await RunTemplateAsync(
                broadcasterId,
                command,
                context,
                cancellationToken
            ),
            "pipeline" or "code" => await RunBoundPipelineAsync(
                broadcasterId,
                command,
                context,
                cancellationToken
            ),
            _ => Result.Failure<InvocationOutcome>(
                $"A '{command.Tier}' command cannot be run directly yet.",
                "NOT_SUPPORTED"
            ),
        };
    }

    // The channel's enabled switch, floor and cooldown are skipped like the other tiers: the binding is the choice.
    private async Task<Result<InvocationOutcome>> RunBuiltinAsync(
        Guid broadcasterId,
        string builtinKey,
        InvocationContext context,
        CancellationToken cancellationToken
    )
    {
        IBuiltinCommand? builtin = builtins.Get(builtinKey);
        if (builtin is null)
            return Errors.NotFound<InvocationOutcome>("Built-in command", builtinKey);

        Invoker invoker = await ResolveInvokerAsync(broadcasterId, context, cancellationToken);
        BuiltinCommandContext builtinContext = new()
        {
            BroadcasterId = broadcasterId,
            TriggeringUserId = invoker.UserId,
            TriggeringUserDisplayName = invoker.DisplayName,
            TriggeringUserLogin = invoker.DisplayName.ToLowerInvariant(),
            RoleLevel = ChatRole.Parse(invoker.Role).ToLevelValue(),
            Args = context.Args,
            CancellationToken = cancellationToken,
        };

        Result<string> run;
        using (
            sanctions.Begin(OutboundSanction.ChannelConfiguration($"builtin:{builtin.BuiltinKey}"))
        )
            run = await builtin.ExecuteAsync(builtinContext, cancellationToken);
        if (run.IsFailure)
            return Result.Failure<InvocationOutcome>(
                run.ErrorMessage!,
                run.ErrorCode!,
                run.ErrorDetail
            );

        string reply = run.Value ?? string.Empty;
        if (reply.Length == 0)
            return Result.Success(new InvocationOutcome(true, null, null));

        bool sent = await chat.SendMessageAsync(broadcasterId, reply, cancellationToken);
        return Result.Success(
            sent
                ? new InvocationOutcome(true, reply, null)
                : new InvocationOutcome(false, null, "The reply could not be sent to chat.")
        );
    }

    // A "code" command's reaction is a single run_code pipeline, so it takes the same path as a "pipeline" one.
    private async Task<Result<InvocationOutcome>> RunBoundPipelineAsync(
        Guid broadcasterId,
        Command command,
        InvocationContext context,
        CancellationToken cancellationToken
    )
    {
        PipelineEntity? pipeline = command.PipelineId is null
            ? null
            : await db
                .Pipelines.AsNoTracking()
                .FirstOrDefaultAsync(
                    p =>
                        p.Id == command.PipelineId
                        && p.BroadcasterId == broadcasterId
                        && p.IsEnabled,
                    cancellationToken
                );
        if (pipeline is null || string.IsNullOrEmpty(pipeline.GraphJsonCache))
            return Errors.NotFound<InvocationOutcome>(
                "Pipeline",
                command.PipelineId?.ToString() ?? command.Name
            );

        return await ExecutePipelineAsync(
            broadcasterId,
            pipeline.Id,
            pipeline.GraphJsonCache,
            command.SpeakWithTts,
            context,
            cancellationToken
        );
    }

    // Outside chat there is no message to reply to, so the reply goes out as a plain message in the one bot voice.
    private async Task<Result<InvocationOutcome>> RunTemplateAsync(
        Guid broadcasterId,
        Command command,
        InvocationContext context,
        CancellationToken cancellationToken
    )
    {
        string[] responses =
            command.TemplateResponses is { Count: > 0 } ? [.. command.TemplateResponses]
            : string.IsNullOrWhiteSpace(command.TemplateResponse) ? []
            : [command.TemplateResponse];
        if (responses.Length == 0)
            return Result.Failure<InvocationOutcome>(
                "The command has no reply text.",
                "VALIDATION_FAILED"
            );

        Invoker invoker = await ResolveInvokerAsync(broadcasterId, context, cancellationToken);
        string resolved = await templateResolver.ResolveAsync(
            NoImmediateRepeatPicker.Pick(responses, $"{broadcasterId}:{command.Name}"),
            ChatMentionName.ForChat(BuildVariables(context, invoker)),
            broadcasterId,
            cancellationToken
        );

        bool sent = await chat.SendMessageAsync(broadcasterId, resolved, cancellationToken);
        if (!sent)
            return Result.Success(
                new InvocationOutcome(false, null, "The reply could not be sent to chat.")
            );

        if (command.SpeakWithTts && !string.IsNullOrWhiteSpace(resolved))
        {
            // Bot-authored speech: the bot identity is the speaker, never the human who triggered the run.
            Invoker bot = await ResolveBotAsync(broadcasterId, cancellationToken);
            await ReplyTtsPipeline.QueueAsync(
                pipelineEngine,
                broadcasterId,
                bot.UserId,
                bot.DisplayName,
                null,
                resolved,
                cancellationToken
            );
        }

        return Result.Success(new InvocationOutcome(true, resolved, null));
    }

    private async Task<Result<InvocationOutcome>> ExecutePipelineAsync(
        Guid broadcasterId,
        Guid pipelineId,
        string? graphJson,
        bool speakWithTts,
        InvocationContext context,
        CancellationToken cancellationToken
    )
    {
        Invoker invoker = await ResolveInvokerAsync(broadcasterId, context, cancellationToken);
        PipelineRequest request = new()
        {
            BroadcasterId = broadcasterId,
            PipelineId = pipelineId,
            PipelineJson = graphJson ?? "{}",
            SpeakWithTts = speakWithTts,
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

        return await ResolveBotAsync(broadcasterId, cancellationToken);
    }

    private async Task<Invoker> ResolveBotAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken
    )
    {
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
