// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Obs.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Bot;

namespace NomNomzBot.Infrastructure.Chat;

/// <inheritdoc cref="IReplyDispatcher"/>
/// <remarks>
/// The dispatcher opens no sanction scope of its own. It runs in the caller's async context, so a chat write
/// keeps the outbound sanction the caller holds.
/// </remarks>
public sealed class ReplyDispatcher(
    IInboundOriginChatSender originChat,
    IChatProvider chat,
    IChatOverlayLineSender overlay,
    IChannelTwitchBotResolver bots,
    IObsBrowserSourceRecoveryScheduler recovery,
    ILogger<ReplyDispatcher> logger
) : IReplyDispatcher
{
    public async Task<ReplyOutcome> DispatchAsync(
        ReplyRequest request,
        CancellationToken cancellationToken = default
    )
    {
        bool sentToChat =
            request.Delivery.SendsToChat && await SendToChatAsync(request, cancellationToken);

        if (!request.Delivery.SendsToOverlay)
            return new ReplyOutcome(sentToChat, false, false);

        bool sentToOverlay = await SendToOverlayAsync(request, cancellationToken);
        return new ReplyOutcome(sentToChat, sentToOverlay, !sentToOverlay);
    }

    private async Task<bool> SendToChatAsync(ReplyRequest request, CancellationToken ct)
    {
        ReplyOrigin? origin = request.Origin;
        if (origin is null)
            return await chat.SendMessageAsync(request.BroadcasterId, request.Text, ct);

        if (string.IsNullOrEmpty(origin.MessageId))
            return (
                await originChat.SendMessageAsync(
                    request.BroadcasterId,
                    origin.Provider,
                    request.Text,
                    ct
                )
            ).IsSuccess;

        Result reply = await originChat.SendReplyAsync(
            request.BroadcasterId,
            origin.Provider,
            origin.MessageId,
            request.Text,
            ct
        );
        if (reply.IsSuccess)
            return true;

        ReplyOrMentionPlan fallback = ReplyOrMentionComposer.Compose(
            null,
            ChatMentionName.For(origin.UserDisplayName, origin.UserLogin),
            request.Text
        );
        return (
            await originChat.SendMessageAsync(
                request.BroadcasterId,
                origin.Provider,
                fallback.Message,
                ct
            )
        ).IsSuccess;
    }

    private async Task<bool> SendToOverlayAsync(ReplyRequest request, CancellationToken ct)
    {
        ChannelTwitchBot? bot = await bots.ResolveAsync(request.BroadcasterId, ct);
        string text = WithAttribution(
            request.OverlayText ?? request.Text,
            request.RequestedBy,
            request.Delivery.Html
        );
        ChatOverlayLine line = new(
            request.BroadcasterId,
            bot?.TwitchUserId ?? request.BroadcasterId.ToString(),
            bot?.Username ?? DbBotIdentityDirectory.FallbackName,
            request.Delivery.Html ? ChatHtmlSanitizer.Sanitize(text, _ => true) : text,
            request.Delivery.Html
        );

        ChatOverlayDelivery delivery = await overlay.SendAsync(line, ct);
        if (delivery.WasDelivered)
            return true;

        // A browser source that is not connected is an ordinary browser source failure: the recovery pass
        // reloads it when OBS control is active and logs a warning when it is not.
        logger.LogWarning(
            "Reply for channel {BroadcasterId} was meant for the chat overlay, but no chat overlay is open",
            request.BroadcasterId
        );
        recovery.Schedule(request.BroadcasterId);
        return false;
    }

    private static string WithAttribution(string text, string? requestedBy, bool html)
    {
        if (string.IsNullOrWhiteSpace(requestedBy))
            return text;

        return html
            ? $"{text}<br><small>requested by {WebUtility.HtmlEncode(requestedBy)}</small>"
            : $"{text} (requested by {requestedBy})";
    }
}
