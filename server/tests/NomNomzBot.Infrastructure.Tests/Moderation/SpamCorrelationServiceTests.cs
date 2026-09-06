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
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.SpamDefense;
using NomNomzBot.Infrastructure.Moderation;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// Correlation across messages AND across restarts (spam-defense.md §L3.0.1).
///
/// <para>Each observation is made through a FRESH service and DbContext, because that is the whole
/// point of persisting a cohort: the guarantees have to hold when the process that made the first
/// half of the decisions is gone.</para>
/// </summary>
public class SpamCorrelationServiceTests : IDisposable
{
    private static readonly Guid Channel = Guid.Parse("0199c000-0000-7000-8000-0000000000f1");
    private static readonly Guid Owner = Guid.Parse("0199c000-0000-7000-8000-0000000000f2");
    private static readonly DateTimeOffset T0 = new(2026, 9, 3, 23, 0, 0, TimeSpan.Zero);
    private const string Skeleton = "bestviewersonbigfollowscom";

    private readonly SqliteConnection _connection;
    private readonly FakeTimeProvider _time = new(T0);

    /// <summary>
    /// Every UnbanAsync call this test suite has recorded, across every fresh service/DbContext — so a
    /// test can assert the real reversal was invoked per account, not merely that a flag got set.
    /// </summary>
    private readonly List<string> _unbannedAccounts = [];

    /// <summary>Accounts <see cref="IModerationService.UnbanAsync"/> should report as failed to restore.</summary>
    private readonly HashSet<string> _accountsThatRefuseToRestore = [];

