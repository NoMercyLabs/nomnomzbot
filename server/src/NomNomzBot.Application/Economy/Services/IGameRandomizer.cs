// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
