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
using NomNomzBot.Domain.Commands.ValueObjects;

namespace NomNomzBot.Infrastructure.Chat.PipelineActions;

public sealed class SendMessageAction : ICommandAction
{
    private readonly IChatProvider _chat;
    private readonly ITemplateResolver _resolver;
    private readonly IReplyDispatcher _replies;

    public string ActionType => "send_message";

    public LocalizedText Category => new("pipeline.category.chat");

    public LocalizedText Description => new("pipeline.send_message.description");
    public bool ResolvesOwnTemplates => true;

    public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
        [
            new(
                "message",
                PipelineActionFieldKind.Text,
                Required: true,
                Templated: true,
                Description: new("pipeline.send_message.message.help")
            ),
            new(
                "sender",
                PipelineActionFieldKind.Enum,
                Options: [SenderBot, SenderBroadcaster],
                Description: new("pipeline.send_message.sender.help")
            ),
            new(
                "reply_target",
                PipelineActionFieldKind.Enum,
                Options: [.. Enum.GetValues<ReplyTarget>().Select(ReplyTargetName)],
                Description: new("pipeline.send_message.reply_target.help")
            ),
            new(
                "reply_html",
                PipelineActionFieldKind.Boolean,
                Description: new("pipeline.send_message.reply_html.help")
            ),
        ];

    private const string SenderBot = "bot";
    private const string SenderBroadcaster = "broadcaster";

    public SendMessageAction(
        IChatProvider chat,
        ITemplateResolver resolver,
        IReplyDispatcher replies
    )
    {
        _chat = chat;
        _resolver = resolver;
        _replies = replies;
    }

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string template = action.GetString("message") ?? string.Empty;
        if (string.IsNullOrEmpty(template))
            return ActionResult.Failure("send_message requires a 'message' parameter");

        // "broadcaster" is for content only the streamer's own account can post as themselves — e.g. a
        // subscriber-only emote a separate bot account isn't subscribed to and so can't render. That voice
        // only exists in the platform chat, so it never takes the reply target.
        if (action.GetString("sender") == SenderBroadcaster)
            return await SendAsBroadcasterAsync(ctx, template);

        // Defaults to the bot voice and to the platform chat (existing behavior, unchanged for every step
        // that never set a reply target).
        ReplyDelivery delivery = ReadDelivery(action);
        RenderedReply rendered = await ReplyTemplateRenderer.RenderAsync(
            _resolver,
            template,
            ctx.Variables,
            ctx.BroadcasterId,
            delivery,
            ctx.CancellationToken
        );
        string chatLine = PipelineChatLine.Cut(rendered.ChatText);
        ReplyOutcome outcome = await _replies.DispatchAsync(
            new ReplyRequest
            {
                BroadcasterId = ctx.BroadcasterId,
                Text = chatLine,
                OverlayText = rendered.OverlayText,
                Delivery = delivery,
                RequestedBy = string.IsNullOrWhiteSpace(ctx.TriggeredByDisplayName)
                    ? null
                    : ctx.TriggeredByDisplayName,
            },
            ctx.CancellationToken
        );
        ctx.RepliedToChat |= outcome.SentToChat;
        return outcome.Succeeded(delivery)
            ? ActionResult.Success(chatLine)
            : ActionResult.Failure("send_message could not be delivered");
    }

    private async Task<ActionResult> SendAsBroadcasterAsync(
        PipelineExecutionContext ctx,
        string template
    )
    {
        string resolved = PipelineChatLine.Cut(
            await _resolver.ResolveAsync(
                template,
                ChatMentionName.ForChat(ctx.Variables),
                ctx.BroadcasterId,
                ctx.CancellationToken
            )
        );
        bool sent = await _chat.SendMessageAsBroadcasterAsync(
            ctx.BroadcasterId,
            resolved,
            ctx.CancellationToken
        );
        ctx.RepliedToChat |= sent;
        return sent
            ? ActionResult.Success(resolved)
            : ActionResult.Failure("send_message could not be delivered");
    }

    private static ReplyDelivery ReadDelivery(ActionDefinition action)
    {
        string? target = action.GetString("reply_target");
        ReplyTarget parsed = Enum.TryParse(target, ignoreCase: true, out ReplyTarget value)
            ? value
            : ReplyTarget.Chat;
        return new(parsed, action.GetBool("reply_html"));
    }

    private static string ReplyTargetName(ReplyTarget target) =>
        target.ToString().ToLowerInvariant();
}