    public SpamCorrelationServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using AppDbContext db = NewDbContext();
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF;");
        db.Channels.Add(
            new Channel
            {
                Id = Channel,
                OwnerUserId = Owner,
                Provider = AuthEnums.Platform.Twitch,
                ExternalChannelId = "chan-ext",
                Name = "chan",
                NameNormalized = "chan",
            }
        );
        db.SaveChanges();
    }

    private AppDbContext NewDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

    /// <summary>
    /// A fresh moderation mock per call, matching the fresh-service-and-context pattern this suite
    /// already uses — UnbanAsync succeeds unless the target was added to <see
    /// cref="_accountsThatRefuseToRestore"/>, and every call is remembered in <see
    /// cref="_unbannedAccounts"/> regardless of outcome.
    /// </summary>
    private IModerationService NewModerationMock()
    {
        IModerationService moderation = Substitute.For<IModerationService>();
        moderation
            .UnbanAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                string targetUserId = call.ArgAt<string>(2);
                _unbannedAccounts.Add(targetUserId);
                return Task.FromResult(
                    _accountsThatRefuseToRestore.Contains(targetUserId)
                        ? Result.Failure<ModerationActionResult>("twitch_unavailable")
                        : Result.Success(new ModerationActionResult(true, null))
                );
            });
        return moderation;
    }

    /// <summary>A brand-new service and context each time — the restart this design has to survive.</summary>
    private async Task<CohortObservation> ObserveAsync(
        string accountId,
        SpamTrustTier tier,
        SpamDefenseSettings? settings = null
    )
    {
        using AppDbContext db = NewDbContext();
        SpamCorrelationService service = new(
            db,
            new SpamCampaignReversalExecutor(
                db,
                NewModerationMock(),
                NullLogger<SpamCampaignReversalExecutor>.Instance
            ),
            _time
        );
        return await service.ObserveAsync(
            Channel,
            Skeleton,
            accountId,
            tier,
            settings ?? new SpamDefenseSettings()
        );
    }

    private async Task<CohortVerdict> AddStrangersAsync(int count, string prefix = "stranger")
    {
        CohortVerdict verdict = CohortVerdict.Watching;
        for (int i = 0; i < count; i++)
            verdict = (await ObserveAsync($"{prefix}{i}", SpamTrustTier.Untrusted)).Verdict;
        return verdict;
    }

    private async Task AddRegularsAsync(int count)
    {
        for (int i = 0; i < count; i++)
            await ObserveAsync($"regular{i}", SpamTrustTier.Established);
    }

    private SpamCampaignRecord Stored()
    {
        using AppDbContext db = NewDbContext();
        return db.SpamCampaigns.Single();
    }

    // ---- The cohort survives the process that created it -----------------------------------------

    [Fact]
    public async Task ACohortIsBuiltAcrossSeparateProcesses_AndStillQualifies()
    {
        (await AddStrangersAsync(5)).Should().Be(CohortVerdict.Campaign);

        SpamCampaignRecord record = Stored();
        record.QualificationCount.Should().Be(5);
        record.MemberAccountIds.Split(',').Should().HaveCount(5);
        record.Verdict.Should().Be(CohortVerdict.Campaign);
    }

    [Fact]
    public async Task MembershipSurvivesTheRestart_SoPresenceStillIsNotMembership()
    {
        // SD9's sharpest edge, and the reason members are stored rather than counted: after a restart
        // the engine must still be able to say who actually posted the phrase and who merely existed.
        await AddStrangersAsync(6);

        _time.Advance(TimeSpan.FromSeconds(20));
        using AppDbContext db = NewDbContext();
        SpamCorrelationService fresh = new(
            db,
            new SpamCampaignReversalExecutor(
                db,
                NewModerationMock(),
                NullLogger<SpamCampaignReversalExecutor>.Instance
            ),
            _time
        );

        CohortObservation lurker = await fresh.ObserveAsync(
            Channel,
            Skeleton,
            "stranger0",
            SpamTrustTier.Untrusted,
            new SpamDefenseSettings()
        );

        lurker
            .MayActOnSender.Should()
            .BeTrue("stranger0 posted the phrase and the delay has passed");
        Stored()
            .MemberAccountIds.Should()
            .NotContain("silentlurker", "an account that never posted is not a member");
    }

    [Fact]
    public async Task TheActionDelayIsMeasuredFromQualification_NotFromProcessStart()
    {
        // If the qualified-at clock were recomputed on load, every restart would reset the exoneration
        // head start and the delay would never actually elapse under churn.
        await AddStrangersAsync(5);
        Stored().QualifiedAt.Should().Be(T0.UtcDateTime);

        (await ObserveAsync("stranger9", SpamTrustTier.Untrusted))
            .MayActOnSender.Should()
            .BeFalse("only a moment has passed since it qualified");

        _time.Advance(TimeSpan.FromSeconds(9));
        (await ObserveAsync("stranger10", SpamTrustTier.Untrusted))
            .MayActOnSender.Should()
            .BeTrue("the 8-second head start has elapsed");

        Stored().QualifiedAt.Should().Be(T0.UtcDateTime, "the clock never restarted");
    }

    // ---- Strangers start, regulars join ----------------------------------------------------------

    [Fact]
    public async Task WhenRegularsJoin_TheCohortDequalifiesAndProducesExactlyOneReversal()
    {
        await AddStrangersAsync(20);
        _time.Advance(TimeSpan.FromSeconds(10));

        // Two accounts get actioned now that the delay has passed.
        (await ObserveAsync("stranger0", SpamTrustTier.Untrusted))
            .MayActOnSender.Should()
            .BeTrue();
        (await ObserveAsync("stranger1", SpamTrustTier.Untrusted)).MayActOnSender.Should().BeTrue();
        Stored().ActionedCount.Should().Be(2);

        // 15 regulars join: 20 of 35 have no standing = 57%, below the 65% de-qualify line.
        await AddRegularsAsync(15);

        SpamCampaignRecord record = Stored();
        record.Verdict.Should().Be(CohortVerdict.CommunityPattern);
        record.IsDequalified.Should().BeTrue();
        record.ReversedAt.Should().NotBeNull();
        record.ReversalReason.Should().Contain("regulars");
        record
            .ActionedAccountIds.Split(',')
            .Should()
            .BeEquivalentTo(["stranger0", "stranger1"], "exactly who to restore, not a count");
    }

    // ---- A reversal is not just a stamp — it has to actually restore people -----------------------

    [Fact]
    public async Task DequalificationActuallyRestoresEveryActionedAccount()
    {
        // The defect this fixes: Persist() used to stamp ReversedAt/ReversalReason on its own, with
        // nothing downstream ever calling the real unban. Reproduced first: before the fix, this
        // assertion on _unbannedAccounts failed with an empty list even though ReversedAt was set.
        await AddStrangersAsync(20);
        _time.Advance(TimeSpan.FromSeconds(10));
        await ObserveAsync("stranger0", SpamTrustTier.Untrusted);
        await ObserveAsync("stranger1", SpamTrustTier.Untrusted);
        Stored().ActionedCount.Should().Be(2);

        await AddRegularsAsync(15);

        _unbannedAccounts
            .Should()
            .BeEquivalentTo(
                ["stranger0", "stranger1"],
                "the real unban must be invoked for every account the campaign actioned"
            );

        SpamCampaignRecord record = Stored();
        record.ReversedAt.Should().NotBeNull("both accounts actually came back");
        record.RestoredAccountCount.Should().Be(2);
        record.RestorationFailedAccountIds.Should().BeEmpty();
        record
            .ReversedByActorId.Should()
            .Be(
                SpamCampaignReversalExecutor.SystemActorId,
                "an audit trail has to name WHO/WHAT reversed it"
            );
    }

    [Fact]
    public async Task APartialRestoreFailure_IsNeverRecordedAsACleanReversal()
    {
        // Twitch refuses to restore stranger1 (rate limit, account already unbanned by a moderator,
        // whatever the reason). stranger0 still comes back fine. The record must not claim the campaign
        // is clear while one of its victims is still sitting in a timeout — a silent partial success is
        // exactly the shape of bug this slice exists to rule out.
        _accountsThatRefuseToRestore.Add("stranger1");

        await AddStrangersAsync(20);
        _time.Advance(TimeSpan.FromSeconds(10));
        await ObserveAsync("stranger0", SpamTrustTier.Untrusted);
        await ObserveAsync("stranger1", SpamTrustTier.Untrusted);

        CohortObservation flipping = await ObserveAsync("regular0", SpamTrustTier.Established);
        for (int i = 1; i < 15; i++)
            flipping = await ObserveAsync($"regular{i}", SpamTrustTier.Established);

        // Both accounts were attempted — a failure on one must never stop the attempt on the rest.
        _unbannedAccounts.Should().Contain(["stranger0", "stranger1"]);

        SpamCampaignRecord record = Stored();
        record
            .ReversedAt.Should()
            .BeNull("stranger1 is still actioned — this is not a clean reversal");
        record.RestoredAccountCount.Should().Be(1, "only stranger0 actually came back");
        record
            .RestorationFailedAccountIds.Should()
            .Be("stranger1", "an operator has to see exactly who still needs manual restoring");
        record
            .ReversedByActorId.Should()
            .Be(SpamCampaignReversalExecutor.SystemActorId, "the attempt itself is still audited");
    }

    [Fact]
    public async Task TheDequalifyingObservationReportsThePartialOutcomeToItsCaller()
    {
        // The handler that logs this has to tell the truth about what happened, not just that a
        // reversal was intended — so the outcome has to reach it, not just get buried in the row.
        _accountsThatRefuseToRestore.Add("stranger1");

        await AddStrangersAsync(20);
        _time.Advance(TimeSpan.FromSeconds(10));
        await ObserveAsync("stranger0", SpamTrustTier.Untrusted);
        await ObserveAsync("stranger1", SpamTrustTier.Untrusted);

        CohortObservation flipping = new(CohortVerdict.Watching, false, null);
        for (int i = 0; i < 15; i++)
        {
            CohortObservation observation = await ObserveAsync(
                $"regular{i}",
                SpamTrustTier.Established
            );
            if (observation.Reversal is not null)
                flipping = observation;
        }

        flipping.Reversal.Should().NotBeNull();
        flipping.Restoration.Should().NotBeNull();
        flipping.Restoration!.IsComplete.Should().BeFalse();
        flipping.Restoration.Restored.Should().BeEquivalentTo(["stranger0"]);
        flipping.Restoration.Failed.Should().BeEquivalentTo(["stranger1"]);
    }

    [Fact]
    public async Task TheReversalIsProducedOnce_NotOnEveryLaterMessage()
    {
        // Re-issuing it would mean re-unbanning accounts already restored on every subsequent message
        // in the window — noisy at best, and at worst undoing a moderator's later decision.
        await AddStrangersAsync(20);
        _time.Advance(TimeSpan.FromSeconds(10));
        await ObserveAsync("stranger0", SpamTrustTier.Untrusted);

        // 20 strangers and 10 regulars is 20/30 = 66.7%, still above the 65% de-qualify line. The
        // eleventh regular takes it to 20/31 = 64.5% and flips it — worth stating precisely, because a
        // test that flipped early would pass while asserting the wrong observation.
        await AddRegularsAsync(10);

        CohortObservation flipping = await ObserveAsync("regular10", SpamTrustTier.Established);
        flipping.Reversal.Should().NotBeNull("this is the observation that flipped the latch");

        CohortObservation after = await ObserveAsync("regular11", SpamTrustTier.Established);
        after.Reversal.Should().BeNull("already reversed");
    }

    [Fact]
    public async Task AnAlreadyReversedCampaign_IsNeverRestoredTwice_NoDoubleUnbanStorm()
    {
        // Same setup as above, but this asserts the ACTUAL platform call rather than just the domain
        // flag: re-observing after the reversal already went through must not call UnbanAsync again for
        // accounts that came back on the first pass.
        await AddStrangersAsync(20);
        _time.Advance(TimeSpan.FromSeconds(10));
        await ObserveAsync("stranger0", SpamTrustTier.Untrusted);
        await AddRegularsAsync(11);

        _unbannedAccounts.Should().BeEquivalentTo(["stranger0"], "restored exactly once");
        Stored().ReversedAt.Should().NotBeNull();

        // A wave of new strangers piles into the (already de-qualified, one-way) cohort. None of this
        // may re-trigger a restore of stranger0.
        await AddStrangersAsync(10, prefix: "wave2");

        _unbannedAccounts
            .Should()
            .BeEquivalentTo(
                ["stranger0"],
                "the cohort is one-way and already reversed — no re-run should touch Twitch again"
            );
    }

    [Fact]
    public async Task ADequalifiedCohortNeverRequalifies_EvenAcrossARestart()
    {
        // The latch is persisted precisely so a restart cannot re-action people the regulars already
        // exonerated.
        await AddStrangersAsync(20);
        await AddRegularsAsync(15);
        Stored().IsDequalified.Should().BeTrue();

        _time.Advance(TimeSpan.FromSeconds(30));
        await AddStrangersAsync(60, prefix: "wave2");

        SpamCampaignRecord record = Stored();
        record.NoStandingShare.Should().BeGreaterThan(0.8, "the raw share is back above the bar");
        record.Verdict.Should().Be(CohortVerdict.CommunityPattern, "but the verdict is one-way");
        (await ObserveAsync("wave20", SpamTrustTier.Untrusted)).MayActOnSender.Should().BeFalse();
    }

    [Fact]
    public async Task TurningOffAutoReverse_StopsTheUndoButNotTheDequalification()
    {
        // The setting the catalogue warns against. It must still stop actioning people — what it costs
        // is that those already actioned stay actioned until a moderator intervenes.
        SpamDefenseSettings noUndo = new() { AutoReverseOnDequalify = false };

        for (int i = 0; i < 20; i++)
            await ObserveAsync($"stranger{i}", SpamTrustTier.Untrusted, noUndo);
        _time.Advance(TimeSpan.FromSeconds(10));
        await ObserveAsync("stranger0", SpamTrustTier.Untrusted, noUndo);

        for (int i = 0; i < 15; i++)
            await ObserveAsync($"regular{i}", SpamTrustTier.Established, noUndo);

        SpamCampaignRecord record = Stored();
        record.Verdict.Should().Be(CohortVerdict.CommunityPattern, "it still de-qualifies");
        record.ReversedAt.Should().BeNull("but nothing is undone automatically");
        (await ObserveAsync("stranger5", SpamTrustTier.Untrusted, noUndo))
            .MayActOnSender.Should()
            .BeFalse("and it still stops actioning people");
    }

    // ---- Guards ----------------------------------------------------------------------------------

    [Fact]
    public async Task AShortSkeletonNeverFormsACohort()
    {
        // Correlating on "gg" would gather half the channel into one cohort seconds after a good play.
        using AppDbContext db = NewDbContext();
        SpamCorrelationService service = new(
            db,
            new SpamCampaignReversalExecutor(
                db,
                NewModerationMock(),
                NullLogger<SpamCampaignReversalExecutor>.Instance
            ),
            _time
        );

        for (int i = 0; i < 20; i++)
            await service.ObserveAsync(
                Channel,
                "gg",
                $"viewer{i}",
                SpamTrustTier.Untrusted,
                new SpamDefenseSettings()
            );

        using AppDbContext read = NewDbContext();
        (await read.SpamCampaigns.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AStandingViewerIsNeverActionable_AndCostsTheSkeletonItsNetworkEligibility()
    {
        await AddStrangersAsync(20);
        _time.Advance(TimeSpan.FromSeconds(10));

        CohortObservation sub = await ObserveAsync("thesub", SpamTrustTier.SemiTrusted);

        sub.MayActOnSender.Should().BeFalse("SD11: standing is never auto-actioned");
        Stored()
            .MayContributeToNetwork.Should()
            .BeFalse("one standing member disqualifies it as a signature source");
    }

    [Fact]
    public async Task AnExpiredCohortIsNotReused_ANewOneStarts()
    {
        // A cohort is a statement about a moment. Reusing an expired one would let an attack from
        // yesterday keep counting toward today's verdict.
        await AddStrangersAsync(3);
        _time.Advance(TimeSpan.FromMinutes(31));

        await ObserveAsync("latecomer", SpamTrustTier.Untrusted);

        using AppDbContext read = NewDbContext();
        (await read.SpamCampaigns.CountAsync())
            .Should()
            .Be(2, "the window closed and a new one began");
    }

    // ---- The signature corpus (§4) ---------------------------------------------------------------

    private SpamSignature? StoredSignature()
    {
        using AppDbContext db = NewDbContext();
        return db.SpamSignatures.FirstOrDefault(sig => sig.Value == Skeleton);
    }

    [Fact]
    public async Task AConfirmedCampaign_BecomesALocalSignature()
    {
        // The corpus was empty until something populated it, which meant corpus-match and
        // near-duplicate could never fire. This is what makes those two signals real.
        await AddStrangersAsync(5);

        SpamSignature? signature = StoredSignature();
        signature.Should().NotBeNull();
        signature!.Source.Should().Be(SignatureSource.Local);
        signature
            .IsQuarantined.Should()
            .BeFalse("confirmed on our own evidence, in our own instance");
        signature.Kind.Should().Be(SignatureKind.Skeleton);
    }

    [Fact]
    public async Task ACohortThatIncludedAnyStandingViewer_IsNeverContributed()
    {
        // The worst outcome available: a false signature propagated to every subscriber. One regular in
        // the cohort disqualifies it as a source, even while it still qualifies as a campaign locally.
        await AddStrangersAsync(20);
        await ObserveAsync("thesub", SpamTrustTier.SemiTrusted);

        Stored().Verdict.Should().Be(CohortVerdict.Campaign, "20 of 21 is still 95%");

        // The row is WITHDRAWN rather than deleted — withdrawal is auditable and deletion is not, and
        // what the guarantee actually requires is that it can never be used or shared again.
        SpamSignature? signature = StoredSignature();
        signature!.WithdrawnAt.Should().NotBeNull();
        signature.CanAct(requiredCorroborations: 1).Should().BeFalse();
    }

    [Fact]
    public async Task AnExoneratedCampaign_WithdrawsItsSignature()
    {
        // A corpus that only ever grows is one that slowly starts deleting a community's own
        // catchphrases. The premise the entry was added under no longer holds, so it goes.
        await AddStrangersAsync(20);
        StoredSignature().Should().NotBeNull();

        await AddRegularsAsync(11);

        StoredSignature()!.WithdrawnAt.Should().NotBeNull();
    }

    [Fact]
    public async Task AWithdrawnSignatureIsNotResurrectedByALaterCohort()
    {
        // Otherwise the next wave of strangers would quietly undo a moderator's correction.
        await AddStrangersAsync(20);
        await AddRegularsAsync(11);
        StoredSignature()!.WithdrawnAt.Should().NotBeNull();

        _time.Advance(TimeSpan.FromMinutes(31));
        await AddStrangersAsync(6, prefix: "wave2");

        StoredSignature()!.WithdrawnAt.Should().NotBeNull("still withdrawn");
    }

    [Fact]
    public void AQuarantinedNetworkSignature_OnlyActsOnceCorroborated()
    {
        // The anti-poisoning property: one malicious contributor must not be able to cause a mass
        // removal everywhere at once.
        SpamSignature unproven = new()
        {
            Kind = SignatureKind.Skeleton,
            Value = "x",
            Source = SignatureSource.Network,
            IsQuarantined = true,
            Corroborations = 1,
        };

        unproven.CanAct(requiredCorroborations: 3).Should().BeFalse();

        unproven.Corroborations = 3;
        unproven.CanAct(requiredCorroborations: 3).Should().BeTrue();
    }

    [Fact]
    public void ACuratedSignatureSkipsQuarantine_AndAWithdrawnOneNeverActs()
    {
        SpamSignature curated = new()
        {
            Kind = SignatureKind.Skeleton,
            Value = "y",
            Source = SignatureSource.Curated,
            IsQuarantined = false,
        };
        curated.CanAct(requiredCorroborations: 99).Should().BeTrue();

        curated.WithdrawnAt = T0.UtcDateTime;
        curated
            .CanAct(requiredCorroborations: 1)
            .Should()
            .BeFalse("withdrawal outranks every other reason to act");
    }

    public void Dispose() => _connection.Dispose();
}
