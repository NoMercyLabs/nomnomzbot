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
/// L3 correlation (spam-defense.md §L3.0, §L3.0.1, §L3.1).
///
/// <para>This is the most dangerous layer in the design, because a beloved copypasta and a spam campaign
/// are the same shape. The tests are written around the scenario the spec says decides whether the system
/// is safe — strangers start, regulars join — rather than around the happy path.</para>
/// </summary>
public class CampaignCohortTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 3, 20, 0, 0, TimeSpan.Zero);

    private static CampaignCohort NewCohort(CohortThresholds? thresholds = null) =>
        new("bestviewersonbigfollowscom", T0, thresholds);

    /// <summary>Adds <paramref name="count"/> distinct no-standing accounts.</summary>
    private static void AddStrangers(
        CampaignCohort cohort,
        int count,
        DateTimeOffset at,
        string prefix = "stranger"
    )
    {
        for (int i = 0; i < count; i++)
            cohort.Observe($"{prefix}{i}", SpamTrustTier.Untrusted, at);
    }

    private static void AddRegulars(CampaignCohort cohort, int count, DateTimeOffset at)
    {
        for (int i = 0; i < count; i++)
            cohort.Observe($"regular{i}", SpamTrustTier.Established, at);
    }

    // ---- Qualification -------------------------------------------------------------------------

    [Fact]
    public void FiveStrangersPostingTheSameThing_IsACampaign()
    {
        CampaignCohort cohort = NewCohort();
        AddStrangers(cohort, 5, T0);

        cohort.Verdict.Should().Be(CohortVerdict.Campaign);
    }

    [Fact]
    public void FourStrangers_IsNotACampaign_HoweverIdenticalTheText()
    {
        // The minimum-accounts floor is what stops two friends quoting each other from being a campaign.
        CampaignCohort cohort = NewCohort();
        AddStrangers(cohort, 4, T0);

        cohort.Verdict.Should().Be(CohortVerdict.Watching);
    }

    [Fact]
    public void TheSameAccountPostingFiveTimes_IsNotFiveAccounts()
    {
        // "Distinct accounts" has to mean distinct, or one spammer on a loop qualifies as a cohort — and
        // worse, one excited regular repeating a joke does.
        CampaignCohort cohort = NewCohort();
        for (int i = 0; i < 5; i++)
            cohort.Observe("thesameperson", SpamTrustTier.Untrusted, T0);

        cohort.QualificationSet.Should().HaveCount(1);
        cohort.Verdict.Should().Be(CohortVerdict.Watching);
    }

    [Fact]
    public void ACohortThatIsMostlyRegulars_IsCommunityBehaviour_AndIsNeverActioned()
    {
        // Seven regulars and three strangers pasting the same phrase is an in-joke, not an attack.
        CampaignCohort cohort = NewCohort();
        AddStrangers(cohort, 3, T0);
        AddRegulars(cohort, 7, T0);

        cohort.Verdict.Should().Be(CohortVerdict.Watching, "it never qualified in the first place");
        cohort.MayActOn("stranger0", T0.AddMinutes(1)).Should().BeFalse();
    }

    [Fact]
    public void RegularsPresentFromTheStart_KeepACohortFromEverQualifying()
    {
        // Eight strangers alone would qualify. With two regulars already in the phrase the share is
        // exactly 80% and it still does — but with three regulars it never crosses the line at all.
        // The regulars' presence IS the signal.
        CampaignCohort atTheLine = NewCohort();
        AddRegulars(atTheLine, 2, T0);
        AddStrangers(atTheLine, 8, T0);
        atTheLine.NoStandingShare.Should().Be(0.8);
        atTheLine.Verdict.Should().Be(CohortVerdict.Campaign);

        CampaignCohort belowTheLine = NewCohort();
        AddRegulars(belowTheLine, 3, T0);
        AddStrangers(belowTheLine, 7, T0);
        belowTheLine.Verdict.Should().Be(CohortVerdict.Watching, "70% never reaches the 80% bar");
    }

    [Fact]
    public void WhenRegularsArriveLate_TheCohortQualifiesFirstAndIsThenReversed()
    {
        // The counterpart to the test above, and a real property of the design rather than a wart:
        // evaluation is incremental, so seven strangers posting BEFORE any regular does qualify at
        // that moment. Safety there comes from the action delay and from reversal, not from
        // prevention — which is exactly why both of those are mandatory and not tuning options.
        CampaignCohort cohort = NewCohort();
        AddStrangers(cohort, 7, T0);
        cohort
            .Verdict.Should()
            .Be(CohortVerdict.Campaign, "at this instant they are all strangers");

        AddRegulars(cohort, 5, T0.AddSeconds(3));

        cohort.NoStandingShare.Should().BeApproximately(0.583, 0.001);
        cohort.Verdict.Should().Be(CohortVerdict.CommunityPattern);
        cohort
            .MayActOn("stranger0", T0.AddSeconds(8))
            .Should()
            .BeFalse(
                "the regulars arrived within the 8-second delay, so nothing was ever actioned"
            );
    }

    // ---- The scenario the spec says decides whether this is safe --------------------------------

    [Fact]
    public void StrangersStartAndRegularsJoin_TheCampaignReversesItself()
    {
        // §L3.0.1, the live case. Twenty strangers post a phrase, it qualifies, actions fire — and then
        // the regulars pile in, because it was the community mocking the spam.
        CampaignCohort cohort = NewCohort();
        AddStrangers(cohort, 20, T0);
        cohort.Verdict.Should().Be(CohortVerdict.Campaign);

        DateTimeOffset afterDelay = T0.AddSeconds(10);
        cohort.MayActOn("stranger0", afterDelay).Should().BeTrue();
        cohort.RecordAction("stranger0");
        cohort.RecordAction("stranger1");

        // 15 regulars join: 20 of 35 have no standing = 57%, below the 65% de-qualify line.
        AddRegulars(cohort, 15, T0.AddMinutes(1));

        cohort.Verdict.Should().Be(CohortVerdict.CommunityPattern);
        cohort
            .MayActOn("stranger2", T0.AddMinutes(2))
            .Should()
            .BeFalse("all further action stops immediately");

        CampaignReversal? reversal = cohort.BuildReversal();
        reversal.Should().NotBeNull();
        reversal!
            .AccountsToRestore.Should()
            .BeEquivalentTo(["stranger0", "stranger1"], "everyone it actioned is restored");
        reversal.RemoveSkeletonFromCorpus.Should().BeTrue();
        reversal.OperatorMessage.Should().Contain("15 regulars");
    }

    [Fact]
    public void ADequalifiedCohort_NeverRequalifies_HoweverManyStrangersArriveAfterwards()
    {
        // One-way, explicitly. Without this latch a spammer could re-trigger the same cohort by simply
        // sending more, and the exoneration the regulars earned would evaporate.
        CampaignCohort cohort = NewCohort();
        AddStrangers(cohort, 20, T0);
        AddRegulars(cohort, 15, T0.AddMinutes(1));
        cohort.Verdict.Should().Be(CohortVerdict.CommunityPattern);

        AddStrangers(cohort, 60, T0.AddMinutes(2), prefix: "wave2");

        cohort.NoStandingShare.Should().BeGreaterThan(0.8, "the raw share is back above the bar");
        cohort.Verdict.Should().Be(CohortVerdict.CommunityPattern, "but the verdict is one-way");
        cohort.MayActOn("wave20", T0.AddMinutes(3)).Should().BeFalse();
    }

    [Fact]
    public void TheHysteresisBand_StopsACohortOnTheLineFromFlapping()
    {
        // Qualified at 80%. A slide to 70% is inside the band: still a campaign, no reversal. Only
        // below 65% does it flip. Without the gap, one regular arriving and leaving the sample would
        // action and un-action people repeatedly.
        CampaignCohort cohort = NewCohort();
        AddStrangers(cohort, 8, T0);
        AddRegulars(cohort, 2, T0);
        cohort.Verdict.Should().Be(CohortVerdict.Campaign);

        cohort.Observe("regular2", SpamTrustTier.Established, T0.AddSeconds(30));
        cohort.NoStandingShare.Should().BeApproximately(0.727, 0.001);
        cohort.Verdict.Should().Be(CohortVerdict.Campaign, "still inside the hysteresis band");

        cohort.Observe("regular3", SpamTrustTier.Established, T0.AddSeconds(40));
        cohort.Observe("regular4", SpamTrustTier.Established, T0.AddSeconds(41));
        cohort.NoStandingShare.Should().BeLessThan(0.65);
        cohort.Verdict.Should().Be(CohortVerdict.CommunityPattern);
    }

    // ---- Who may be acted on (SD9, SD11) --------------------------------------------------------

    [Fact]
    public void AStandingViewerInAQualifiedCampaign_IsNeverActionable()
    {
        // SD11. They counted toward the verdict and are excluded from its consequences.
        CampaignCohort cohort = NewCohort();
        AddStrangers(cohort, 20, T0);
        cohort.Observe("thesub", SpamTrustTier.SemiTrusted, T0);

        cohort.Verdict.Should().Be(CohortVerdict.Campaign);
        cohort.QualificationSet.Should().Contain("thesub");
        cohort.ActionSet.Should().NotContain("thesub");
        cohort.MayActOn("thesub", T0.AddMinutes(1)).Should().BeFalse();
    }

    [Fact]
    public void AnAccountThatNeverPostedTheSkeleton_IsNotAMember_EvenInTheSameWindow()
    {
        // SD9 in its sharpest form: there is no path from "you were in the room" to "you were banned".
        CampaignCohort cohort = NewCohort();
        AddStrangers(cohort, 20, T0);

        cohort.MayActOn("silentlurker", T0.AddMinutes(1)).Should().BeFalse();
    }

    [Fact]
    public void NothingIsActionable_BeforeTheActionDelayElapses()
    {
        // The 8 seconds exist so a regular can exonerate the phrase before anyone is punished for it.
        CampaignCohort cohort = NewCohort();
        AddStrangers(cohort, 20, T0);

        cohort.MayActOn("stranger0", T0.AddSeconds(7)).Should().BeFalse();
        cohort.MayActOn("stranger0", T0.AddSeconds(8)).Should().BeTrue();
    }

    [Fact]
    public void AnOperatorCanTradeVisibleSpamAgainstReversals_ByChangingTheDelay()
    {
        // Both settings are honest, and the spec says the choice is the operator's. A 30-second head
        // start means exoneration nearly always beats the ban; instant means it leans on reversal.
        CohortThresholds patient = new() { ActionDelay = TimeSpan.FromSeconds(30) };
        CampaignCohort cohort = NewCohort(patient);
        AddStrangers(cohort, 20, T0);

        cohort.MayActOn("stranger0", T0.AddSeconds(10)).Should().BeFalse();
        cohort.MayActOn("stranger0", T0.AddSeconds(30)).Should().BeTrue();
    }

    // ---- Windows -------------------------------------------------------------------------------

    [Fact]
    public void EachNewMatchExtendsTheWindow_ButNeverPastTheCap()
    {
        // An attack that keeps trickling must not hold a cohort open forever; a cohort is a statement
        // about a moment.
        CampaignCohort cohort = NewCohort();
        cohort.Observe("a", SpamTrustTier.Untrusted, T0);
        cohort.ExpiresAt.Should().Be(T0.AddMinutes(10));

        cohort.Observe("b", SpamTrustTier.Untrusted, T0.AddMinutes(5));
        cohort.ExpiresAt.Should().Be(T0.AddMinutes(15), "extended by the new match");

        cohort.Observe("c", SpamTrustTier.Untrusted, T0.AddMinutes(14));
        cohort.ExpiresAt.Should().Be(T0.AddMinutes(24), "still under the cap");

        cohort.Observe("d", SpamTrustTier.Untrusted, T0.AddMinutes(23));
        cohort
            .ExpiresAt.Should()
            .Be(T0.AddMinutes(30), "capped at 30 minutes from the FIRST match");
    }

    [Fact]
    public void ObservationsAfterTheWindowCloses_AreIgnored()
    {
        CampaignCohort cohort = NewCohort();
        AddStrangers(cohort, 4, T0);

        cohort.Observe("latecomer", SpamTrustTier.Untrusted, T0.AddMinutes(11));

        cohort.QualificationSet.Should().NotContain("latecomer");
        cohort.Verdict.Should().Be(CohortVerdict.Watching, "the fifth account arrived too late");
    }

    // ---- Network contribution ------------------------------------------------------------------

    [Fact]
    public void ASkeletonEverPostedByAStandingViewer_IsNeverContributedToTheNetwork()
    {
        // The worst outcome this system can produce is a false signature propagated to every
        // subscriber. One regular in the cohort is enough to disqualify it as a signature source —
        // even while the cohort still qualifies as a campaign locally.
        CampaignCohort cohort = NewCohort();
        AddStrangers(cohort, 20, T0);
        cohort.MayContributeToNetwork.Should().BeTrue();

        cohort.Observe("thesub", SpamTrustTier.SemiTrusted, T0.AddSeconds(5));

        cohort.Verdict.Should().Be(CohortVerdict.Campaign, "20 of 21 is still 95%");
        cohort
            .MayContributeToNetwork.Should()
            .BeFalse("but it can never be a network signature again");
    }

    [Fact]
    public void ACleanStrangerOnlyCohort_MayContribute()
    {
        // The system still has to work: if nothing could ever be contributed, the network is theatre.
        CampaignCohort cohort = NewCohort();
        AddStrangers(cohort, 20, T0);

        cohort.MayContributeToNetwork.Should().BeTrue();
    }

    [Fact]
    public void AWatchingCohortProducesNoReversal_BecauseItNeverDidAnything()
    {
        NewCohort().BuildReversal().Should().BeNull();
    }
}
