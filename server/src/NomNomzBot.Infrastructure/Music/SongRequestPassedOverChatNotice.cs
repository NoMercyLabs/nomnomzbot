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
