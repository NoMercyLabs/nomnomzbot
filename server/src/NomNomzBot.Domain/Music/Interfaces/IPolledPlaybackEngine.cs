// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Music.Interfaces;

/// <summary>
/// An engine whose player has no push: it learns what plays only by polling. Each poll result goes in here and
/// comes out as engine reports and track ends.
/// </summary>
public interface IPolledPlaybackEngine : IPlaybackEngine
{
    /// <summary>Takes one poll result. Null means nothing is playing.</summary>
    Task ObserveAsync(TrackInfo? polled);
}
