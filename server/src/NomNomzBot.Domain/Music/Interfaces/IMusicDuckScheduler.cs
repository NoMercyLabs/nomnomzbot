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
/// Lowers a channel's music while bot audio speaks. The overlay plays spoken lines strictly in order, so lines
/// that queue up hold one duck until the last line has finished.
/// </summary>
public interface IMusicDuckScheduler
{
    /// <summary>
    /// Ducks the channel's music to <paramref name="percent"/> of its level for <paramref name="durationMs"/>,
    /// counted after any line already speaking. A percent of 100 or more, or a length of 0, does nothing.
    /// </summary>
    Task DuckAsync(Guid broadcasterId, int percent, int durationMs, CancellationToken ct = default);
}
