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
using NomNomzBot.Domain.Chat.Interfaces;

namespace NomNomzBot.Infrastructure.Moderation.PipelineActions;

public sealed class DeleteMessageAction : ICommandAction
{
    private readonly IChatProvider _chat;

    public string ActionType => "delete_message";

    public DeleteMessageAction(IChatProvider chat) => _chat = chat;

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string messageId = action.GetString("message_id") ?? ctx.MessageId;
        if (string.IsNullOrEmpty(messageId))
            return ActionResult.Failure("delete_message: message_id not resolved");

        await _chat.DeleteMessageAsync(ctx.BroadcasterId, messageId, ctx.CancellationToken);
        return ActionResult.Success($"Deleted message {messageId}");
    }
}
