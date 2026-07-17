// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;

namespace NomNomzBot.Application.Games.Services;

/// <summary>
/// The discovered game registry (live-games.md §3.2), built from every assembly-scanned
/// <see cref="ILiveGame"/> at startup. A duplicate <c>GameKey</c> fails the build fast — two games cannot
/// claim one key.
/// </summary>
public interface ILiveGameCatalog
{
    /// <summary>Every discovered game's manifest, keyed by its <c>GameKey</c>.</summary>
    IReadOnlyDictionary<string, LiveGameManifest> All { get; }

    bool TryGet(string gameKey, [NotNullWhen(true)] out ILiveGame? game);
}
