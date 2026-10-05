// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Abstractions.Pipeline;

/// <summary>
/// Tells the channel's chat when an ad break is over, like the old bot did. An ad break is scheduled when it
/// begins and the end line goes out once its duration has passed, as long as the same stream is still live. The
/// pending list lives in memory.
/// </summary>
public interface IAdBreakEndScheduler
{
    /// <summary>Remembers that an ad break began, so its end line is due after <paramref name="durationSeconds"/>.</summary>
    void Schedule(Guid broadcasterId, DateTimeOffset startedAt, int durationSeconds);

    /// <summary>Sends the end line of every ad break whose duration has passed and whose stream is still live.</summary>
    Task ProcessDueAsync(CancellationToken cancellationToken);
}
