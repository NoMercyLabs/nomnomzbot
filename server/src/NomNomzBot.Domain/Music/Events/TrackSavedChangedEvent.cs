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
/// Fired the moment the currently playing track is saved to or removed from the streamer's Liked
/// Songs — whether triggered from the dashboard, a Stream Deck action, or Spotify itself. A
/// transient signal (not part of the standing <see cref="PlaybackStateChangedEvent"/> snapshot) for
/// the overlay's heart-like animation: it should pulse the instant the like happens, not wait for the
/// next playback poll.
/// </summary>
public sealed class TrackSavedChangedEvent : DomainEventBase
{
    /// <summary>The id of the track at the music service.</summary>
    public required string TrackUri { get; init; }

    /// <summary>The title of the track. Empty when it is not known.</summary>
    public string? TrackName { get; init; }

    /// <summary>The artist of the track. Empty when it is not known.</summary>
    public string? Artist { get; init; }

    /// <summary>True when the track is saved in the library now, and false when it was removed.</summary>
    public required bool IsSaved { get; init; }
}
