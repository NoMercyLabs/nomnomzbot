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
using NomNomzBot.Application.Community.Dtos;

namespace NomNomzBot.Application.Community.Services;

/// <summary>
/// Every Twitch poll and prediction the channel ever ran, read from the event journal (Twitch itself only returns
/// the most recent ones). Newest first.
/// </summary>
public interface IPollPredictionHistoryService
{
    Task<Result<IReadOnlyList<PollPredictionHistoryItemDto>>> ListAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    );
}
