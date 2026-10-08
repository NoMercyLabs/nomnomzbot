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

public class SpamEnforcementCampaignTests
{
    public static TheoryData<SpamTrustTier> UnshieldedTiers() =>
        [
            SpamTrustTier.Untrusted,
            SpamTrustTier.Newcomer,
            SpamTrustTier.Known,
            SpamTrustTier.Regular,
        ];

    public static TheoryData<SpamTrustTier> ShieldedTiers() =>
        [SpamTrustTier.SemiTrusted, SpamTrustTier.Trusted, SpamTrustTier.Established];

    [Theory]
    [MemberData(nameof(UnshieldedTiers))]
    public void ACampaignMember_BelowStanding_IsEscalated_EvenWhenTheMessageAloneIsHarmless(
        SpamTrustTier tier
    )
    {
        SpamDecision perMessage = SpamEnforcement.Decide(SpamConfidence.Zero, tier, dryRun: false);

        SpamDecision escalated = SpamEnforcement.EscalateForCampaign(perMessage, tier);

        escalated.Outcome.Should().Be(SpamOutcome.DeleteAndEscalate);
        escalated.WouldHaveBeen.Should().Be(SpamOutcome.DeleteAndEscalate);
        escalated.IsDryRun.Should().BeFalse();
        escalated.TouchesAccount.Should().BeTrue();
        escalated.Reason.Should().Contain("campaign");
    }

    [Theory]
    [MemberData(nameof(UnshieldedTiers))]
    public void ADryRunCampaignEscalation_ActsOnNothing_ButRecordsWhatWouldHaveHappened(
        SpamTrustTier tier
    )
    {
        SpamDecision perMessage = SpamEnforcement.Decide(SpamConfidence.Zero, tier, dryRun: true);

        SpamDecision escalated = SpamEnforcement.EscalateForCampaign(perMessage, tier);

        escalated.Outcome.Should().Be(SpamOutcome.None);
        escalated.WouldHaveBeen.Should().Be(SpamOutcome.DeleteAndEscalate);
        escalated.IsDryRun.Should().BeTrue();
        escalated.TouchesAccount.Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(ShieldedTiers))]
    public void AViewerWithStanding_IsNeverEscalatedByACampaign(SpamTrustTier tier)
    {
        SpamDecision perMessage = SpamEnforcement.Decide(SpamConfidence.Zero, tier, dryRun: false);

        SpamDecision escalated = SpamEnforcement.EscalateForCampaign(perMessage, tier);

        escalated.Should().Be(perMessage);
        escalated.TouchesAccount.Should().BeFalse();
    }

    [Fact]
    public void ADecisionThatAlreadyEscalates_IsReturnedUnchanged()
    {
        SpamDecision perMessage = SpamEnforcement.Decide(
            SpamConfidence.High,
            SpamTrustTier.Untrusted,
            dryRun: false
        );

        SpamEnforcement
            .EscalateForCampaign(perMessage, SpamTrustTier.Untrusted)
            .Should()
            .Be(perMessage);
    }
}
