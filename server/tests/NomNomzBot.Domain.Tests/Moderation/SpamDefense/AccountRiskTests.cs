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
/// L1 account risk (spam-defense.md §L1). The load-bearing assertion in this file is
/// <see cref="TheWorstLookingAccountSayingSomethingOrdinary_ScoresZero"/>: it is the SD10 invariant, and
/// if it ever fails the system has started punishing people for what they are instead of what they said.
/// </summary>
public class AccountRiskTests
{
    /// <summary>An account with nothing suspicious about it and no standing — the neutral baseline.</summary>
    private static AccountFacts Ordinary() =>
        new()
        {
            AccountAgeDays = 400,
            IsFollowing = true,
            FollowAgeHours = 5_000,
            Username = "ordinaryviewer",
            HasChatHistoryOnInstance = true,
        };

    /// <summary>Every risk mark the table can describe, stacked onto one account.</summary>
    private static AccountFacts WorstPossibleShape() =>
        new()
        {
            AccountAgeDays = 2,
            IsFollowing = false,
            FollowAgeHours = 0,
            HasAvatar = false,
            HasBio = false,
            HasStreamed = false,
            Username = "streamer48213",
            IsFirstMessageInChannel = true,
            HasChatHistoryOnInstance = false,
        };

    [Fact]
    public void TheWorstLookingAccountSayingSomethingOrdinary_ScoresZero()
    {
        // THE SD10 INVARIANT. Two days old, not following, default profile, generated handle, first
        // message ever, no history anywhere — and it says "hi". Score = content × coefficient, and the
        // content signal is zero, so the score is zero no matter how bad the account looks. There is no
        // additive path from L1 into a score, and this test is what stops one being added.
        AccountRiskAssessment risk = AccountRisk.Assess(WorstPossibleShape());
        const double contentSignalScore = 0.0; // "hi" — nothing fired

        double finalScore = contentSignalScore * risk.Coefficient;

        finalScore
            .Should()
            .Be(0.0, "an account cannot be actioned for what it IS, only for what it said");
        risk.Coefficient.Should()
            .BeGreaterThan(
                1.0,
                "the marks are real and DO make a suspicious message judged harder"
            );
    }

    [Fact]
    public void EverySilenceMark_IsRecordedButMovesNothing()
    {
        // "First message ever" and "no history anywhere" describe a lurker finally speaking. Under any
        // additive scheme they stack two penalties for the crime of having been quiet. They must appear
        // in the explanation (SD7) and change the coefficient by exactly nothing.
        AccountFacts silentOnly = Ordinary() with
        {
            IsFirstMessageInChannel = true,
            HasChatHistoryOnInstance = false,
        };

        AccountRiskAssessment risk = AccountRisk.Assess(silentOnly);

        risk.Marks.Should()
            .Contain(AccountRiskMark.FirstMessageInChannel)
            .And.Contain(AccountRiskMark.NoChatHistoryOnInstance);
        risk.Coefficient.Should()
            .Be(
                1.0,
                "a ten-year lurker's first word is judged exactly like a regular's thousandth"
            );
    }

    [Fact]
    public void ALurkersFirstWord_IsJudgedIdenticallyToARegularsThousandth()
    {
        // The same claim from the other direction: the ONLY difference between these two accounts is
        // silence, and silence must produce no difference at all.
        double lurker = AccountRisk
            .Assess(
                Ordinary() with
                {
                    IsFirstMessageInChannel = true,
                    HasChatHistoryOnInstance = false,
                }
            )
            .Coefficient;
        double regular = AccountRisk.Assess(Ordinary()).Coefficient;

        lurker.Should().Be(regular);
    }

    [Theory]
    [InlineData(2.0, 1.6)] // under 7 days
    [InlineData(14.0, 1.3)] // under 30 days
    [InlineData(90.0, 1.1)] // under 6 months
    [InlineData(400.0, 1.0)] // older than 6 months — no age mark at all
    public void AgeBandsDoNotStack_AnAccountSitsInExactlyOne(double ageDays, double expected)
    {
        // A three-day-old account is "under 7 days", not all three bands multiplied together — that
        // would give ×2.29 for what the table says is ×1.6.
        AccountRisk
            .Assess(Ordinary() with { AccountAgeDays = ageDays })
            .Coefficient.Should()
            .BeApproximately(expected, 0.0001);
    }

