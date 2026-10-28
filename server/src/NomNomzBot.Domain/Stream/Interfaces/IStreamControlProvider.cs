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
