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
using NomNomzBot.Application.Tts.Dtos;

namespace NomNomzBot.Application.Tts.Services;

/// <summary>Reads what a channel's TTS has spoken, newest first.</summary>
public interface ITtsHistoryService
{
    /// <summary>
    /// One page of the channel's spoken history. <paramref name="limit"/> is clamped to 1..100.
    /// <paramref name="before"/> is the id of the oldest row already seen; only older rows return.
    /// </summary>
    Task<Result<TtsHistoryPageDto>> ListAsync(
        Guid broadcasterId,
        int limit,
        int? before,
        CancellationToken cancellationToken = default
    );
}
