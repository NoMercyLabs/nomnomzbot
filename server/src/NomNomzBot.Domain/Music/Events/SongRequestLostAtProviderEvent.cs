// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Music.Events;

/// <summary>
/// A song request was handed to the provider but disappeared from the provider's queue without ever being
/// seen playing — typically the streamer removed it by hand in the Spotify app, or skipped it faster than
/// the playback poller could see it. The bot has already dropped it
/// from the fair queue and moved on to the next request; this event exists so the change is announced
/// instead of the queue silently skipping someone's song.
/// </summary>
public sealed class SongRequestLostAtProviderEvent : DomainEventBase
{
    public required string TrackUri { get; init; }
    public required string TrackName { get; init; }
    public required string RequestedBy { get; init; }
}
