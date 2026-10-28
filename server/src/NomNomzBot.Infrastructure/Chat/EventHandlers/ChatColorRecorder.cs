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

using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Chat.EventHandlers;

/// <summary>
/// Learns each chatter's chat colour from their own messages and stores it in <see cref="IChatColorMemory"/>, so the
/// mention step can later colour an <c>@mention</c> of that user (chat-decoration spec §3.1). A message with no colour
/// set is a no-op. Auto-discovered as an <see cref="IEventHandler{TEvent}"/>; runs alongside the broadcast/decoration handler.
/// </summary>
public sealed class ChatColorRecorder
    : IEventHandler<ChatMessageReceivedEvent>,
        IOnboardingExemptHandler
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
