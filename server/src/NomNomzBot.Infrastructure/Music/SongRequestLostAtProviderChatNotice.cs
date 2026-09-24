// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
