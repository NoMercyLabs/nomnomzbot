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

using NomNomzBot.Application.Chat.Decoration;
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
    IReadOnlyList<ResolvedChatBadge> Badges,
    string? ReplyParentMessageId,
    DateTimeOffset Timestamp
);
