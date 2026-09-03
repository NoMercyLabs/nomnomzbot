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
using NomNomzBot.Domain.Moderation.SpamDefense;

namespace NomNomzBot.Domain.Tests.Moderation.SpamDefense;

/// <summary>
/// Burst baselines and the follow-bot track (spam-defense.md §L3, §L3.1 / SD9).
///
/// <para>The centrepiece is <see cref="AViralMoment_BlocksNobody"/>. §9 requires it to ship in the same
/// slice as the detector, because a follow spike and a front-page moment are indistinguishable from the
/// rate alone, and the people arriving in the second case are real.</para>
/// </summary>
public class FollowBotTrackTests
{
    private static FollowCandidate RealViewer(string id) =>
        new(
            AccountId: id,
            Username: $"kate_{id}",
            AccountAgeHours: 9_000,
            HasProfileContent: true,
            FollowUnfollowCycles: 0,
            IsOnKnownBotList: false,
            Tier: SpamTrustTier.Untrusted
        );

    private static FollowCandidate BrandNewButRealViewer(string id) =>
        RealViewer(id) with
        {
            AccountAgeHours = 2,
            Username = $"newfan_{id}",
        };

    private static FollowCandidate Bot(string id) =>
        new(
            AccountId: id,
            Username: $"viewer{id}8042193",
            AccountAgeHours: 3,
            HasProfileContent: false,
            FollowUnfollowCycles: 0,
            IsOnKnownBotList: false,
            Tier: SpamTrustTier.Untrusted
        );

    // ---- The test the spec demands in this slice ------------------------------------------------

    [Fact]
    public void AViralMoment_BlocksNobody()
    {
        // 500 real people arrive at once because the channel hit the front page. Every one of them
        // followed inside the spike window and none of them will ever type a word. If the spike alone
        // could select accounts, this is 500 wrongly-blocked viewers at the best moment of someone's
        // streaming career.
        List<FollowCandidate> arrivals = [];
        for (int i = 0; i < 500; i++)
            arrivals.Add(RealViewer($"viral{i}"));

        FollowBotBlockBatch batch = FollowBotTrack.Examine(arrivals);

        batch.Findings.Should().BeEmpty("a spike is a window to scrutinise, never a set to action");
        batch.Examined.Should().Be(500);
        batch.LeftAlone.Should().Be(500);
    }

    [Fact]
    public void ARaidOfBrandNewAccountsWithProfiles_BlocksNobody()
    {
        // The harder version: a streamer tells their new Discord to come follow. The accounts really
        // are hours old. Being new is not evidence — only new AND empty is, and even that is one
        // indicator rather than a verdict.
        List<FollowCandidate> arrivals = [];
        for (int i = 0; i < 50; i++)
            arrivals.Add(BrandNewButRealViewer($"discord{i}"));

        FollowBotTrack.Examine(arrivals).Findings.Should().BeEmpty();
    }

    [Fact]
    public void ASilentLurkerIsNeverBlocked_HoweverLongTheyWatch()
    {
        // SD9: being silent is not a signal. There is deliberately no field on FollowCandidate for
        // "has never spoken", because it must not be able to contribute to a block.
        FollowCandidate lurker = RealViewer("lurker") with
        {
            HasProfileContent = false,
        };

        FollowBotTrack.IndicatorsFor(lurker).Should().BeEmpty();
    }

    // ---- Detection still has to work ------------------------------------------------------------

    [Fact]
    public void AFollowBotFarm_IsBlocked_AndEachBlockCarriesItsOwnReason()
    {
        List<FollowCandidate> arrivals = [];
        for (int i = 0; i < 200; i++)
            arrivals.Add(Bot($"{i}"));
        for (int i = 0; i < 20; i++)
            arrivals.Add(RealViewer($"bystander{i}"));

        FollowBotBlockBatch batch = FollowBotTrack.Examine(arrivals);

        batch.Findings.Should().HaveCount(200);
        batch.LeftAlone.Should().Be(20, "the real viewers in the same window are untouched");
        batch
            .Findings.Should()
            .OnlyContain(f => f.Indicators.Count > 0, "SD9: no block without its own evidence");
    }

