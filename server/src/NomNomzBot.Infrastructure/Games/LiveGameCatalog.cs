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
        _games = new(StringComparer.OrdinalIgnoreCase);
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
