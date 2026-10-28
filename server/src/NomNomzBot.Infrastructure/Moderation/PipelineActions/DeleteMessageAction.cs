// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Domain.Chat.Interfaces;

namespace NomNomzBot.Infrastructure.Moderation.PipelineActions;

public sealed class DeleteMessageAction : ICommandAction
{
    private readonly IChatProvider _chat;

    public string ActionType => "delete_message";

    public LocalizedText Category => new("pipeline.category.moderation");

    public LocalizedText Description => new("pipeline.delete_message.description");
    public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
        [
            new(
                "message_id",
                PipelineActionFieldKind.Text,
                Description: new("pipeline.delete_message.message_id.help")
            ),
        ];

    public DeleteMessageAction(IChatProvider chat) => _chat = chat;

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string messageId = action.GetString("message_id") ?? ctx.MessageId;
        if (string.IsNullOrEmpty(messageId))
            return ActionResult.Failure("delete_message: message_id not resolved");

        bool deleted = await _chat.DeleteMessageAsync(
            ctx.BroadcasterId,
            messageId,
            ctx.CancellationToken
        );
        return deleted
            ? ActionResult.Success($"Deleted message {messageId}")
            : ActionResult.Failure(
                $"delete_message: the platform did not delete message {messageId}"
            );
    }
}
