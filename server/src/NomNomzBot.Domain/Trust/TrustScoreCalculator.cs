// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Trust.Entities;

namespace NomNomzBot.Domain.Trust;

/// <summary>
/// Calculates a per-user, per-channel trust score (0–100) using
/// Bamo's exponential-decay weighting algorithm.
/// </summary>
public static class TrustScoreCalculator
{
    /// <summary>
    /// The shipped tuning, used when a caller has no per-channel policy. A freshly constructed
    /// <see cref="TrustPolicy"/> IS the default set — its property initializers are the single source of
    /// truth for every default, so there is no second copy here to drift out of step with the database.
    /// </summary>
    public static readonly TrustPolicy DefaultPolicy = new();

    /// <summary>
    /// Calculate a trust score from 0 to 100 for the given context, tuned by the channel's
    /// <paramref name="policy"/> (<see cref="DefaultPolicy"/> when none is supplied).
    /// </summary>
    public static double Calculate(TrustContext ctx, TrustPolicy? policy = null)
    {
        TrustPolicy p = policy ?? DefaultPolicy;

        // Step 1: Metric scores (0–100 each) via exponential decay
        double requestScore =
            100.0 * (1.0 - Math.Exp(-p.RequestCountDecay * ctx.SuccessfulRequestCount));
        double accountScore = 100.0 * (1.0 - Math.Exp(-p.AccountAgeDecay * ctx.AccountAgeMonths));
        double contentScore = 100.0 * (1.0 - Math.Exp(-p.ContentAgeDecay * ctx.ContentAgeMonths));
        double popularityScore =
            100.0 * (1.0 - Math.Exp(-p.ContentPopularityDecay * ctx.ContentViewCount));

        // Step 2: Weighted sum
        double score =
            requestScore * p.RequestCountWeight
            + accountScore * p.AccountAgeWeight
            + contentScore * p.ContentAgeWeight
            + popularityScore * p.ContentPopularityWeight;

        // Step 3: Follow penalty — not following or <24h follow
        if (!ctx.IsFollowing || ctx.FollowAgeDays < 1.0)
            score *= p.NotFollowingFactor;

        // Step 4: Reputation boost — mods/VIPs/subs or established requesters
        if (
            p.ReputationBoostEnabled
            && (
                ctx.IsModerator || ctx.IsVip || ctx.IsSubscriber || ctx.SuccessfulRequestCount >= 10
            )
        )
            score = score + (100.0 - score) / 2.0;

        // Step 5: YouTube-specific channel quality penalties
        if (ctx.IsYouTubeContent)
        {
            if (ctx.ContentChannelVideoCount < 5 || ctx.ContentChannelTotalViews < 5_000)
                score *= p.YouTubeQualityPenaltyFactor;

            if (ctx.ContentChannelSubscribers < 25)
                score *= p.YouTubeQualityPenaltyFactor;

            if (ctx.ContentChannelAgeMonths < 1.0)
                score *= p.YouTubeQualityPenaltyFactor;
        }

        // Step 6: Violation penalties (applied after boosts)
        score -= ctx.SkippedByModCount * p.SkipPenalty;
        score -= ctx.TimeoutCount * p.TimeoutPenalty;
        score -= ctx.BanCount * p.BanPenalty;

        return Math.Clamp(score, 0.0, 100.0);
    }

    /// <summary>
    /// Maps a numeric score to its trust tier using the channel's ceilings
    /// (<see cref="DefaultPolicy"/> when none is supplied).
    /// </summary>
    public static TrustTier GetTier(double score, TrustPolicy? policy = null)
    {
        TrustPolicy p = policy ?? DefaultPolicy;
        if (score <= p.UntrustedMax)
            return TrustTier.Untrusted;
        if (score <= p.LowMax)
            return TrustTier.Low;
        return score <= p.StandardMax ? TrustTier.Standard : TrustTier.Trusted;
    }
}
