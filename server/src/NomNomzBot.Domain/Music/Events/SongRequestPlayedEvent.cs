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

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Music.Events;

/// <summary>
/// A song request left the queue because the music service was seen playing it. Only now does the request
/// count on the leaderboards: a song still waiting when the stream ends has not been played.
/// </summary>
public sealed class SongRequestPlayedEvent : DomainEventBase
{
    /// <summary>The queue row's id: its short code, or track and requester for a row without one.</summary>
    public required string ItemId { get; init; }

    /// <summary>The id of the track at the music service.</summary>
    public required string TrackUri { get; init; }
}
