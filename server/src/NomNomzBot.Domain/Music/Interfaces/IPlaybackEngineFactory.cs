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
/// Builds the playback engines of one channel. An engine is bound to one channel, so the playback director
/// of that channel receives its own pair.
/// </summary>
public interface IPlaybackEngineFactory
{
    /// <summary>One engine per <see cref="ValueObjects.EngineKind"/>, each over the channel's own provider.</summary>
    IReadOnlyList<IPlaybackEngine> Create(Guid broadcasterId);
}
