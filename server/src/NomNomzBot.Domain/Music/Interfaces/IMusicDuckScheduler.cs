// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
