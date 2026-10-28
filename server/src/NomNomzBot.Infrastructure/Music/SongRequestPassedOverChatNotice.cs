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
/// Tells chat when a request waits for the Music Player page and the requests behind it play first, so the
/// wait is never silent.
/// </summary>
public sealed class SongRequestPassedOverChatNotice(IChatProvider chat)
    : IEventHandler<SongRequestPassedOverEvent>
{
    public Task HandleAsync(
        SongRequestPassedOverEvent @event,
        CancellationToken cancellationToken = default
    ) =>
        chat.SendMessageAsync(
            @event.BroadcasterId,
            $"\"{@event.TrackName}\" is waiting for the Music Player page to open, so the next requests play first.",
            cancellationToken
        );
}
