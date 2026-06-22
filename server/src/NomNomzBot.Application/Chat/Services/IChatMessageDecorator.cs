// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
