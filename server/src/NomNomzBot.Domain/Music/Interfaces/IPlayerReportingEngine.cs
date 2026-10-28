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
/// An engine whose player pushes its own state: the overlay player reports each change. Each report goes in
/// here and comes out as engine reports and track ends.
/// </summary>
public interface IPlayerReportingEngine : IPlaybackEngine
{
    /// <summary>Takes one report from the player.</summary>
    Task ReportAsync(YouTubePlayerReport report);
}
