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
/// An engine whose player has no push: it learns what plays only by polling. Each poll result goes in here and
/// comes out as engine reports and track ends.
/// </summary>
public interface IPolledPlaybackEngine : IPlaybackEngine
{
    /// <summary>Takes one poll result. Null means nothing is playing.</summary>
    Task ObserveAsync(TrackInfo? polled);
}
