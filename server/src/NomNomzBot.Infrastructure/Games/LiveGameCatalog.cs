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
using NomNomzBot.Application.Games;
using NomNomzBot.Application.Games.Services;

namespace NomNomzBot.Infrastructure.Games;

/// <summary>
/// The discovered game registry (live-games.md §3.2), built once from every assembly-scanned
/// <see cref="ILiveGame"/>. Two games claiming one <c>GameKey</c> is a programming error — the build (and
/// with it the host) fails fast rather than silently shadowing one game with another.
/// </summary>
public sealed class LiveGameCatalog : ILiveGameCatalog
{
    private readonly Dictionary<string, ILiveGame> _games;

    public LiveGameCatalog(IEnumerable<ILiveGame> games)
    {
        _games = new Dictionary<string, ILiveGame>(StringComparer.OrdinalIgnoreCase);
        foreach (ILiveGame game in games)
        {
            if (!_games.TryAdd(game.GameKey, game))
                throw new InvalidOperationException(
                    $"Duplicate live game key '{game.GameKey}' — every ILiveGame must have a unique GameKey."
                );
        }
        All = _games.ToDictionary(
            p => p.Key,
            p => p.Value.Manifest,
            StringComparer.OrdinalIgnoreCase
        );
    }

    public IReadOnlyDictionary<string, LiveGameManifest> All { get; }

    public bool TryGet(string gameKey, [NotNullWhen(true)] out ILiveGame? game) =>
        _games.TryGetValue(gameKey, out game);
}
