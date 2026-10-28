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

namespace NomNomzBot.Application.Music.Services;

/// <summary>
/// What retracting one song request did. The provider (Spotify) cannot delete from its own queue, so a
/// request it already holds can only be skipped when it starts — callers must tell the viewer which of the
/// two happened.
/// </summary>
public enum RequestRetraction
{
    /// <summary>No request at that position; nothing changed.</summary>
    NotRemoved,

    /// <summary>Dropped from the fair queue. The provider does not hold it, so it will never play.</summary>
    Removed,

    /// <summary>
    /// Dropped from the fair queue, and the provider's live queue still lists it. It is armed to be skipped
    /// the moment it starts; every other song in the provider's queue is left alone.
    /// </summary>
    RemovedWillSkip,

    /// <summary>
    /// Dropped from the fair queue, and the provider is playing it right now, so a marker would never fire:
    /// the player was told to skip it at once.
    /// </summary>
    RemovedAndSkippedNow,
}
