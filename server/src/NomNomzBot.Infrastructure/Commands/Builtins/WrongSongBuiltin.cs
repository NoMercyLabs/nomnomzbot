// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Infrastructure.Chat.EventHandlers;

namespace NomNomzBot.Infrastructure.Commands.Builtins;

/// <summary>
/// !wrongsong — takes back the caller's own song request: by its code (<c>!wrongsong K7QM</c>), by its queue
/// number (<c>!wrongsong 3</c>), or their newest one. A built-in, so every channel has it without hand-making a
/// command. It runs the one <c>song_wrong</c> action as the viewer who typed it; that action answers in chat
/// itself, so the built-in returns no reply of its own.
/// </summary>
public sealed class WrongSongBuiltin(IEnumerable<ICommandAction> actions) : IBuiltinCommand
{
    private const string ActionType = "song_wrong";

    public string BuiltinKey => "wrongsong";
    public int DefaultCooldownSeconds => 0;
    public int DefaultUserCooldownSeconds => 5;

    // Everyone: the action itself only ever removes the caller's own request.
    public int DefaultMinPermissionLevel => 0;

    public async Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        ICommandAction? action = actions.FirstOrDefault(a => a.ActionType == ActionType);
        if (action is null)
            return Result.Failure<string>(
                $"The {ActionType} action is not registered.",
                "NOT_FOUND"
            );

        ActionResult result = await action.ExecuteAsync(
            CallerContext(context, ct),
            new ActionDefinition { Type = ActionType }
        );
        return result.Succeeded
            ? Result.Success(string.Empty)
            : Result.Failure<string>(
                result.ErrorMessage ?? $"{ActionType} failed.",
                "ACTION_FAILED"
            );
    }

    // The pipeline context song_wrong reads: who typed it (its ownership check) and what came after the command.
    private static PipelineExecutionContext CallerContext(
        BuiltinCommandContext context,
        CancellationToken ct
    )
    {
        PipelineExecutionContext pipeline = new()
        {
            BroadcasterId = context.BroadcasterId,
            TriggeredByUserId = context.TriggeringUserId,
            TriggeredByPlatform = context.TriggeringPlatform,
            TriggeredByDisplayName = context.TriggeringUserDisplayName,
            MessageId = context.MessageId,
            RawMessage = context.Args,
            CancellationToken = ct,
        };
        pipeline.Variables["user"] = context.TriggeringUserDisplayName;
        pipeline.Variables["user.name"] = context.TriggeringUserLogin;
        foreach (
            KeyValuePair<string, string> pair in ChatMessageHandler.BuildArgumentVariables(
                context.Args
            )
        )
            pipeline.Variables[pair.Key] = pair.Value;

        return pipeline;
    }
}
