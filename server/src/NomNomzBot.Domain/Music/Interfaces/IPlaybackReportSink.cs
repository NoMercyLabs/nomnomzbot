// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
