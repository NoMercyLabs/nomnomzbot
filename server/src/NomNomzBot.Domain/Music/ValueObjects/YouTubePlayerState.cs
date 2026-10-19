// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Music.ValueObjects;

/// <summary>What the overlay YouTube player says it is doing.</summary>
public enum YouTubePlayerState
{
    Playing,
    Paused,
    Ended,
    Error,

    /// <summary>The video plays and has 3 seconds left. Sent once; stored as <see cref="Playing"/>.</summary>
    Ending,
}
