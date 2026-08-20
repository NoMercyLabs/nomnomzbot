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
/// Fired the moment the currently playing track is saved to or removed from the streamer's Liked
/// Songs — whether triggered from the dashboard, a Stream Deck action, or Spotify itself. A
/// transient signal (not part of the standing <see cref="PlaybackStateChangedEvent"/> snapshot) for
/// the overlay's heart-like animation: it should pulse the instant the like happens, not wait for the
/// next playback poll.
/// </summary>
public sealed class TrackSavedChangedEvent : DomainEventBase
{
    public required string TrackUri { get; init; }
    public string? TrackName { get; init; }
    public string? Artist { get; init; }
    public required bool IsSaved { get; init; }
}
