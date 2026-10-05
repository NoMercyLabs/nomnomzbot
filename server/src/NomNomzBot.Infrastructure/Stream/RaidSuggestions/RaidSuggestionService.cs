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
using NomNomzBot.Application.Raids;

namespace NomNomzBot.Infrastructure.Stream.RaidSuggestions;

/// <summary>
/// Ranks live raid candidates for one channel with that channel's own stored rules and its raid history.
/// The channel's rules come from <see cref="IRaidScoringRulesStore"/>, never from a constant.
/// </summary>
internal sealed class RaidSuggestionService(
    IRaidHistoryReader history,
    IRaidScoringRulesStore rules
)
{
    public async Task<Result<List<RaidScoreResult>>> RankAsync(
        Guid channelId,
        string ownTwitchUserId,
        IReadOnlyList<RaidCandidate> candidates,
        DateTimeOffset now,
        CancellationToken cancellationToken = default
    )
    {
        Result<RaidHistory> raids = await history.GetHistoryAsync(
            channelId,
            ownTwitchUserId,
            cancellationToken
        );
        if (raids.IsFailure)
            return Result.Failure<List<RaidScoreResult>>(
                raids.ErrorMessage,
                raids.ErrorCode,
                raids.ErrorDetail
            );

        RaidScoringRules channelRules = await rules.GetAsync(channelId, cancellationToken);
        return Result.Success(
            RaidScoring.Rank(
                candidates,
                raids.Value.Outgoing,
                raids.Value.Incoming,
                channelRules,
                now
            )
        );
    }
}
