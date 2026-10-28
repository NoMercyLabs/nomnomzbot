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

/// <summary>The music level the director holds for a channel: the volume (0 to 100) and whether it is muted.</summary>
public sealed record PlaybackLevel(int Volume, bool Muted)
{
    /// <summary>The level of a channel that has no director yet.</summary>
    public static PlaybackLevel Default { get; } = new(100, false);
}
