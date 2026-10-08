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
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.SpamDefense;

namespace NomNomzBot.Domain.Tests.Moderation.SpamDefense;

/// <summary>
/// Twitch's suspicious-user flag on the trust ladder: a restricted chatter is Untrusted whatever else is true
/// of them, a monitored chatter never rises above Newcomer, and no flag changes nothing.
/// </summary>
public class TrustTierLadderLowTrustTests
{
    private static AccountFacts RegularAccount(string lowTrustStatus) =>
        new()
        {
            AccountAgeDays = 400,
            Follow = FollowState.Following,
            FollowAgeHours = 60 * 24,
            Username = "realviewer",
            LowTrustStatus = lowTrustStatus,
        };

    private static ChannelParticipation ThreeYearRegular(bool operatorGranted = false) =>
        new()
        {
            DaysSinceFirstMessageHere = 1_095,
            MessagesHere = 1_200,
            DistinctActiveDaysHere = 400,
            DaysSinceLastUpheldStrike = double.MaxValue,
            MessageCountHere = 1_200,
            OperatorGrantedEstablished = operatorGranted,
        };

    private static AccountRiskAssessment NoStanding() => new(1.0, [], IsSemiTrusted: false);

    [Fact]
    public void No_flag_leaves_the_earned_tier_alone()
    {
        SpamTrustTier tier = TrustTierLadder.Resolve(
            RegularAccount(LowTrustStatuses.None),
            ThreeYearRegular(),
            NoStanding()
        );

        tier.Should().Be(SpamTrustTier.Established);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_restricted_chatter_is_untrusted_even_with_a_long_history_or_an_operator_grant(
        bool operatorGranted
    )
    {
        SpamTrustTier tier = TrustTierLadder.Resolve(
            RegularAccount(LowTrustStatuses.Restricted),
            ThreeYearRegular(operatorGranted),
            NoStanding()
        );

        tier.Should().Be(SpamTrustTier.Untrusted);
        TrustTierLadder.IsImmune(tier).Should().BeFalse();
    }

    [Fact]
    public void A_restricted_chatter_with_semi_trusted_standing_is_still_untrusted()
    {
        SpamTrustTier tier = TrustTierLadder.Resolve(
            RegularAccount(LowTrustStatuses.Restricted),
            new ChannelParticipation(),
            new AccountRiskAssessment(1.0, [], IsSemiTrusted: true)
        );

        tier.Should().Be(SpamTrustTier.Untrusted);
    }

    [Fact]
    public void A_monitored_chatter_who_earned_established_is_capped_at_newcomer()
    {
        SpamTrustTier tier = TrustTierLadder.Resolve(
            RegularAccount(LowTrustStatuses.ActiveMonitoring),
            ThreeYearRegular(),
            NoStanding()
        );

        tier.Should().Be(SpamTrustTier.Newcomer);
    }

    [Fact]
    public void A_monitored_chatter_below_newcomer_stays_untrusted()
    {
        AccountFacts brandNew = new()
        {
            AccountAgeDays = 1,
            Follow = FollowState.NotFollowing,
            Username = "fresh123",
            LowTrustStatus = LowTrustStatuses.ActiveMonitoring,
        };

        TrustTierLadder
            .Resolve(brandNew, new ChannelParticipation(), NoStanding())
            .Should()
            .Be(SpamTrustTier.Untrusted);
    }

    [Fact]
    public void A_restricted_chatter_never_needs_the_paid_follow_lookup()
    {
        TrustTierLadder
            .FollowCanChangeTier(
                RegularAccount(LowTrustStatuses.Restricted),
                new ChannelParticipation(),
                NoStanding()
            )
            .Should()
            .BeFalse();
    }
}
