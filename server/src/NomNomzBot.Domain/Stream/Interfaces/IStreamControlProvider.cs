// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Stream.Interfaces;

/// <summary>
/// Abstraction for stream control operations (title, game, clips, commercials).
/// </summary>
public interface IStreamControlProvider
{
    Task UpdateTitleAsync(
        string broadcasterId,
        string title,
        CancellationToken cancellationToken = default
    );

    Task UpdateGameAsync(
        string broadcasterId,
        string gameId,
        CancellationToken cancellationToken = default
    );

    Task<string?> CreateClipAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    );

    Task StartCommercialAsync(
        string broadcasterId,
        int durationSeconds,
        CancellationToken cancellationToken = default
    );
}
