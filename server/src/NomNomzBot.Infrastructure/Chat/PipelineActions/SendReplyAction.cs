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
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Domain.Chat.Interfaces;

namespace NomNomzBot.Infrastructure.Chat.PipelineActions;

public sealed class SendReplyAction : ICommandAction
{
    private readonly IChatProvider _chat;
    private readonly ITemplateResolver _resolver;

    public string ActionType => "send_reply";

    public SendReplyAction(IChatProvider chat, ITemplateResolver resolver)
    {
        _chat = chat;
        _resolver = resolver;
    }

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string template = action.GetString("message") ?? action.GetString("text") ?? string.Empty;
        if (string.IsNullOrEmpty(template))
            return ActionResult.Failure("send_reply requires a 'message' parameter");

        string resolved = await _resolver.ResolveAsync(
            template,
            ctx.Variables,
            ctx.BroadcasterId,
            ctx.CancellationToken
        );
        await _chat.SendReplyAsync(
            ctx.BroadcasterId,
            ctx.MessageId,
            resolved,
            ctx.CancellationToken
        );
        return ActionResult.Success(resolved);
    }
}
