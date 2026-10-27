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
