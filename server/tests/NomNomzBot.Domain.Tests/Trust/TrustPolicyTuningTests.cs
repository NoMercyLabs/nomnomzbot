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
using NomNomzBot.Domain.Trust;
using NomNomzBot.Domain.Trust.Entities;

namespace NomNomzBot.Domain.Tests.Trust;

/// <summary>
/// Proves the per-channel <see cref="TrustPolicy"/> actually drives the score (S-OWN23 T2) — an
/// operator changing a weight must change what the bot decides, not merely persist a number. Also
/// pins the promise the whole slice rests on: an untouched policy reproduces the shipped scores
/// exactly, so introducing the policy changed nobody's trust overnight.
/// </summary>
public class TrustPolicyTuningTests
{
    // TrustContext is a plain class (not a record), so each variant is built explicitly rather than
    // with a `with` expression.
    private static TrustContext EstablishedViewer(
        bool isModerator = false,
        int banCount = 0,
        bool isFollowing = true
    ) =>
        new()
        {
            SuccessfulRequestCount = 4,
            AccountAgeMonths = 8.0,
            ContentAgeMonths = 5.0,
            ContentViewCount = 12_000,
            IsFollowing = isFollowing,
            FollowAgeDays = isFollowing ? 90.0 : 0.0,
            IsModerator = isModerator,
            BanCount = banCount,
        };

    [Fact]
    public void DefaultPolicy_ReproducesTheShippedScore_Exactly()
    {
        TrustContext ctx = EstablishedViewer();

        // The no-policy overload and an untouched policy must agree to the bit — this is what makes
        // shipping the policy a no-op for every existing channel.
        double implicitDefault = TrustScoreCalculator.Calculate(ctx);
        double explicitDefault = TrustScoreCalculator.Calculate(ctx, new TrustPolicy());

        explicitDefault.Should().Be(implicitDefault);
        implicitDefault.Should().BeGreaterThan(0.0, "the fixture describes a real, scoring viewer");
    }

    [Fact]
    public void RaisingTheAccountAgeWeight_RaisesTheScoreOfAnOldAccount()
    {
        // A viewer whose strength is age: brand-new content, no request history.
        TrustContext oldAccountNewContent = new()
        {
            AccountAgeMonths = 24.0,
            ContentAgeMonths = 0.0,
            ContentViewCount = 0,
            IsFollowing = true,
            FollowAgeDays = 400.0,
        };
        double baseline = TrustScoreCalculator.Calculate(oldAccountNewContent);

        // Move the weight the operator would move, keeping the four weights summing to 1.0.
        TrustPolicy ageMatters = new()
        {
            AccountAgeWeight = 0.55,
            ContentAgeWeight = 0.05,
            RequestCountWeight = 0.25,
            ContentPopularityWeight = 0.15,
        };
        double tuned = TrustScoreCalculator.Calculate(oldAccountNewContent, ageMatters);

        tuned
            .Should()
            .BeGreaterThan(
                baseline,
                "weighting account age more must reward an old account, or the knob is decorative"
            );
    }

    [Fact]
    public void DisablingTheReputationBoost_LowersAModeratorsScore()
    {
        TrustContext moderator = EstablishedViewer(isModerator: true);

        double boosted = TrustScoreCalculator.Calculate(moderator);
        double unboosted = TrustScoreCalculator.Calculate(
            moderator,
            new TrustPolicy { ReputationBoostEnabled = false }
        );

        boosted.Should().BeGreaterThan(unboosted);
        unboosted
            .Should()
            .Be(
                TrustScoreCalculator.Calculate(
                    EstablishedViewer(),
                    new TrustPolicy { ReputationBoostEnabled = false }
                ),
                "with the boost off, being a moderator must stop mattering entirely"
            );
    }

    [Fact]
    public void TheBanPenalty_IsTheNumberOfPointsRemoved()
    {
        TrustContext clean = EstablishedViewer();
        TrustContext banned = EstablishedViewer(banCount: 1);

        double cleanScore = TrustScoreCalculator.Calculate(clean);
        double defaultPenalty = cleanScore - TrustScoreCalculator.Calculate(banned);
        double halvedPenalty =
            cleanScore
            - TrustScoreCalculator.Calculate(banned, new TrustPolicy { BanPenalty = 15.0 });

        defaultPenalty
            .Should()
            .BeApproximately(30.0, 0.0001, "the shipped ban penalty is 30 points");
        halvedPenalty
            .Should()
            .BeApproximately(15.0, 0.0001, "the policy value is the points removed");
    }

    [Fact]
    public void NotFollowingFactorOfOne_RemovesThePenaltyEntirely()
    {
        TrustContext notFollowing = EstablishedViewer(isFollowing: false);
        TrustContext following = EstablishedViewer();

        TrustScoreCalculator
            .Calculate(notFollowing)
            .Should()
            .BeLessThan(TrustScoreCalculator.Calculate(following), "the shipped penalty is real");

        TrustPolicy noPenalty = new() { NotFollowingFactor = 1.0 };
        TrustScoreCalculator
            .Calculate(notFollowing, noPenalty)
            .Should()
            .Be(
                TrustScoreCalculator.Calculate(following, noPenalty),
                "a factor of 1.0 must make following irrelevant"
            );
    }

    [Theory]
    [InlineData(10.0, TrustTier.Untrusted)]
    [InlineData(40.0, TrustTier.Low)]
    [InlineData(60.0, TrustTier.Standard)]
    [InlineData(90.0, TrustTier.Trusted)]
    public void DefaultCeilings_MapScoresToTheShippedTiers(double score, TrustTier expected) =>
        TrustScoreCalculator.GetTier(score).Should().Be(expected);

    [Fact]
    public void RaisingTheCeilings_MovesTheSameScoreIntoALowerTier()
    {
        const double score = 60.0;
        TrustScoreCalculator.GetTier(score).Should().Be(TrustTier.Standard);

        // A stricter channel: the same 60 is now merely Low.
        TrustPolicy strict = new()
        {
            UntrustedMax = 40.0,
            LowMax = 70.0,
            StandardMax = 90.0,
        };
        TrustScoreCalculator.GetTier(score, strict).Should().Be(TrustTier.Low);

        // And the boundaries stay inclusive-at-the-ceiling, as the shipped switch was.
        TrustScoreCalculator.GetTier(40.0, strict).Should().Be(TrustTier.Untrusted);
        TrustScoreCalculator.GetTier(90.0, strict).Should().Be(TrustTier.Standard);
        TrustScoreCalculator.GetTier(90.001, strict).Should().Be(TrustTier.Trusted);
    }
}
