// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Raids;

namespace NomNomzBot.Infrastructure.Stream.RaidSuggestions;

/// <summary>
/// Pure raid-target ranking, no I/O. The scorer holds no preference of its own: the channel's
/// <see cref="RaidScoringRules"/> say which categories, signals, sizes, raid gaps and raid balances
/// earn points. A rule worth 0 adds no reason.
/// </summary>
internal static class RaidScoring
{
    /// <summary>Scores every candidate and sorts best first. Equal scores keep their input order.</summary>
    public static List<RaidScoreResult> Rank(
        IReadOnlyList<RaidCandidate> candidates,
        IReadOnlyDictionary<string, RaidStats> outgoing,
        IReadOnlyDictionary<string, RaidStats> incoming,
        RaidScoringRules rules,
        DateTimeOffset now
    ) =>
        candidates
            .Select(c =>
                Score(
                    c,
                    outgoing.GetValueOrDefault(c.Stream.UserId),
                    incoming.GetValueOrDefault(c.Stream.UserId),
                    rules,
                    now
                )
            )
            .OrderByDescending(r => r.Score)
            .ToList();

    public static RaidScoreResult Score(
        RaidCandidate candidate,
        RaidStats? outgoing,
        RaidStats? incoming,
        RaidScoringRules rules,
        DateTimeOffset now
    )
    {
        List<RaidScoreReason> reasons = [];
        AddIfWeighted(reasons, ScoreCategory(candidate.Stream, rules));
        AddIfWeighted(reasons, ScoreContentSignal(candidate.Stream, rules));
        AddIfWeighted(reasons, ScoreViewerCount(candidate.Stream.ViewerCount, rules));
        AddIfWeighted(reasons, ScoreRaidCooldown(outgoing?.LastAt, rules.Recency, now));

        if (candidate.IsFollowed)
            AddIfWeighted(reasons, new RaidScoreReason(rules.FollowedWeight, "followed"));

        AddIfWeighted(
            reasons,
            ScoreReciprocity(incoming?.Count ?? 0, outgoing?.Count ?? 0, rules.Balance)
        );

        return new RaidScoreResult(
            candidate.Stream,
            candidate.IsFollowed,
            reasons.Sum(r => r.Delta),
            reasons,
            string.Join(
                ", ",
                reasons.Select(r => $"{(r.Delta >= 0 ? "+" : "")}{r.Delta} {r.Label}")
            )
        );
    }

    private static void AddIfWeighted(List<RaidScoreReason> reasons, RaidScoreReason? reason)
    {
        if (reason is { Delta: not 0 })
            reasons.Add(reason);
    }

    private static RaidScoreReason ScoreCategory(TwitchStream stream, RaidScoringRules rules)
    {
        RaidCategoryRule? preferred = rules.PreferredCategories.FirstOrDefault(c =>
            c.Name.Equals(stream.GameName, StringComparison.OrdinalIgnoreCase)
        );
        return preferred is not null
            ? new RaidScoreReason(preferred.Weight, preferred.Label)
            : new RaidScoreReason(
                rules.OtherCategoryWeight,
                $"{rules.OtherCategoryLabel} ({stream.GameName})"
            );
    }

    /// <summary>The first keyword rule the stream's tags or title match, in the channel's own order.</summary>
    private static RaidScoreReason? ScoreContentSignal(TwitchStream stream, RaidScoringRules rules)
    {
        RaidKeywordRule? match = RaidTagSignals.FindMatch(stream, rules.Keywords);
        return match is null ? null : new RaidScoreReason(match.Weight, match.Label);
    }

    private static RaidScoreReason? ScoreViewerCount(int viewers, RaidScoringRules rules)
    {
        RaidViewerBand? band = rules.ViewerBands.FirstOrDefault(b =>
            viewers >= b.Min && viewers <= b.Max
        );
        return band is null ? null : new RaidScoreReason(band.Weight, $"{band.Label} ({viewers}v)");
    }

    private static RaidScoreReason ScoreRaidCooldown(
        DateTimeOffset? lastRaid,
        RaidRecencyRules recency,
        DateTimeOffset now
    )
    {
        if (lastRaid is null)
            return new RaidScoreReason(recency.NeverRaidedWeight, "never raided");

        TimeSpan ago = now - lastRaid.Value;
        string when = ShortAgo(ago);
        RaidRecencyStep? step = recency
            .Steps.OrderBy(s => s.WithinDays)
            .FirstOrDefault(s => ago < TimeSpan.FromDays(s.WithinDays));
        return step is not null
            ? new RaidScoreReason(step.Weight, RaidedLabel(when, step.Note))
            : new RaidScoreReason(recency.OlderWeight, RaidedLabel(when, recency.OlderNote));
    }

    private static string RaidedLabel(string when, string note) =>
        note.Length == 0 ? $"raided {when} ago" : $"raided {when} ago ({note})";

    /// <summary>If they raided us more than we raided them, we owe them; level raids earn the reciprocal weight.</summary>
    private static RaidScoreReason? ScoreReciprocity(
        int inCount,
        int outCount,
        RaidBalanceWeights balance
    )
    {
        if (inCount == 0)
            return null;

        int imbalance = inCount - outCount;
        if (imbalance >= 1)
        {
            string raidWord = imbalance == 1 ? "raid" : "raids";
            return new RaidScoreReason(
                balance.OwedBaseWeight + (imbalance - 1) * balance.OwedExtraWeight,
                $"owed {imbalance} {raidWord} (in:{inCount}/out:{outCount})"
            );
        }

        return imbalance == 0
            ? new RaidScoreReason(
                balance.ReciprocalWeight,
                $"reciprocal (in:{inCount}/out:{outCount})"
            )
            : null;
    }

    /// <summary>Compact age: "1mo", "2w", "5d" or "today".</summary>
    private static string ShortAgo(TimeSpan ago)
    {
        if (ago.TotalDays >= 30)
            return $"{(int)(ago.TotalDays / 30)}mo";
        if (ago.TotalDays >= 7)
            return $"{(int)(ago.TotalDays / 7)}w";
        if (ago.TotalDays >= 1)
            return $"{(int)ago.TotalDays}d";
        return "today";
    }
}
