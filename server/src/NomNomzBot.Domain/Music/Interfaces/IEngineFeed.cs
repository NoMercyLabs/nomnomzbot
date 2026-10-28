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

using NomNomzBot.Domain.Music.ValueObjects;

namespace NomNomzBot.Domain.Music.Interfaces;

/// <summary>
/// Where a player watcher hands what it saw to the channel's engine, so the engine (not the watcher) turns it
/// into reports and track ends for the playback director. A channel with no playback director yet is a no-op.
/// </summary>
public interface IEngineFeed
{
    /// <summary>Gives the Spotify engine of the channel one poll result. Null means nothing is playing.</summary>
    Task ObservePollAsync(Guid broadcasterId, TrackInfo? polled, CancellationToken ct = default);

    /// <summary>
    /// Gives the YouTube engine of the channel one report from the overlay player. True when an engine took
    /// it, so the director now owns what follows; false when the channel has no director or no such engine.
    /// </summary>
    Task<bool> ReportPlayerAsync(
        Guid broadcasterId,
        YouTubePlayerReport report,
        CancellationToken ct = default
    );
}
