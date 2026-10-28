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
