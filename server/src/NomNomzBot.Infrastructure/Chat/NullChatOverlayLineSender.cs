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

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>
/// Stand-in for a host with no overlay hub: no chat widget can be open, so every line reports
/// <see cref="ChatOverlayDelivery.NoChatOverlayOpen"/>. The API host replaces it with the hub-backed sender.
/// </summary>
public sealed class NullChatOverlayLineSender : IChatOverlayLineSender
{
    public Task<ChatOverlayDelivery> SendAsync(
        ChatOverlayLine line,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(ChatOverlayDelivery.NoChatOverlayOpen);
}
