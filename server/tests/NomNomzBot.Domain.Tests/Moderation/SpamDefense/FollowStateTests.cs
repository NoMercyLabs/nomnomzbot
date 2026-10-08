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
/// Follow standing is three-valued. "We could not find out" (a failed lookup, a missing scope, a
/// platform with no follow-date API) must never read as "does not follow".
/// </summary>
public class FollowStateTests
{
    private static AccountFacts Account(double ageDays, FollowState follow, double followHours) =>
        new()
        {
            AccountAgeDays = ageDays,
            Follow = follow,
            FollowAgeHours = followHours,
            Username = "realviewer",
        };

    private static AccountRiskAssessment NoStanding() => new(1.0, [], IsSemiTrusted: false);

    [Fact]
    public void ATenDayFollower_OnAThirtyDayAccount_ReachesKnown_WhenTheMessageCountIsMet()
    {
        SpamTrustTier tier = TrustTierLadder.Resolve(
            Account(30, FollowState.Following, 10 * 24),
            new ChannelParticipation { MessageCountHere = 5 },
            NoStanding()
        );

        tier.Should().Be(SpamTrustTier.Known);
    }

    [Fact]
    public void ATenDayFollower_BelowTheMessageCount_StopsAtNewcomer()
    {
        SpamTrustTier tier = TrustTierLadder.Resolve(
            Account(30, FollowState.Following, 10 * 24),
            new ChannelParticipation { MessageCountHere = 4 },
            NoStanding()
        );

        tier.Should().Be(SpamTrustTier.Newcomer);
    }

    [Fact]
    public void ATwoHourFollower_StaysBelowNewcomer_EvenWithAnOldAccountAndManyMessages()
    {
        SpamTrustTier tier = TrustTierLadder.Resolve(
            Account(400, FollowState.Following, 2),
            new ChannelParticipation { MessageCountHere = 40 },
            NoStanding()
        );

        tier.Should().Be(SpamTrustTier.Untrusted);
    }

    [Theory]
    [InlineData(FollowState.Unknown)]
    [InlineData(FollowState.NotFollowing)]
    public void WithoutAKnownFollow_NoFollowBasedTierIsEarned(FollowState follow)
    {
        SpamTrustTier tier = TrustTierLadder.Resolve(
            Account(400, follow, 9_999),
            new ChannelParticipation { MessageCountHere = 60 },
            NoStanding()
        );

        tier.Should().Be(SpamTrustTier.Untrusted);
    }

    [Fact]
    public void AnUnknownFollow_AddsNoRiskMark_ButANonFollowerAndABrandNewFollowerDo()
    {
        AccountRisk
            .Assess(Account(400, FollowState.Unknown, 0), 10, 25)
            .Marks.Should()
            .NotContain(AccountRiskMark.NotFollowingOrBrandNewFollow);

        AccountRisk
            .Assess(Account(400, FollowState.NotFollowing, 0), 10, 25)
            .Marks.Should()
            .Contain(AccountRiskMark.NotFollowingOrBrandNewFollow);

        AccountRisk
            .Assess(Account(400, FollowState.Following, 2), 10, 25)
            .Marks.Should()
            .Contain(AccountRiskMark.NotFollowingOrBrandNewFollow);

        AccountRisk
            .Assess(Account(400, FollowState.Following, 48), 10, 25)
            .Marks.Should()
            .NotContain(AccountRiskMark.NotFollowingOrBrandNewFollow);
    }

    [Fact]
    public void AnUnknownFollow_DoesNotRaiseTheCoefficient_OfAnOtherwiseOrdinaryYoungAccount()
    {
        double unknown = AccountRisk
            .Assess(Account(20, FollowState.Unknown, 0), 10, 25)
            .Coefficient;
        double notFollowing = AccountRisk
            .Assess(Account(20, FollowState.NotFollowing, 0), 10, 25)
            .Coefficient;

        notFollowing.Should().BeApproximately(unknown * 1.2, 1e-9);
    }

    [Fact]
    public void FollowAgeCanChangeTheTier_OnlyForAnAccountOldEnoughToEarnNewcomer()
    {
        TrustTierLadder
            .FollowCanChangeTier(
                Account(3, FollowState.Unknown, 0),
                new ChannelParticipation(),
                NoStanding()
            )
            .Should()
            .BeFalse("an account under 7 days cannot reach Newcomer however long it has followed");

        TrustTierLadder
            .FollowCanChangeTier(
                Account(20, FollowState.Unknown, 0),
                new ChannelParticipation(),
                NoStanding()
            )
            .Should()
            .BeTrue();
    }

    [Fact]
    public void FollowAgeCannotChangeTheTier_OfAViewerAlreadyAboveTheEarnedLadder()
    {
        AccountFacts account = Account(400, FollowState.Unknown, 0);

        TrustTierLadder
            .FollowCanChangeTier(
                account,
                new ChannelParticipation { IsSubscriberHere = true },
                NoStanding()
            )
            .Should()
            .BeFalse("a subscriber is Trusted regardless of follow");

        TrustTierLadder
            .FollowCanChangeTier(
                account,
                new ChannelParticipation(),
                new AccountRiskAssessment(1.0, [], IsSemiTrusted: true)
            )
            .Should()
            .BeFalse("standing elsewhere is SemiTrusted regardless of follow");

        TrustTierLadder
            .FollowCanChangeTier(
                account,
                new ChannelParticipation { OperatorGrantedEstablished = true },
                NoStanding()
            )
            .Should()
            .BeFalse("Established is decided before any follow is read");
    }
}
