// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using NomNomzBot.Infrastructure.Music;

namespace NomNomzBot.Infrastructure.Tests.Music;

public class TrustScoreCalculatorTests
{
    // ─── Score range ─────────────────────────────────────────────────────────

    [Fact]
    public void Calculate_ZeroInputs_ScoreIsZero()
    {
        TrustContext ctx = new();
        TrustScoreCalculator.Calculate(ctx).Should().Be(0.0);
    }

    [Fact]
    public void Calculate_ScoreAlwaysInRange_Positive()
    {
        TrustContext ctx = new()
        {
            SuccessfulRequestCount = 1000,
            AccountAgeMonths = 120,
            ContentAgeMonths = 120,
            ContentViewCount = 100_000_000,
            IsFollowing = true,
            FollowAgeDays = 365,
            IsModerator = true,
            IsVip = true,
            IsSubscriber = true,
        };
        TrustScoreCalculator.Calculate(ctx).Should().BeInRange(0, 100);
    }

    [Fact]
    public void Calculate_ScoreAlwaysInRange_WithViolations()
    {
        TrustContext ctx = new()
        {
            BanCount = 100,
            TimeoutCount = 100,
            SkippedByModCount = 100,
        };
        TrustScoreCalculator.Calculate(ctx).Should().BeInRange(0, 100);
    }

    // ─── Follow penalty ──────────────────────────────────────────────────────

    [Fact]
    public void Calculate_NotFollowing_AppliesFollowPenalty()
    {
        TrustContext withFollower = new()
        {
            SuccessfulRequestCount = 5,
            AccountAgeMonths = 12,
            ContentAgeMonths = 6,
            ContentViewCount = 10_000,
            IsFollowing = true,
            FollowAgeDays = 30,
        };

        TrustContext withoutFollower = new()
        {
            SuccessfulRequestCount = 5,
            AccountAgeMonths = 12,
            ContentAgeMonths = 6,
            ContentViewCount = 10_000,
            IsFollowing = false,
            FollowAgeDays = 30,
        };

        TrustScoreCalculator
            .Calculate(withoutFollower)
            .Should()
            .BeLessThan(
                TrustScoreCalculator.Calculate(withFollower),
                "not following reduces the score by 25%"
            );
    }

    [Fact]
    public void Calculate_FollowingLessThan24Hours_AppliesFollowPenalty()
    {
        TrustContext newFollower = new()
        {
            SuccessfulRequestCount = 5,
            AccountAgeMonths = 12,
            ContentAgeMonths = 6,
            ContentViewCount = 10_000,
            IsFollowing = true,
            FollowAgeDays = 0.5,
        };

        TrustContext establishedFollower = new()
        {
            SuccessfulRequestCount = 5,
            AccountAgeMonths = 12,
            ContentAgeMonths = 6,
            ContentViewCount = 10_000,
            IsFollowing = true,
            FollowAgeDays = 30,
        };

        TrustScoreCalculator
            .Calculate(newFollower)
            .Should()
            .BeLessThan(
                TrustScoreCalculator.Calculate(establishedFollower),
                "following < 24h triggers follow penalty"
            );
    }

    // ─── Reputation boost ────────────────────────────────────────────────────

    [Fact]
    public void Calculate_Moderator_BoostsScore()
    {
        TrustContext baseCtx = new()
        {
            SuccessfulRequestCount = 2,
            AccountAgeMonths = 6,
            ContentAgeMonths = 3,
            ContentViewCount = 5_000,
            IsFollowing = true,
            FollowAgeDays = 30,
        };

        TrustContext modCtx = new()
        {
            SuccessfulRequestCount = 2,
            AccountAgeMonths = 6,
            ContentAgeMonths = 3,
            ContentViewCount = 5_000,
            IsFollowing = true,
            FollowAgeDays = 30,
            IsModerator = true,
        };

        TrustScoreCalculator
            .Calculate(modCtx)
            .Should()
            .BeGreaterThan(
                TrustScoreCalculator.Calculate(baseCtx),
                "moderators receive a reputation boost"
            );
    }

