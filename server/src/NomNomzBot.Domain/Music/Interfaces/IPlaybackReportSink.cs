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

using NomNomzBot.Domain.Music.ValueObjects;

namespace NomNomzBot.Domain.Music.Interfaces;

/// <summary>
/// Where an engine report goes. The playback director is the only thing that changes the song queue;
/// anything that watches a player passes what it saw here and changes nothing itself.
/// </summary>
public interface IPlaybackReportSink
{
    /// <summary>Hands one engine report or track end for the channel to its playback director.</summary>
    Task ReportAsync(Guid broadcasterId, EngineSignal signal, CancellationToken ct = default);
}
