// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Chat.EventHandlers;

/// <summary>
/// Learns each chatter's chat colour from their own messages and stores it in <see cref="IChatColorMemory"/>, so the
/// mention step can later colour an <c>@mention</c> of that user (chat-decoration spec §3.1). A message with no colour
/// set is a no-op. Auto-discovered as an <see cref="IEventHandler{TEvent}"/>; runs alongside the broadcast/decoration handler.
/// </summary>
public sealed class ChatColorRecorder : IEventHandler<ChatMessageReceivedEvent>
{
    private readonly IChatColorMemory _colors;

    public ChatColorRecorder(IChatColorMemory colors)
    {
        _colors = colors;
    }

    public Task HandleAsync(
        ChatMessageReceivedEvent @event,
        CancellationToken cancellationToken = default
    ) =>
        _colors.RememberAsync(
            @event.BroadcasterId,
            @event.UserId,
            @event.ColorHex,
            cancellationToken
        );
}
