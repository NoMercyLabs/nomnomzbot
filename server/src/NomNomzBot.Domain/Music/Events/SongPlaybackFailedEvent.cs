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
/// The overlay player could not play a track (embedding disabled, removed, private, blocked in the
/// streamer's country). The queue moves on to the next request; this event exists so the skip is announced
/// instead of someone's song silently vanishing.
/// </summary>
public sealed class SongPlaybackFailedEvent : DomainEventBase
{
    /// <summary>The id of the track at the music service.</summary>
    public required string TrackUri { get; init; }

    /// <summary>The title of the track; empty when the player was never told it.</summary>
    public required string TrackName { get; init; }
}