    [Theory]
    [InlineData("viewer80421933", true)]
    [InlineData("xqcfan2837461", true)]
    [InlineData("tom1994", false)]
    [InlineData("player123", false)]
    [InlineData("s1mple", false)]
    [InlineData("kate", false)]
    public void TheGeneratedHandlePattern_NeedsALongDigitRun_SoBirthYearsAreSafe(
        string username,
        bool isGenerated
    )
    {
        // A short digit suffix is how ordinary people get a free handle. Only a bulk-registration-length
        // run counts.
        FollowCandidate candidate = RealViewer("x") with
        {
            Username = username,
        };

        FollowBotTrack
            .IndicatorsFor(candidate)
            .Contains(FollowBotIndicator.GeneratedHandlePattern)
            .Should()
            .Be(isGenerated);
    }

    [Fact]
    public void FollowUnfollowOscillation_IsItsOwnEvidence()
    {
        FollowCandidate cycler = RealViewer("cycler") with { FollowUnfollowCycles = 4 };

        FollowBotTrack
            .IndicatorsFor(cycler)
            .Should()
            .Contain(FollowBotIndicator.FollowUnfollowOscillation);
    }

    [Fact]
    public void AStandingViewer_IsExcludedFromTheSweepEntirely_EvenLookingLikeABot()
    {
        // SD11: excluded from every burst sweep. A moderator elsewhere with an unfortunate handle is
        // still not blockable by automation.
        FollowCandidate moderator = Bot("42") with
        {
            Tier = SpamTrustTier.SemiTrusted,
        };

        FollowBotTrack.Examine([moderator]).Findings.Should().BeEmpty();
    }

    [Fact]
    public void ABlockBatchIsReviewable_SoAMisreadViralMomentIsRecoverable()
    {
        // "Nothing is silently unrecoverable" — the batch keeps both what it did and what it examined,
        // which is what lets an operator judge whether the sweep was right at all.
        FollowBotBlockBatch batch = FollowBotTrack.Examine([Bot("1"), RealViewer("2")]);

        batch.Examined.Should().Be(2);
        batch.Findings.Should().ContainSingle();
        batch.LeftAlone.Should().Be(1);
    }

    // ---- Baselines are per-channel --------------------------------------------------------------

    [Fact]
    public void ASmallChannelAndALargeChannel_DoNotShareAThreshold()
    {
        // Ten follows a minute is an attack on a 50-viewer channel and a quiet Tuesday on a large one.
        ChannelBaseline small = new();
        ChannelBaseline large = new();
        for (int i = 0; i < 20; i++)
        {
            small.Record(1);
            large.Record(40);
        }

        small.IsSpike(30).Should().BeTrue("30x its own normal");
        large.IsSpike(30).Should().BeFalse("below its own normal");
    }

    [Fact]
    public void ANewChannelsFirstBusyMinute_IsNotASpike()
    {
        // Without a warmup requirement, every new streamer's opening night reads as an attack: any
        // number is infinitely above a baseline of nothing.
        ChannelBaseline fresh = new();
        fresh.Record(0);
        fresh.Record(1);

        fresh.HasEnoughHistory.Should().BeFalse();
        fresh.IsSpike(100).Should().BeFalse();
    }

    [Fact]
    public void AQuietChannelDoesNotSpikeOnTwoFollows()
    {
        // A baseline of 0.1 makes 2 follows a 20x multiple. That is one friend telling another to hit
        // the button, and the absolute floor is what stops it being an incident.
        ChannelBaseline quiet = new();
        for (int i = 0; i < 20; i++)
            quiet.Record(0.1);

        quiet.IsSpike(2).Should().BeFalse("below the absolute floor");
        quiet.IsSpike(40).Should().BeTrue("this one is real");
    }

    [Fact]
    public void TheBaselineRollsForward_SoAChannelThatGrowsIsNotPermanentlySuspicious()
    {
        // A channel that genuinely grows from 5 to 50 follows a minute must stop treating 50 as an
        // attack once that is simply what it does now.
        ChannelBaseline baseline = new(capacity: 10, minimumSamples: 5);
        for (int i = 0; i < 10; i++)
            baseline.Record(5);
        baseline.IsSpike(50).Should().BeTrue();

        for (int i = 0; i < 10; i++)
            baseline.Record(50);

        baseline.Mean.Should().Be(50);
        baseline.IsSpike(50).Should().BeFalse("this is the channel's new normal");
    }

    [Fact]
    public void TheRollingWindowNeverGrowsPastItsCapacity()
    {
        ChannelBaseline baseline = new(capacity: 10, minimumSamples: 5);
        for (int i = 0; i < 100; i++)
            baseline.Record(1);

        baseline.SampleCount.Should().Be(10);
    }
}
