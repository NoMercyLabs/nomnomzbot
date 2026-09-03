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
/// L5 enforcement + dry run (spam-defense.md §L5, §6.2). These are the tests that decide whether the
/// engine can hurt somebody it shouldn't, so several are written table-driven over the full confidence
/// and tier enums: a ceiling that holds for the cases someone remembered to list is not a ceiling.
/// </summary>
public class SpamEnforcementTests
{
    public static TheoryData<SpamConfidence> AllConfidences()
    {
        TheoryData<SpamConfidence> data = [];
        foreach (SpamConfidence c in Enum.GetValues<SpamConfidence>())
            data.Add(c);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllConfidences))]
    public void AnEstablishedViewer_NeverGetsMoreThanAFlag_AtAnyConfidence(
        SpamConfidence confidence
    )
    {
        // SD8 over the WHOLE confidence enum. Not "high confidence is softened" — nothing above Flag is
        // reachable, so a future confidence level cannot quietly become the one that gets through.
        SpamDecision decision = SpamEnforcement.Decide(
            confidence,
            SpamTrustTier.Established,
            dryRun: false
        );

        decision.Outcome.Should().BeOneOf(SpamOutcome.Flag);
        decision.TouchesAccount.Should().BeFalse();
        decision
            .WouldHaveBeen.Should()
            .Be(
                SpamOutcome.Flag,
                "even the counterfactual must never show a ban for an Established viewer"
            );
    }

    [Theory]
    [MemberData(nameof(AllConfidences))]
    public void ASemiTrustedViewer_IsNeverAutoActionedOnTheAccount_AtAnyConfidence(
        SpamConfidence confidence
    )
    {
        // SD11's ceiling: the engine may delete and flag. A human decides everything past that.
        SpamDecision decision = SpamEnforcement.Decide(
            confidence,
            SpamTrustTier.SemiTrusted,
            dryRun: false
        );

        decision.TouchesAccount.Should().BeFalse();
        decision
            .Outcome.Should()
            .BeOneOf(SpamOutcome.None, SpamOutcome.Flag, SpamOutcome.DeleteAndQueue);
    }

    [Theory]
    [MemberData(nameof(AllConfidences))]
    public void DryRunIsTheDefault_SoForgettingToPassItCannotStartPunishingPeople(
        SpamConfidence confidence
    )
    {
        // The parameter defaults to true on purpose: a caller that has not explicitly opted into
        // enforcement observes rather than acts (§6.2).
        SpamDecision decision = SpamEnforcement.Decide(confidence, SpamTrustTier.Untrusted);

        decision.IsDryRun.Should().BeTrue();
        decision.Outcome.Should().Be(SpamOutcome.None, "dry run acts on nothing at all");
    }

    [Fact]
    public void DryRun_RecordsExactlyWhatWouldHaveHappened()
    {
        // This is the whole safety story: the operator reads a week of these and sees a wrongly-caught
        // regular in a LIST instead of in an apology.
        SpamDecision observed = SpamEnforcement.Decide(
            SpamConfidence.High,
            SpamTrustTier.Untrusted,
            dryRun: true
        );

        observed.Outcome.Should().Be(SpamOutcome.None, "nothing happens");
        observed
            .WouldHaveBeen.Should()
            .Be(SpamOutcome.DeleteAndEscalate, "but the dashboard can show what would have");
    }

    [Fact]
    public void EnforcingAndObserving_AgreeOnTheVerdict_AndDifferOnlyInWhetherItIsApplied()
    {
        // If these could diverge, a week of dry-run observation would not predict what enforcement does,
        // and the whole "look before you switch it on" promise would be worthless.
        foreach (SpamConfidence confidence in Enum.GetValues<SpamConfidence>())
        foreach (SpamTrustTier tier in Enum.GetValues<SpamTrustTier>())
        {
            SpamOutcome observed = SpamEnforcement
                .Decide(confidence, tier, dryRun: true)
                .WouldHaveBeen;
            SpamOutcome enforced = SpamEnforcement.Decide(confidence, tier, dryRun: false).Outcome;

            observed
                .Should()
                .Be(enforced, $"dry run must predict enforcement for {tier}/{confidence}");
        }
    }

    [Theory]
    [MemberData(nameof(AllTiers))]
    public void MediumConfidence_NeverTouchesTheAccount_AtAnyTier(SpamTrustTier tier)
    {
        // SD1 is explicit: medium confidence deletes and queues, and never touches the account. The
        // message is recoverable and restoring it credits the sender's trust.
        SpamDecision decision = SpamEnforcement.Decide(SpamConfidence.Medium, tier, dryRun: false);

        decision.TouchesAccount.Should().BeFalse();
    }

    public static TheoryData<SpamTrustTier> AllTiers()
    {
        TheoryData<SpamTrustTier> data = [];
        foreach (SpamTrustTier t in Enum.GetValues<SpamTrustTier>())
            data.Add(t);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllTiers))]
    public void ZeroConfidence_DoesNothingToAnyone_WhateverTheirTier(SpamTrustTier tier)
    {
        // SD10's landing zone: every silent, new or odd-looking account saying something ordinary.
        SpamDecision decision = SpamEnforcement.Decide(SpamConfidence.Zero, tier, dryRun: false);

        decision.Outcome.Should().BeOneOf(SpamOutcome.None, SpamOutcome.Flag);
        decision.TouchesAccount.Should().BeFalse();
    }

    [Fact]
    public void HighConfidenceAgainstAnUntrustedAccount_IsTheOneCaseThatReachesEscalation()
    {
        // The system must still work: if nothing ever escalated, it would be theatre.
        SpamDecision decision = SpamEnforcement.Decide(
            SpamConfidence.High,
            SpamTrustTier.Untrusted,
            dryRun: false
        );

        decision.Outcome.Should().Be(SpamOutcome.DeleteAndEscalate);
        decision.TouchesAccount.Should().BeTrue();
    }

    [Fact]
    public void EveryDecisionCarriesAnExplanation_IncludingTheOnesWhereNothingHappened()
    {
        // SD7: no black-box verdicts. A moderator can see the system looked and chose not to act.
        foreach (SpamConfidence confidence in Enum.GetValues<SpamConfidence>())
        foreach (SpamTrustTier tier in Enum.GetValues<SpamTrustTier>())
            SpamEnforcement
                .Decide(confidence, tier, dryRun: false)
                .Reason.Should()
                .NotBeNullOrWhiteSpace($"{tier}/{confidence} must explain itself");
    }

    [Fact]
    public void TheImmunityExplanation_SaysWhyNothingHappened_NotJustThatNothingDid()
    {
        SpamEnforcement
            .Decide(SpamConfidence.High, SpamTrustTier.Established, dryRun: false)
            .Reason.Should()
            .Contain("never actioned automatically");
    }
}
