// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Chat.Services;

/// <summary>
/// Sends a chat message AS THE LOGGED-IN OPERATOR (chat-client.md §3.3) — the dashboard composer's send verb,
/// distinct from the bot's <c>IChatProvider.SendMessageAsync</c> (which stays for automation/announcements).
/// It posts <c>/helix/chat/messages</c> on the <em>operator's own</em> Twitch token with the operator's Twitch
/// user id as <c>sender_id</c>, so a moderator typing in a channel they moderate appears as themselves, not the
/// bot. The target channel and the operator identity travel in; Twitch is the authority on whether the operator
/// may speak there (a ban/timeout surfaces as a typed failure, never a silent success).
/// </summary>
public interface IOperatorChatSender
{
    /// <summary>
    /// Sends <paramref name="message"/> to <paramref name="broadcasterId"/> as <paramref name="operatorUserId"/>.
    /// Optional <paramref name="replyToMessageId"/> makes it a reply. Returns a failure (never throws) when the
    /// operator has no linked Twitch identity, the channel is unknown, or Twitch rejects the send.
    /// </summary>
    Task<Result> SendAsUserAsync(
        Guid operatorUserId,
        Guid broadcasterId,
        string message,
        string? replyToMessageId,
        CancellationToken ct = default
    );
}
