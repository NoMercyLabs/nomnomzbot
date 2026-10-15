// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
