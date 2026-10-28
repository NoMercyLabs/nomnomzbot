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

namespace NomNomzBot.Domain.Music.Interfaces;

/// <summary>
/// Builds the playback engines of one channel. An engine is bound to one channel, so the playback director
/// of that channel receives its own pair.
/// </summary>
public interface IPlaybackEngineFactory
{
    /// <summary>One engine per <see cref="ValueObjects.EngineKind"/>, each over the channel's own provider.</summary>
    IReadOnlyList<IPlaybackEngine> Create(Guid broadcasterId);
}
