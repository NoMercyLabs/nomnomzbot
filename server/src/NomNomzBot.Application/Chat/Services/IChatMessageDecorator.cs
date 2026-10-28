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
using NomNomzBot.Domain.Chat.Events;

namespace NomNomzBot.Application.Chat.Services;

/// <summary>
/// The thin orchestrator that turns an inbound chat event into an enriched message (chat-decoration spec §0/§3.1): it
/// seeds a <see cref="ChatDecorationContext"/> from the event and runs the ordered <see cref="IChatDecorationAdapter"/>
/// chain. Best-effort — a provider/cache miss or a throwing adapter never fails the call; the message still emits.
/// </summary>
public interface IChatMessageDecorator
{
    Task<DecoratedChatMessage> DecorateAsync(
        ChatMessageReceivedEvent message,
        CancellationToken ct = default
    );
}
