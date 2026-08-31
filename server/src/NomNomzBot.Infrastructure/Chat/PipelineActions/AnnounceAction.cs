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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Chat.PipelineActions;

/// <summary>
/// Pipeline action that posts a Twitch "Chat Announcement" — the native Helix endpoint
/// (<see cref="ITwitchChatApi.SendAnnouncementAsync"/>, requires <c>moderator:manage:announcements</c>),
/// distinct from a plain chat message: Twitch renders it with a colored highlight banner. Unlike
/// <see cref="SendMessageAction"/>'s plain <c>user:write:chat</c> post, this always goes out on the
/// tenant's own moderator token via the Chat sub-client.
///
/// Parameters:
///   message — the announcement text (required, templated — resolved the same way
///             <see cref="SendMessageAction"/> resolves its own message).
///   color   — optional highlight color; one of "primary", "purple", "blue", "green", "orange".
///             Omitted/blank leaves Twitch's default (primary).
///
/// Usage example:
///   { "type": "announce", "message": "{user.name} just hit level {args.1}!", "color": "green" }
/// </summary>
public sealed class AnnounceAction : ICommandAction
{
    private static readonly IReadOnlyList<string> Colors =
    [
        "primary",
        "purple",
        "blue",
        "green",
        "orange",
    ];

    private readonly ITwitchChatApi _chat;
    private readonly ITemplateResolver _resolver;

    public string ActionType => "announce";

    public LocalizedText Category => new("pipeline.category.chat");

    public LocalizedText Description => new("pipeline.announce.description");
    public bool ResolvesOwnTemplates => true;

    public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
        [
            new(
                "message",
                PipelineActionFieldKind.Text,
                Required: true,
                Templated: true,
                Description: new("pipeline.announce.message.help")
            ),
            new(
                "color",
                PipelineActionFieldKind.Enum,
                Options: Colors,
                Description: new("pipeline.announce.color.help")
            ),
        ];

    public AnnounceAction(ITwitchChatApi chat, ITemplateResolver resolver)
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
            return ActionResult.Failure("announce requires a 'message' parameter");

        string resolved = await _resolver.ResolveAsync(
            template,
            ctx.Variables,
            ctx.BroadcasterId,
            ctx.CancellationToken
        );

        string? color = action.GetString("color");
        if (string.IsNullOrWhiteSpace(color) || !Colors.Contains(color))
            color = null;

        Result result = await _chat.SendAnnouncementAsync(
            ctx.BroadcasterId,
            resolved,
            color,
            ctx.CancellationToken
        );
        return result.IsSuccess
            ? ActionResult.Success(resolved)
            : ActionResult.Failure($"announce could not be delivered: {result.ErrorMessage}");
    }
}
