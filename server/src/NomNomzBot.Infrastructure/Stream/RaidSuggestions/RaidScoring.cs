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
/// Pure raid-target ranking, no I/O. Rules match the legacy bot: favour real software streams over
/// games, small channels over big ones, channels not raided lately, followed channels, and channels
/// that raided us more often than we raided them.
/// </summary>
internal static class RaidScoring
{
    private const string SoftwareCategoryName = "Software and Game Development";
    private const string ScienceCategoryName = "Science & Technology";

    /// <summary>Scores every candidate and sorts best first. Equal scores keep their input order.</summary>
    public static List<RaidScoreResult> Rank(
        IReadOnlyList<RaidCandidate> candidates,
        IReadOnlyDictionary<string, RaidStats> outgoing,
        IReadOnlyDictionary<string, RaidStats> incoming,
        DateTimeOffset now
    ) =>
        candidates
            .Select(c =>
                Score(
                    c,
                    outgoing.GetValueOrDefault(c.Stream.UserId),
                    incoming.GetValueOrDefault(c.Stream.UserId),
                    now
                )
            )
            .OrderByDescending(r => r.Score)
            .ToList();

    public static RaidScoreResult Score(
        RaidCandidate candidate,
        RaidStats? outgoing,
        RaidStats? incoming,
        DateTimeOffset now
    )
    {
        List<RaidScoreReason> reasons = [ScoreCategory(candidate.Stream)];

        RaidScoreReason? signal = ScoreContentSignal(candidate.Stream);
        if (signal is not null)
            reasons.Add(signal);

        reasons.Add(ScoreViewerCount(candidate.Stream.ViewerCount));
        reasons.Add(ScoreRaidCooldown(outgoing?.LastAt, now));

        if (candidate.IsFollowed)
            reasons.Add(new RaidScoreReason(50, "followed"));

        RaidScoreReason? reciprocity = ScoreReciprocity(incoming?.Count ?? 0, outgoing?.Count ?? 0);
        if (reciprocity is not null)
            reasons.Add(reciprocity);

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

    private static RaidScoreReason ScoreCategory(TwitchStream stream)
    {
        if (stream.GameName.Equals(SoftwareCategoryName, StringComparison.OrdinalIgnoreCase))
            return new RaidScoreReason(60, "Software cat");
        if (stream.GameName.Equals(ScienceCategoryName, StringComparison.OrdinalIgnoreCase))
            return new RaidScoreReason(40, "Sci&Tech cat");
        return new RaidScoreReason(-20, $"non-dev cat ({stream.GameName})");
    }

    /// <summary>Game dev wins over software: a streamer with "programming" and "deckbuilder" makes a game.</summary>
    private static RaidScoreReason? ScoreContentSignal(TwitchStream stream)
    {
        if (RaidTagSignals.IsGameDev(stream))
            return new RaidScoreReason(-40, "gamedev signal");
        if (RaidTagSignals.IsSoftware(stream))
            return new RaidScoreReason(35, "software signal");
        return null;
    }

    private static RaidScoreReason ScoreViewerCount(int v) =>
        v switch
        {
            >= 1 and <= 50 => new RaidScoreReason(25, $"small ({v}v)"),
            <= 200 => new RaidScoreReason(10, $"mid ({v}v)"),
            <= 500 => new RaidScoreReason(-5, $"medium ({v}v)"),
            <= 1000 => new RaidScoreReason(-15, $"big ({v}v)"),
            <= 5000 => new RaidScoreReason(-30, $"large ({v}v)"),
            _ => new RaidScoreReason(-50, $"giant ({v}v)"),
        };

    /// <summary>Under 7 days is a hard cooldown, 7-14 still recent, 14-30 neutral, over 30 is due.</summary>
    private static RaidScoreReason ScoreRaidCooldown(DateTimeOffset? lastRaid, DateTimeOffset now)
    {
        if (lastRaid is null)
            return new RaidScoreReason(30, "never raided");

        TimeSpan ago = now - lastRaid.Value;
        string when = ShortAgo(ago);
        if (ago < TimeSpan.FromDays(7))
            return new RaidScoreReason(-40, $"raided {when} ago (cooldown)");
        if (ago < TimeSpan.FromDays(14))
            return new RaidScoreReason(-15, $"raided {when} ago (recent)");
        if (ago < TimeSpan.FromDays(30))
            return new RaidScoreReason(5, $"raided {when} ago");
        return new RaidScoreReason(25, $"raided {when} ago (stale)");
    }

    /// <summary>
    /// If they raided us more than we raided them, we owe them. The base bump (55) clears the gamedev
    /// penalty (-40) and each extra owed raid adds 12.
    /// </summary>
    private static RaidScoreReason? ScoreReciprocity(int inCount, int outCount)
    {
        if (inCount == 0)
            return null;

        int imbalance = inCount - outCount;
        if (imbalance >= 1)
        {
            string raidWord = imbalance == 1 ? "raid" : "raids";
            return new RaidScoreReason(
                55 + (imbalance - 1) * 12,
                $"owed {imbalance} {raidWord} (in:{inCount}/out:{outCount})"
            );
        }

        return imbalance == 0
            ? new RaidScoreReason(10, $"reciprocal (in:{inCount}/out:{outCount})")
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
