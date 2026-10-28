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
/// A song request was handed to the provider but disappeared from the provider's queue without ever being
/// seen playing — typically the streamer removed it by hand in the Spotify app, or skipped it faster than
/// the playback poller could see it. The bot has already dropped it
/// from the fair queue and moved on to the next request; this event exists so the change is announced
/// instead of the queue silently skipping someone's song. Internal until a detector raises it again: the
/// playback director starts the head directly, so nothing waits in a provider queue to be lost.
/// </summary>
[Event(visibility: EventVisibility.Internal)]
public sealed class SongRequestLostAtProviderEvent : DomainEventBase
{
    /// <summary>The id of the requested track at the music service.</summary>
    public required string TrackUri { get; init; }

    /// <summary>The title of the requested track.</summary>
    public required string TrackName { get; init; }

    /// <summary>The name of the viewer who requested the song.</summary>
    public required string RequestedBy { get; init; }
}
