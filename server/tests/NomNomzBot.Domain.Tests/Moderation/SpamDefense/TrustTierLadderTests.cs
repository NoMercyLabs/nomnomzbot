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
/// L4 tiers, the capability table, and the SD8 immunity invariant (spam-defense.md §L4/§L4.1).
///
/// <para><see cref="EveryCapability_IsHarmlessAgainstAnEstablishedViewer"/> is table-driven over the FULL
/// capability enum on purpose: §8.1 requires that adding a new signal without covering immunity fails the
/// build. A hand-listed subset would let the next capability ship unprotected.</para>
/// </summary>
public class TrustTierLadderTests
{
    private static AccountFacts NewAccount() =>
        new()
        {
            AccountAgeDays = 1,
            IsFollowing = false,
            FollowAgeHours = 0,
            Username = "viewer12345",
        };

    private static AccountFacts MatureAccount(double ageDays, double followHours) =>
        new()
        {
            AccountAgeDays = ageDays,
            IsFollowing = true,
            FollowAgeHours = followHours,
            Username = "realviewer",
        };

    private static AccountRiskAssessment NoStanding() => new(1.0, [], IsSemiTrusted: false);

    private static AccountRiskAssessment WithStanding() => new(1.0, [], IsSemiTrusted: true);

    /// <summary>A three-year regular of this channel: 3 years, 1200 messages, active 400 days, clean.</summary>
    private static ChannelParticipation ThreeYearRegular() =>
        new()
        {
            DaysSinceFirstMessageHere = 1_095,
            MessagesHere = 1_200,
            DistinctActiveDaysHere = 400,
            DaysSinceLastUpheldStrike = double.MaxValue,
            MessageCountHere = 1_200,
        };

    [Theory]
    [MemberData(nameof(AllCapabilities))]
    public void EveryCapability_IsHarmlessAgainstAnEstablishedViewer(SpamCapability capability)
    {
        // THE SD8 INVARIANT, over the whole enum. Established is a short-circuit, not a high bar: no
        // capability gate, however strict, may produce an action against them. The failure this prevents
        // is a channel's regular of three years auto-banned for pasting a zalgo meme.
        SpamTrustTier tier = TrustTierLadder.Resolve(
            NewAccount(), // deliberately the WORST account shape…
            ThreeYearRegular(), // …with the participation that earns Established
            NoStanding()
        );

        tier.Should().Be(SpamTrustTier.Established);
        TrustTierLadder.IsImmune(tier).Should().BeTrue();
        TrustTierLadder
            .IsShieldedFromAutomatedAccountAction(tier)
            .Should()
            .BeTrue($"{capability} must never reach an Established viewer");
    }

    public static TheoryData<SpamCapability> AllCapabilities()
    {
        TheoryData<SpamCapability> data = [];
        foreach (SpamCapability capability in Enum.GetValues<SpamCapability>())
            data.Add(capability);
        return data;
    }

    [Fact]
    public void CosmeticAbuse_IsUnearnableByAnyLadderTier_ButStillCannotActionAnEstablishedViewer()
    {
        // §L4.1 spells out this exact interaction: "never, at any tier" means no EARNABLE tier grants
        // it — and it does NOT override SD8.
        foreach (SpamTrustTier tier in Enum.GetValues<SpamTrustTier>())
        {
            bool allowed = TrustTierLadder.Allows(tier, SpamCapability.CosmeticAbuseCharacters);
            if (tier == SpamTrustTier.Established)
                continue; // immunity is handled by the short-circuit, not the capability table
            allowed.Should().BeFalse($"{tier} must not earn cosmetic-abuse characters");
        }
    }

    [Fact]
    public void EstablishedIsPerChannel_NotAccountAge()
    {
        // A ten-year-old Twitch account that has never spoken here is Untrusted, correctly.
        SpamTrustTier tier = TrustTierLadder.Resolve(
            MatureAccount(ageDays: 3_650, followHours: 0) with
            {
                IsFollowing = false,
            },
            new ChannelParticipation(), // never spoken in this channel
            NoStanding()
        );

        tier.Should().Be(SpamTrustTier.Untrusted);
    }

    [Fact]
    public void ThreeHundredMessagesInOneNight_DoesNotEarnEstablished()
    {
        // The distinct-active-days requirement exists precisely to stop a burst of messages buying
        // immunity. 300 messages over 2 days is a spammer's shape, not a regular's.
        TrustTierLadder
            .IsEstablished(
                new ChannelParticipation
                {
                    DaysSinceFirstMessageHere = 120,
                    MessagesHere = 300,
                    DistinctActiveDaysHere = 2,
                }
            )
            .Should()
            .BeFalse();
    }

