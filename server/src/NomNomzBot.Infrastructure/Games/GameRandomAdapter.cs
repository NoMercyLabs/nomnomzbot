// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
