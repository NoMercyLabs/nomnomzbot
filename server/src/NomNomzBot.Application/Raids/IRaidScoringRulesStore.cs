// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
