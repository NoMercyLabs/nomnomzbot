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
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Domain.Chat.Interfaces;

namespace NomNomzBot.Infrastructure.Chat.PipelineActions;

public sealed class SendReplyAction : ICommandAction
{
    private readonly IChatProvider _chat;
    private readonly ITemplateResolver _resolver;

    public string ActionType => "send_reply";

    public LocalizedText Category => new("pipeline.category.chat");

    public LocalizedText Description => new("pipeline.send_reply.description");
    public bool ResolvesOwnTemplates => true;

    public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
        [
            new(
                "message",
                PipelineActionFieldKind.Text,
                Required: true,
                Templated: true,
                Description: new("pipeline.send_reply.message.help")
            ),
        ];

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
        string template = action.GetString("message") ?? string.Empty;
        if (string.IsNullOrEmpty(template))
            return ActionResult.Failure("send_reply requires a 'message' parameter");

        string resolved = PipelineChatLine.Cut(
            await _resolver.ResolveAsync(
                template,
                ChatMentionName.ForChat(ctx.Variables),
                ctx.BroadcasterId,
                ctx.CancellationToken
            )
        );
        bool sent = await _chat.SendReplyAsync(
            ctx.BroadcasterId,
            ctx.MessageId,
            resolved,
            ctx.CancellationToken
        );

        ctx.RepliedToChat |= sent;
        if (sent)
            return ActionResult.Success(resolved);

        // The reply form was rejected (e.g. a deleted/invalid parent message) — fall back to a plain
        // line that still addresses the triggering user via an inline mention, rather than dropping
        // the response silently.
        ReplyOrMentionPlan fallback = ReplyOrMentionComposer.Compose(
            null,
            ctx.TriggeredByChatName,
            resolved
        );
        bool fallbackSent = await _chat.SendMessageAsync(
            ctx.BroadcasterId,
            fallback.Message,
            ctx.CancellationToken
        );

        ctx.RepliedToChat |= fallbackSent;

        return fallbackSent
            ? ActionResult.Success(resolved)
            : ActionResult.Failure(
                "send_reply could not be delivered (reply and fallback both failed)"
            );
    }
}
