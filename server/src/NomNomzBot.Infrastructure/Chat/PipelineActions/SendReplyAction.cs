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

    public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
        [new("message", PipelineActionFieldKind.Text, Required: true)];

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
        bool sent = await _chat.SendReplyAsync(
            ctx.BroadcasterId,
            ctx.MessageId,
            resolved,
            ctx.CancellationToken
        );

        if (sent)
            return ActionResult.Success(resolved);

        // The reply form was rejected (e.g. a deleted/invalid parent message) — fall back to a plain
        // line that still addresses the triggering user via an inline mention, rather than dropping
        // the response silently.
        bool fallbackSent = await _chat.SendMessageAsync(
            ctx.BroadcasterId,
            $"@{ctx.TriggeredByDisplayName} {resolved}",
            ctx.CancellationToken
        );

        return fallbackSent
            ? ActionResult.Success(resolved)
            : ActionResult.Failure(
                "send_reply could not be delivered (reply and fallback both failed)"
            );
    }
}
