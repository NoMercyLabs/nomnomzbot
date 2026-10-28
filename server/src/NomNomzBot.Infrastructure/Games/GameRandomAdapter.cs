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

using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Games;

namespace NomNomzBot.Infrastructure.Games;

/// <summary>
/// The <see cref="IGameRandom"/> a game sees, backed by the economy's <see cref="IGameRandomizer"/> —
/// CSPRNG in production, a fixed fake in tests, so game odds stay fair AND deterministic under test.
/// </summary>
public sealed class GameRandomAdapter(IGameRandomizer randomizer) : IGameRandom
{
    public int Next(int maxExclusive) =>
        maxExclusive <= 0 ? 0 : (int)(randomizer.NextUnitInterval() * maxExclusive);

    public double NextDouble() => randomizer.NextUnitInterval();

    public bool Roll(double percent) => randomizer.NextUnitInterval() * 100.0 < percent;
}
