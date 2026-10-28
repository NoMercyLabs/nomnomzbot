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
