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

/// <summary>When the player moves to a new track.</summary>
public sealed class TrackChangedEvent : DomainEventBase
{
    /// <summary>The title of the new track.</summary>
    public required string TrackName { get; init; }

    /// <summary>The artist of the new track.</summary>
    public required string Artist { get; init; }

    /// <summary>The id of the new track at the music service.</summary>
    public required string TrackUri { get; init; }

    /// <summary>A URL to the cover image of the album. Empty when there is no cover.</summary>
    public string? AlbumArtUrl { get; init; }

    /// <summary>How long the track is, in milliseconds.</summary>
    public required int DurationMs { get; init; }

    /// <summary>The music service that plays the track.</summary>
    public required string Provider { get; init; }
}
