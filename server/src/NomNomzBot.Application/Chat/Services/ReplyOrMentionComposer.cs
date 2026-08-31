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
/// The outcome of <see cref="ReplyOrMentionComposer.Compose"/> — either thread the send as a native
/// reply to <see cref="ReplyToMessageId"/> with <see cref="Message"/> left untouched, or send a plain
/// message that carries an inline "@displayName" prefix when no native reply mechanism applies.
/// </summary>
public readonly record struct ReplyOrMentionPlan(string? ReplyToMessageId, string Message)
{
    /// <summary>True when the plan threads a native platform reply rather than an @mention prefix.</summary>
    public bool IsNativeReply => ReplyToMessageId is not null;
}

/// <summary>
/// The shared "reply to the triggering message when a native reply mechanism is available (e.g.
/// Twitch's <c>reply_parent_message_id</c>), otherwise @mention the user" decision used by every send
/// path that answers a specific inbound trigger. Before this helper the same
/// <c>"@{displayName} {message}"</c> mention-prefix format and the same "no reply target → plain send"
/// rule were duplicated across <c>SendReplyAction</c> (pipeline "send reply" action) and
/// <c>ChatMessageHandler.SendResponseAsync</c> (command/built-in response path) — consolidated here so a
/// future platform quirk in the mention format only needs a single fix.
/// </summary>
public static class ReplyOrMentionComposer
{
    /// <summary>
    /// Chooses the send plan for the given trigger. When <paramref name="replyToMessageId"/> is a
    /// non-empty native reply target, the plan threads the send as a reply and leaves
    /// <paramref name="message"/> untouched. Otherwise the plan sends a plain message with an
    /// <c>"@displayName"</c> prefix so the recipient is still addressed.
    /// </summary>
    public static ReplyOrMentionPlan Compose(
        string? replyToMessageId,
        string displayName,
        string message
    ) =>
        string.IsNullOrEmpty(replyToMessageId)
            ? new ReplyOrMentionPlan(null, $"@{displayName} {message}")
            : new ReplyOrMentionPlan(replyToMessageId, message);
}
