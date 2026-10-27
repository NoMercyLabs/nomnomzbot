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
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Domain.Commands.ValueObjects;

namespace NomNomzBot.Application.Chat.Services;

/// <summary>A reply template resolved for each place it will be shown.</summary>
/// <param name="ChatText">The text for the platform chat; empty when the reply does not go to chat.</param>
/// <param name="OverlayText">The text for the chat overlay; null when the reply does not go to the overlay.</param>
public sealed record RenderedReply(string ChatText, string? OverlayText);

/// <summary>
/// Resolves a reply template once per place the reply goes, so the platform chat keeps plain text while the
/// chat overlay can render HTML without a viewer's name or arguments being able to inject markup.
/// </summary>
public static class ReplyTemplateRenderer
{
    public static async Task<RenderedReply> RenderAsync(
        ITemplateResolver resolver,
        string template,
        IDictionary<string, string> variables,
        Guid broadcasterId,
        ReplyDelivery delivery,
        CancellationToken cancellationToken = default
    )
    {
        if (delivery.Target == ReplyTarget.None)
            return new RenderedReply(string.Empty, null);

        string chatText = delivery.SendsToChat
            ? await resolver.ResolveAsync(
                template,
                ChatMentionName.ForChat(variables),
                broadcasterId,
                cancellationToken
            )
            : string.Empty;
        if (!delivery.SendsToOverlay)
            return new RenderedReply(chatText, null);

        // No HTML: the overlay shows the chat text as it is, so a reply for both places resolves once.
        if (!delivery.Html && delivery.SendsToChat)
            return new RenderedReply(chatText, chatText);

        IDictionary<string, string> overlayVariables = delivery.Html
            ? HtmlEncoded(variables)
            : new Dictionary<string, string>(variables, StringComparer.OrdinalIgnoreCase);
        string overlayText = await resolver.ResolveAsync(
            template,
            overlayVariables,
            broadcasterId,
            cancellationToken
        );
        return new RenderedReply(chatText, overlayText);
    }

    /// <summary>
    /// A copy of <paramref name="variables"/> with every value HTML-encoded, so a viewer's name or arguments
    /// land in the authored markup as text and can never open a tag.
    /// </summary>
    private static Dictionary<string, string> HtmlEncoded(IDictionary<string, string> variables)
    {
        Dictionary<string, string> encoded = new(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string> pair in variables)
            encoded[pair.Key] = WebUtility.HtmlEncode(pair.Value);
        return encoded;
    }
}
