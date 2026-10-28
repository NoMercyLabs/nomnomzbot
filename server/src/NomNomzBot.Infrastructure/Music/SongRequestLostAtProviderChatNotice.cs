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

using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// Tells chat when a handed-over song request vanished from the provider's queue before it played. The
/// requester asked in chat, so chat is where they learn their song will not play — and the streamer sees
/// why the queue jumped to the next request rather than wondering whether it broke.
/// </summary>
public sealed class SongRequestLostAtProviderChatNotice
    : IEventHandler<SongRequestLostAtProviderEvent>
{
    private readonly IChatProvider _chat;

    public SongRequestLostAtProviderChatNotice(IChatProvider chat)
    {
        _chat = chat;
    }

    public async Task HandleAsync(
        SongRequestLostAtProviderEvent @event,
        CancellationToken cancellationToken = default
    ) =>
        await _chat.SendMessageAsync(
            @event.BroadcasterId,
            $"@{@event.RequestedBy} \"{@event.TrackName}\" is no longer in the player's queue (removed or skipped there), so song requests moved on to the next one.",
            cancellationToken
        );
}
