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

/// <summary>The music level the director holds for a channel: the volume (0 to 100) and whether it is muted.</summary>
public sealed record PlaybackLevel(int Volume, bool Muted)
{
    /// <summary>The level of a channel that has no director yet.</summary>
    public static PlaybackLevel Default { get; } = new(100, false);
}
