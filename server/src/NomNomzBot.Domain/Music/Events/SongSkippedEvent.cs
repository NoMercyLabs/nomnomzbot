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

/// <summary>When the current song is skipped.</summary>
public sealed class SongSkippedEvent : DomainEventBase
{
    /// <summary>The internal user id (a Guid as text) of the person who skipped the song, on every path. Empty when no user resolves (a timer or an automation token).</summary>
    public required string SkippedByUserId { get; init; }

    /// <summary>The title of the skipped track.</summary>
    public required string TrackName { get; init; }
}
