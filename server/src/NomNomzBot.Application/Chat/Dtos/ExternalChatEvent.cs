// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Chat.ValueObjects;

namespace NomNomzBot.Application.Chat.Dtos;

/// <summary>What happened in an off-bot channel the viewer reads.</summary>
public enum ExternalChatEventKind
{
    Message,
    MessageDeleted,
    ChatCleared,
}

/// <summary>
/// One live chat event from a Twitch channel the bot does not serve, read on the viewer's own token and pushed
/// only to that viewer's connection. Fields a kind does not carry stay null or empty.
/// </summary>
public sealed record ExternalChatEvent(
    ExternalChatEventKind Kind,
    string BroadcasterTwitchId,
    string? MessageId,
    string? ChatterTwitchId,
    string? ChatterLogin,
    string? ChatterDisplayName,
    string? Color,
    string Text,
    IReadOnlyList<ChatMessageFragment> Fragments,
    IReadOnlyList<ChatBadge> Badges,
    string? ReplyParentMessageId,
    DateTimeOffset Timestamp
);
