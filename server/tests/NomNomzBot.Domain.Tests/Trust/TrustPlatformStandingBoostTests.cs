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
/// Twitch affiliates and partners, and verified YouTube channels, earn a trust boost: a platform has
/// already vetted them. The boost is points added before the violation penalties, so it never shields
/// a banned or timed-out viewer.
/// </summary>
public class TrustPlatformStandingBoostTests
{
    private static TrustContext Viewer(
        string twitchBroadcasterType = "",
        bool isYouTubeChannelVerified = false,
        int banCount = 0
    ) =>
        new()
        {
            SuccessfulRequestCount = 2,
            AccountAgeMonths = 3.0,
            ContentAgeMonths = 1.0,
            ContentViewCount = 500,
            IsFollowing = true,
            FollowAgeDays = 30.0,
            TwitchBroadcasterType = twitchBroadcasterType,
            IsYouTubeChannelVerified = isYouTubeChannelVerified,
            BanCount = banCount,
        };

    [Fact]
    public void Affiliate_ScoresHigherThanTheSameViewerWithoutTheBadge()
    {
        double plain = TrustScoreCalculator.Calculate(Viewer());
        double affiliate = TrustScoreCalculator.Calculate(Viewer("affiliate"));

        affiliate.Should().BeApproximately(plain + 5.0, 0.0001);
    }

    [Fact]
    public void Partner_ScoresHigherThanAffiliate()
    {
        double affiliate = TrustScoreCalculator.Calculate(Viewer("affiliate"));
        double partner = TrustScoreCalculator.Calculate(Viewer("partner"));

        partner.Should().BeApproximately(affiliate + 5.0, 0.0001);
    }

    [Fact]
    public void VerifiedYouTubeChannel_GetsItsBoost()
    {
        double plain = TrustScoreCalculator.Calculate(Viewer());
        double verified = TrustScoreCalculator.Calculate(Viewer(isYouTubeChannelVerified: true));

        verified.Should().BeApproximately(plain + 10.0, 0.0001);
    }

    [Fact]
    public void ViolationsStillApplyAfterTheBoost()
    {
        double bannedPartner = TrustScoreCalculator.Calculate(Viewer("partner", banCount: 1));
        double cleanPartner = TrustScoreCalculator.Calculate(Viewer("partner"));

        (cleanPartner - bannedPartner).Should().BeApproximately(30.0, 0.0001);
    }

    [Fact]
    public void TheBoostsAreTunablePerChannel_AndZeroDisablesThem()
    {
        TrustPolicy off = new()
        {
            AffiliateBoost = 0.0,
            PartnerBoost = 0.0,
            VerifiedYouTubeBoost = 0.0,
        };

        TrustScoreCalculator
            .Calculate(Viewer("partner", isYouTubeChannelVerified: true), off)
            .Should()
            .Be(TrustScoreCalculator.Calculate(Viewer(), off));

        TrustPolicy big = new() { PartnerBoost = 20.0 };
        TrustScoreCalculator
            .Calculate(Viewer("partner"), big)
            .Should()
            .BeApproximately(TrustScoreCalculator.Calculate(Viewer(), big) + 20.0, 0.0001);
    }

    [Fact]
    public void ABoostNeverPushesTheScoreAbove100BeforePenalties()
    {
        TrustContext maxed = new()
        {
            SuccessfulRequestCount = 1000,
            AccountAgeMonths = 120,
            ContentAgeMonths = 120,
            ContentViewCount = 100_000_000,
            IsFollowing = true,
            FollowAgeDays = 365,
            TwitchBroadcasterType = "partner",
            IsYouTubeChannelVerified = true,
            BanCount = 1,
        };

        TrustScoreCalculator.Calculate(maxed).Should().BeApproximately(70.0, 0.0001);
    }
}
