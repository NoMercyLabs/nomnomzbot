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

/// <summary>The five director states (music-playback-determinism.md section 2, States).</summary>
public enum PlaybackStateKind
{
    Idle,
    Starting,
    Playing,
    PausedByOwner,
    Blocked,
}
