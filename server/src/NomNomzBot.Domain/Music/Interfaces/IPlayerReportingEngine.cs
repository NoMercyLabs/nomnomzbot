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
/// An engine whose player pushes its own state: the overlay player reports each change. Each report goes in
/// here and comes out as engine reports and track ends.
/// </summary>
public interface IPlayerReportingEngine : IPlaybackEngine
{
    /// <summary>Takes one report from the player.</summary>
    Task ReportAsync(YouTubePlayerReport report);
}
