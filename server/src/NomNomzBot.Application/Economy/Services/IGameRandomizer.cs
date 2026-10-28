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

namespace NomNomzBot.Application.Economy.Services;

/// <summary>
/// The randomness source for game outcome rolls (economy.md §3.5) — abstracted so the production CSPRNG can be
/// swapped for a deterministic source in tests.
/// </summary>
public interface IGameRandomizer
{
    /// <summary>A uniformly-distributed value in <c>[0, 1)</c>.</summary>
    double NextUnitInterval();
}