    [Fact]
    public void Calculate_TenOrMoreRequests_BoostsScore()
    {
        TrustContext regular = new()
        {
            SuccessfulRequestCount = 9,
            AccountAgeMonths = 6,
            ContentAgeMonths = 3,
            ContentViewCount = 5_000,
            IsFollowing = true,
            FollowAgeDays = 30,
        };

        TrustContext veteran = new()
        {
            SuccessfulRequestCount = 10,
            AccountAgeMonths = 6,
            ContentAgeMonths = 3,
            ContentViewCount = 5_000,
            IsFollowing = true,
            FollowAgeDays = 30,
        };

        TrustScoreCalculator
            .Calculate(veteran)
            .Should()
            .BeGreaterThan(
                TrustScoreCalculator.Calculate(regular),
                "10+ requests triggers reputation boost"
            );
    }

    // ─── Violation penalties ─────────────────────────────────────────────────

    [Fact]
    public void Calculate_WithTimeouts_ReducesScore()
    {
        TrustContext good = new()
        {
            SuccessfulRequestCount = 5,
            AccountAgeMonths = 12,
            ContentAgeMonths = 6,
            ContentViewCount = 50_000,
            IsFollowing = true,
            FollowAgeDays = 90,
            IsSubscriber = true,
        };

        TrustContext withViolations = new()
        {
            SuccessfulRequestCount = 5,
            AccountAgeMonths = 12,
            ContentAgeMonths = 6,
            ContentViewCount = 50_000,
            IsFollowing = true,
            FollowAgeDays = 90,
            IsSubscriber = true,
            TimeoutCount = 3,
        };

        TrustScoreCalculator
            .Calculate(withViolations)
            .Should()
            .BeLessThan(TrustScoreCalculator.Calculate(good), "timeouts reduce the trust score");
    }

    // ─── YouTube penalties ───────────────────────────────────────────────────

    [Fact]
    public void Calculate_YoutubeNewChannel_AppliesPenalty()
    {
        TrustContext legitimateYt = new()
        {
            SuccessfulRequestCount = 5,
            AccountAgeMonths = 12,
            ContentAgeMonths = 6,
            ContentViewCount = 50_000,
            IsFollowing = true,
            FollowAgeDays = 30,
            IsYouTubeContent = true,
            ContentChannelVideoCount = 100,
            ContentChannelTotalViews = 500_000,
            ContentChannelSubscribers = 1_000,
            ContentChannelAgeMonths = 24,
        };

        TrustContext spamYt = new()
        {
            SuccessfulRequestCount = 5,
            AccountAgeMonths = 12,
            ContentAgeMonths = 6,
            ContentViewCount = 50_000,
            IsFollowing = true,
            FollowAgeDays = 30,
            IsYouTubeContent = true,
            ContentChannelVideoCount = 2,
            ContentChannelTotalViews = 100,
            ContentChannelSubscribers = 1,
            ContentChannelAgeMonths = 0.5,
        };

        TrustScoreCalculator
            .Calculate(spamYt)
            .Should()
            .BeLessThan(
                TrustScoreCalculator.Calculate(legitimateYt),
                "new/spam YouTube channels receive lower trust"
            );
    }

    // ─── Tier mapping ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0.0, TrustTier.Untrusted)]
    [InlineData(25.0, TrustTier.Untrusted)]
    [InlineData(25.1, TrustTier.Low)]
    [InlineData(50.0, TrustTier.Low)]
    [InlineData(50.1, TrustTier.Standard)]
    [InlineData(75.0, TrustTier.Standard)]
    [InlineData(75.1, TrustTier.Trusted)]
    [InlineData(100.0, TrustTier.Trusted)]
    public void GetTier_MapsScoreToCorrectTier(double score, TrustTier expectedTier)
    {
        TrustScoreCalculator.GetTier(score).Should().Be(expectedTier);
    }
}