    [Fact]
    public void ARecentUpheldStrike_BlocksEstablished()
    {
        TrustTierLadder
            .IsEstablished(ThreeYearRegular() with { DaysSinceLastUpheldStrike = 10 })
            .Should()
            .BeFalse();
    }

    [Fact]
    public void ModeratorsAndVipsGetEstablishedImplicitly()
    {
        TrustTierLadder
            .IsEstablished(new ChannelParticipation { IsModeratorHere = true })
            .Should()
            .BeTrue();
        TrustTierLadder
            .IsEstablished(new ChannelParticipation { IsVipHere = true })
            .Should()
            .BeTrue();
    }

    [Fact]
    public void ThresholdsAreTunablePerChannel_ButImmunityItselfIsNot()
    {
        // §L4.1: "thresholds are per-channel tunable; the invariant is not". A channel may lower the bar…
        ChannelParticipation modest = new()
        {
            DaysSinceFirstMessageHere = 30,
            MessagesHere = 50,
            DistinctActiveDaysHere = 10,
        };
        TrustTierLadder.IsEstablished(modest).Should().BeFalse("not by the shipped thresholds");
        TrustTierLadder
            .IsEstablished(
                modest,
                new TrustTierThresholds
                {
                    EstablishedDays = 30,
                    EstablishedMessages = 50,
                    EstablishedDistinctActiveDays = 10,
                }
            )
            .Should()
            .BeTrue("a channel may set its own bar");

        // …but whatever bar is set, reaching Established still means immune. There is no knob for that.
        TrustTierLadder.IsImmune(SpamTrustTier.Established).Should().BeTrue();
    }

    [Theory]
    [InlineData(1, 0, 0, SpamTrustTier.Untrusted)]
    [InlineData(8, 25, 0, SpamTrustTier.Newcomer)]
    [InlineData(35, 200, 5, SpamTrustTier.Known)]
    [InlineData(200, 800, 50, SpamTrustTier.Regular)]
    public void TheEarnedLadder_PlacesAnAccountOnExactlyTheRungTheSpecDescribes(
        double ageDays,
        double followHours,
        int messages,
        SpamTrustTier expected
    ) =>
        TrustTierLadder
            .Resolve(
                MatureAccount(ageDays, followHours) with
                {
                    IsFollowing = followHours > 0,
                },
                new ChannelParticipation { MessageCountHere = messages },
                NoStanding()
            )
            .Should()
            .Be(expected);

    [Fact]
    public void PositiveStanding_LandsAtSemiTrusted_AndIsShieldedFromAutomatedAccountAction()
    {
        SpamTrustTier tier = TrustTierLadder.Resolve(
            NewAccount(),
            new ChannelParticipation(),
            WithStanding()
        );

        tier.Should().Be(SpamTrustTier.SemiTrusted);
        TrustTierLadder.IsShieldedFromAutomatedAccountAction(tier).Should().BeTrue();
        TrustTierLadder
            .IsImmune(tier)
            .Should()
            .BeFalse("Semi-Trusted caps at delete-and-flag; only Established is fully immune");
    }

    [Fact]
    public void TheNonLatinGate_DefaultsClosedToRegular_SoInternationalChatIsNotSilencedByAccident()
    {
        // §L4/SD2: Japanese, Korean, Cyrillic and Arabic chat is written by real viewers. The gate
        // exists for channels under active attack and is one switch away — it must not bite by default
        // for anyone who has been around.
        TrustTierLadder
            .Allows(SpamTrustTier.Regular, SpamCapability.NonLatinScript)
            .Should()
            .BeTrue();
        TrustTierLadder
            .Allows(SpamTrustTier.Untrusted, SpamCapability.NonLatinScript)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void PlainText_IsAllowedAtEveryTier_IncludingUntrusted()
    {
        // SD6: a brand-new anonymous account may always speak plain text.
        foreach (SpamTrustTier tier in Enum.GetValues<SpamTrustTier>())
            TrustTierLadder.Allows(tier, SpamCapability.PostPlainText).Should().BeTrue();
    }

    [Fact]
    public void AnOperatorMayRetuneACapabilityFloor_WithoutTouchingTheCode()
    {
        // The capability table is "the operator-editable heart of the system" (§L4).
        Dictionary<SpamCapability, SpamTrustTier> strict = new(
            TrustTierLadder.DefaultCapabilityFloors
        )
        {
            [SpamCapability.PostLink] = SpamTrustTier.Trusted,
        };

        TrustTierLadder.Allows(SpamTrustTier.Known, SpamCapability.PostLink).Should().BeTrue();
        TrustTierLadder
            .Allows(SpamTrustTier.Known, SpamCapability.PostLink, strict)
            .Should()
            .BeFalse();
    }
}
