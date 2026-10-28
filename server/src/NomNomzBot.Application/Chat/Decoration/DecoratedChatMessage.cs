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

using NomNomzBot.Domain.Chat.ValueObjects;

namespace NomNomzBot.Application.Chat.Decoration;

/// <summary>
/// The output of the decoration pipeline (chat-decoration spec §0): the message's fragments after enrichment — emotes
/// resolved to the unified <see cref="ChatEmote"/> shape and text re-imploded. It grows to carry resolved badges and
/// other message-level enrichment as those adapters land; today it carries the enriched fragment list the broadcaster
/// maps to the client DTO.
/// </summary>
public sealed class DecoratedChatMessage
{
    public required IReadOnlyList<ChatMessageFragment> Fragments { get; init; }

    /// <summary>The message's badges resolved to image urls (chat-decoration spec §3.3).</summary>
    public IReadOnlyList<ResolvedChatBadge> Badges { get; init; } = [];
}
