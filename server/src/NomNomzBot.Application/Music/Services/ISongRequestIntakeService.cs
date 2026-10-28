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

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Music.Services;

/// <summary>
/// Opens or closes the channel's song queue to new requests (<c>!closequeue</c> / <c>!openqueue</c> and the
/// dashboard button). The music keeps playing and the queued songs stay; only new requests are refused.
/// </summary>
public interface ISongRequestIntakeService
{
    /// <summary>
    /// Sets the open state. The value is <c>true</c> when the state changed and <c>false</c> when the queue
    /// was already in the asked state.
    /// </summary>
    Task<Result<bool>> SetRequestsOpenAsync(
        string broadcasterId,
        bool open,
        CancellationToken cancellationToken = default
    );
}
