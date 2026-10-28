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
