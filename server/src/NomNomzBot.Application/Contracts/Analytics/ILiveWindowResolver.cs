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

namespace NomNomzBot.Application.Contracts.Analytics;

/// <summary>
/// Resolves whether a channel was live at a point in time, from the durable stream history (analytics.md §1.1).
/// The watch-session projection gates on this — a session only opens for activity inside a live window. Backed by
/// the recorded stream start/end times, so it is historically accurate on replay (no stream-event subscription).
/// </summary>
public interface ILiveWindowResolver
{
    /// <summary>The id of the stream covering <paramref name="at"/> for the channel, or null if it was not live.</summary>
    Task<string?> GetCoveringStreamIdAsync(
        Guid broadcasterId,
        DateTime at,
        CancellationToken ct = default
    );
}
