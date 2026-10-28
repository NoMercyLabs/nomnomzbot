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
