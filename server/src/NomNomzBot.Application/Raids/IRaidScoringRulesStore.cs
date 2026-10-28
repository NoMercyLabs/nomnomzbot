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

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Raids;

/// <summary>
/// Keeps one <see cref="RaidScoringRules"/> per channel. A channel that never saved rules gets
/// <see cref="RaidScoringRules.Neutral"/>.
/// </summary>
public interface IRaidScoringRulesStore
{
    Task<RaidScoringRules> GetAsync(Guid channelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates with <see cref="RaidScoringRulesValidator"/> and stores the rules. A rule that breaks a
    /// limit fails with <c>VALIDATION_FAILED</c> and stores nothing.
    /// </summary>
    Task<Result<RaidScoringRules>> SaveAsync(
        Guid channelId,
        RaidScoringRules rules,
        CancellationToken cancellationToken = default
    );
}