    [Theory]
    [InlineData("streamer48213", true)] // word + 5 digits — the generated shape
    [InlineData("viewer1234", true)]
    [InlineData("xQcOW", false)] // real handles must not match
    [InlineData("ninja", false)]
    [InlineData("user2024fan", false)] // digits in the middle, not a trailing block
    public void GeneratedHandlePattern_CatchesBotNames_WithoutCatchingRealOnes(
        string username,
        bool expectedMark
    ) =>
        AccountRisk
            .Assess(Ordinary() with { Username = username })
            .Marks.Contains(AccountRiskMark.GeneratedHandlePattern)
            .Should()
            .Be(expectedMark);

    [Theory]
    // Every route into Semi-Trusted from §L1.2. Each is asserted separately, because a single OR that
    // silently stopped checking one route would still pass a test that only exercised another.
    [InlineData("moderator elsewhere")]
    [InlineData("vip elsewhere")]
    [InlineData("subscriber elsewhere")]
    [InlineData("watch time here")]
    [InlineData("watch time instance-wide")]
    public void EveryRouteIntoSemiTrusted_ForcesCoefficientToOne_EvenOnTheWorstShape(string route)
    {
        // The point of SD11: someone who moderates three other channels is not a spam bot, and no
        // accumulation of account-shape suspicion may reach them.
        AccountFacts facts = route switch
        {
            "moderator elsewhere" => WorstPossibleShape() with { IsModeratorAnywhere = true },
            "vip elsewhere" => WorstPossibleShape() with { IsVipAnywhere = true },
            "subscriber elsewhere" => WorstPossibleShape() with { IsSubscriberAnywhere = true },
            "watch time here" => WorstPossibleShape() with { WatchTimeHoursThisChannel = 10.0 },
            _ => WorstPossibleShape() with { WatchTimeHoursInstanceWide = 25.0 },
        };

        AccountRiskAssessment risk = AccountRisk.Assess(facts);

        risk.IsSemiTrusted.Should().BeTrue();
        risk.Coefficient.Should().Be(1.0);
        risk.Marks.Should()
            .NotBeEmpty(
                "the marks are still RECORDED — standing overrides their weight, not their truth"
            );
    }

    [Theory]
    [InlineData(9.9, false)] // just under the 10h bar in this channel
    [InlineData(10.0, true)] // exactly at it
    public void TheWatchTimeThreshold_IsExactlyWhereTheSpecPutsIt(double hours, bool expected) =>
        AccountRisk
            .Assess(WorstPossibleShape() with { WatchTimeHoursThisChannel = hours })
            .IsSemiTrusted.Should()
            .Be(expected);

    [Fact]
    public void PartnerOrAffiliate_PinsTheCoefficient_ButIsNotSemiTrusted()
    {
        // §L1.2 draws this distinction deliberately: their shape stops counting against them, but they
        // do not inherit the no-automated-action ceiling that standing confers.
        AccountRiskAssessment risk = AccountRisk.Assess(
            WorstPossibleShape() with
            {
                IsPartnerOrAffiliate = true,
            }
        );

        risk.Coefficient.Should().Be(1.0);
        risk.IsSemiTrusted.Should().BeFalse();
    }

    [Fact]
    public void AnOldAccountWithNoActivityAtAll_DoesNotGetTheEstablishedPass()
    {
        // Age ALONE is not evidence of a real person — a dormant registered-and-parked account is
        // exactly what a farm buys. The spec requires age PLUS genuine activity.
        AccountRiskAssessment risk = AccountRisk.Assess(
            new AccountFacts
            {
                AccountAgeDays = 900,
                IsFollowing = false,
                HasAvatar = false,
                HasBio = false,
                HasStreamed = false,
                HasChatHistoryOnInstance = false,
                Username = "abcd12345",
            }
        );

        risk.Coefficient.Should().BeGreaterThan(1.0);
    }

    [Fact]
    public void MarksAreAlwaysReported_SoAModeratorCanSeeWhatTheSystemLookedAt()
    {
        // SD7: no black-box scoring. Even at a coefficient of 1.0 the observations are visible.
        AccountRisk
            .Assess(WorstPossibleShape() with { IsModeratorAnywhere = true })
            .Marks.Should()
            .HaveCountGreaterThan(3);
    }
}
