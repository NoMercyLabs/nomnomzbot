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
/// Where a player watcher hands what it saw to the channel's engine, so the engine (not the watcher) turns it
/// into reports and track ends for the playback director. A channel with no playback director yet is a no-op.
/// </summary>
public interface IEngineFeed
{
    /// <summary>Gives the Spotify engine of the channel one poll result. Null means nothing is playing.</summary>
    Task ObservePollAsync(Guid broadcasterId, TrackInfo? polled, CancellationToken ct = default);
}
