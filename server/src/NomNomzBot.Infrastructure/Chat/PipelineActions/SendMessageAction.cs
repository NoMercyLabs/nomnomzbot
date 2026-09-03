// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Domain.Chat.Interfaces;

namespace NomNomzBot.Infrastructure.Chat.PipelineActions;

public sealed class SendMessageAction : ICommandAction
{
    private readonly IChatProvider _chat;
    private readonly ITemplateResolver _resolver;

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
        ];

    private const string SenderBot = "bot";
    private const string SenderBroadcaster = "broadcaster";

    public SendMessageAction(IChatProvider chat, ITemplateResolver resolver)
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
            return ActionResult.Failure("send_message requires a 'message' parameter");

        string resolved = await _resolver.ResolveAsync(
            template,
            ctx.Variables,
            ctx.BroadcasterId,
            ctx.CancellationToken
        );
        // Defaults to the bot voice (existing behavior, unchanged for every step that never set this).
        // "broadcaster" is for content only the streamer's own account can post as themselves — e.g. a
        // subscriber-only emote a separate bot account isn't subscribed to and so can't render.
        bool asBroadcaster = action.GetString("sender") == SenderBroadcaster;
        bool sent = asBroadcaster
            ? await _chat.SendMessageAsBroadcasterAsync(
                ctx.BroadcasterId,
                resolved,
                ctx.CancellationToken
            )
            : await _chat.SendMessageAsync(ctx.BroadcasterId, resolved, ctx.CancellationToken);
        return sent
            ? ActionResult.Success(resolved)
            : ActionResult.Failure("send_message could not be delivered");
    }
}
