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

namespace NomNomzBot.Domain.Music.ValueObjects;

/// <summary>What the overlay YouTube player says it is doing.</summary>
public enum YouTubePlayerState
{
    Playing,
    Paused,
    Ended,
    Error,

    /// <summary>The video plays and has 3 seconds left. Sent once; stored as <see cref="Playing"/>.</summary>
    Ending,
}
