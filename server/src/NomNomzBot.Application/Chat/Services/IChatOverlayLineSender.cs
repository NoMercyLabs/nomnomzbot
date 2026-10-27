// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Chat.Services;

/// <summary>
/// Puts one bot-authored line on the channel's chat overlay (the OBS chat widgets) and nowhere else: not the
/// platform chat, and not the dashboard chat feed (the dashboard never speaks as the bot).
/// </summary>
public interface IChatOverlayLineSender
{
    /// <summary>
    /// Pushes <paramref name="line"/> to the chat widgets that are open right now. When no chat widget is
    /// attached nothing is pushed and the result is <see cref="ChatOverlayDelivery.NoChatOverlayOpen"/>.
    /// </summary>
    Task<ChatOverlayDelivery> SendAsync(
        ChatOverlayLine line,
        CancellationToken cancellationToken = default
    );
}

/// <summary>One line for the chat overlay, spoken by the channel's bot identity.</summary>
/// <param name="BroadcasterId">The tenant the overlay belongs to.</param>
/// <param name="BotUserId">The platform user id of the bot identity the line is shown under.</param>
/// <param name="BotDisplayName">The display name of that bot identity.</param>
/// <param name="Text">The line. Already sanitized HTML when <paramref name="IsHtml"/> is true, plain text otherwise.</param>
/// <param name="IsHtml">True when <paramref name="Text"/> is sanitized HTML for the widget's <c>html</c> fragment.</param>
public sealed record ChatOverlayLine(
    Guid BroadcasterId,
    string BotUserId,
    string BotDisplayName,
    string Text,
    bool IsHtml
);

/// <summary>How many chat widgets the overlay line reached.</summary>
public sealed record ChatOverlayDelivery(int WidgetsReached)
{
    /// <summary>No chat widget was open: nothing was pushed.</summary>
    public static ChatOverlayDelivery NoChatOverlayOpen { get; } = new(0);

    public bool WasDelivered => WidgetsReached > 0;
}
