// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
