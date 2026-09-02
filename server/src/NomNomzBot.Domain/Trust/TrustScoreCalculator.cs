// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Trust;

/// <summary>
/// Calculates a per-user, per-channel trust score (0–100) using
/// Bamo's exponential-decay weighting algorithm.
/// </summary>
public static class TrustScoreCalculator
{
    // ─── Weights (must sum to 1.0) ────────────────────────────────────────────
    private const double RequestCountWeight = 0.25;
    private const double AccountAgeWeight = 0.25;
    private const double ContentAgeWeight = 0.30;
    private const double ContentPopularityWeight = 0.20;

    // ─── Decay rates (higher = faster saturation toward 100) ─────────────────
    private const double RequestCountDecay = 0.599; // ~5 requests → ~95%
    private const double AccountAgeDecay = 0.499; // ~6 months   → ~95%
    private const double ContentAgeDecay = 0.999; // ~3 months   → ~95%
    private const double ContentPopularityDecay = 0.0003; // ~10K views  → ~95%

    /// <summary>
    /// Calculate a trust score from 0 to 100 for the given context.
    /// </summary>
    public static double Calculate(TrustContext ctx)
    {
        // Step 1: Metric scores (0–100 each) via exponential decay
        double requestScore =
            100.0 * (1.0 - Math.Exp(-RequestCountDecay * ctx.SuccessfulRequestCount));
        double accountScore = 100.0 * (1.0 - Math.Exp(-AccountAgeDecay * ctx.AccountAgeMonths));
        double contentScore = 100.0 * (1.0 - Math.Exp(-ContentAgeDecay * ctx.ContentAgeMonths));
        double popularityScore =
            100.0 * (1.0 - Math.Exp(-ContentPopularityDecay * ctx.ContentViewCount));

        // Step 2: Weighted sum
        double score =
            requestScore * RequestCountWeight
            + accountScore * AccountAgeWeight
            + contentScore * ContentAgeWeight
            + popularityScore * ContentPopularityWeight;

        // Step 3: Follow penalty — not following or <24h follow
        if (!ctx.IsFollowing || ctx.FollowAgeDays < 1.0)
            score *= 0.75;

        // Step 4: Reputation boost — mods/VIPs/subs or established requesters
        if (ctx.IsModerator || ctx.IsVip || ctx.IsSubscriber || ctx.SuccessfulRequestCount >= 10)
            score = score + (100.0 - score) / 2.0;

        // Step 5: YouTube-specific channel quality penalties
        if (ctx.IsYouTubeContent)
        {
            if (ctx.ContentChannelVideoCount < 5 || ctx.ContentChannelTotalViews < 5_000)
                score *= 0.75;

            if (ctx.ContentChannelSubscribers < 25)
                score *= 0.75;

            if (ctx.ContentChannelAgeMonths < 1.0)
                score *= 0.75;
        }

        // Step 6: Violation penalties (applied after boosts)
        score -= ctx.SkippedByModCount * 5.0;
        score -= ctx.TimeoutCount * 10.0;
        score -= ctx.BanCount * 30.0;

        return Math.Clamp(score, 0.0, 100.0);
    }

    /// <summary>Maps a numeric score to its corresponding trust tier.</summary>
    public static TrustTier GetTier(double score) =>
        score switch
        {
            <= 25.0 => TrustTier.Untrusted,
            <= 50.0 => TrustTier.Low,
            <= 75.0 => TrustTier.Standard,
            _ => TrustTier.Trusted,
        };
}
