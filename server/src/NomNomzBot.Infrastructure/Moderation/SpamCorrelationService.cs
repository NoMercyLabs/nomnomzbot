// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.SpamDefense;

namespace NomNomzBot.Infrastructure.Moderation;

/// <summary>What observing one message did to its cohort, so the caller can act without re-deriving it.</summary>
/// <param name="Verdict">The cohort's verdict after this observation.</param>
/// <param name="MayActOnSender">
/// Whether THIS sender may be actioned right now — campaign, member, no standing, delay elapsed.
/// </param>
/// <param name="Reversal">Set when this observation de-qualified the cohort and actions must be undone.</param>
public sealed record CohortObservation(
    CohortVerdict Verdict,
    bool MayActOnSender,
    CampaignReversal? Reversal
);

/// <summary>
/// Keeps correlated cohorts across messages and across restarts (spam-defense.md §L3.0, §L3.0.1).
///
/// <para>The cohort lives in the database rather than in process memory, and that is a correctness
/// decision rather than a scaling one. The verdict is reversible for the life of its window: when
/// regulars join a phrase, every account the campaign actioned has to be undone. A cohort that only
/// existed in memory loses that list on restart, the de-qualification latch resets so more strangers
/// could re-action people the regulars already exonerated, and the action-delay clock starts over every
/// time the process bounces.</para>
///
/// <para>All of the judgement lives in <see cref="CampaignCohort"/>. This type only loads, hands over,
/// and stores — so the rules that decide whether somebody is punished are tested as pure functions and
/// exist in exactly one place.</para>
/// </summary>
public sealed class SpamCorrelationService
{
    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _time;

    public SpamCorrelationService(IApplicationDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    /// <summary>
    /// Record one account posting <paramref name="skeleton"/> and re-judge its cohort.
    ///
    /// <para>Short skeletons are ignored outright: correlating on "gg" would gather half the channel
    /// into one cohort within seconds of a good play.</para>
    /// </summary>
    public async Task<CohortObservation> ObserveAsync(
        Guid broadcasterId,
        string skeleton,
        string accountId,
        SpamTrustTier tier,
        SpamDefenseSettings settings,
        CancellationToken ct = default
    )
    {
        if (skeleton.Length < settings.MinimumSkeletonLength)
            return new CohortObservation(CohortVerdict.Watching, false, null);

        DateTimeOffset now = _time.GetUtcNow();
        CohortThresholds thresholds = ThresholdsFrom(settings);

        SpamCampaignRecord? record = await _db
            .SpamCampaigns.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                c =>
                    c.BroadcasterId == broadcasterId
                    && c.Skeleton == skeleton
                    && c.DeletedAt == null
                    && c.ExpiresAt > now.UtcDateTime,
                ct
            );

        CampaignCohort cohort;
        if (record is null)
        {
            cohort = new CampaignCohort(skeleton, now, thresholds);
            record = new SpamCampaignRecord
            {
                BroadcasterId = broadcasterId,
                Skeleton = skeleton,
                FirstSeenAt = now.UtcDateTime,
            };
            _db.SpamCampaigns.Add(record);
        }
        else
        {
            cohort = CampaignCohort.Rehydrate(
                record.Skeleton,
                AsUtc(record.FirstSeenAt),
                AsUtc(record.ExpiresAt),
                Split(record.MemberAccountIds),
                Split(record.StandingAccountIds),
                Split(record.ActionedAccountIds),
                record.Verdict,
                record.IsDequalified,
                record.QualifiedAt is null ? null : AsUtc(record.QualifiedAt.Value),
                record.MayContributeToNetwork,
                thresholds
            );
        }

        bool wasDequalified = cohort.IsDequalified;
        CohortVerdict verdict = cohort.Observe(accountId, tier, now);
        bool mayAct = cohort.MayActOn(accountId, now);
        if (mayAct)
            cohort.RecordAction(accountId);

        // A reversal is produced only on the OBSERVATION that flipped the latch. Emitting it on every
        // later message would re-issue unbans for accounts already restored.
        //
        // AutoReverseOnDequalify gates the UNDO, never the verdict: with it off the cohort still
        // de-qualifies and still stops actioning people, but the accounts it already touched stay
        // touched until a moderator intervenes. That is the setting the catalogue warns against, and
        // this is the line that makes the warning true.
        bool flippedNow = !wasDequalified && cohort.IsDequalified;
        CampaignReversal? reversal =
            flippedNow && settings.AutoReverseOnDequalify ? cohort.BuildReversal() : null;

        Persist(record, cohort, now, reversal);
        await SyncSignatureAsync(cohort, now, reversal is not null, ct);
        await _db.SaveChangesAsync(ct);

        return new CohortObservation(verdict, mayAct, reversal);
    }

    /// <summary>
    /// Promote a confirmed campaign into the corpus, or withdraw it when the cohort is exonerated.
    ///
    /// <para>Two guards, and both matter more than the promotion itself. A cohort that ever included a
    /// viewer with standing is <b>never</b> contributed, because a false signature propagated to every
    /// subscriber is the worst outcome this system can produce. And a de-qualification WITHDRAWS the
    /// entry rather than leaving it: the premise it was added under no longer holds, and a corpus that
    /// only ever grows is one that slowly starts deleting a community's own catchphrases.</para>
    /// </summary>
    private async Task SyncSignatureAsync(
        CampaignCohort cohort,
        DateTimeOffset now,
        bool wasReversed,
        CancellationToken ct
    )
    {
        SpamSignature? existing = await _db.SpamSignatures.FirstOrDefaultAsync(
            sig =>
                sig.Kind == SignatureKind.Skeleton
                && sig.Value == cohort.Skeleton
                && sig.DeletedAt == null,
            ct
        );

        // Withdrawal has TWO triggers, and the second one is easy to miss: the cohort qualified and was
        // contributed BEFORE a standing viewer showed up. "Never contributed" has to mean never, which
        // includes retracting an entry the moment the cohort stops being eligible — otherwise the
        // guarantee holds only for regulars who happened to arrive early.
        bool noLongerEligible = !cohort.MayContributeToNetwork;
        if (wasReversed || noLongerEligible)
        {
            if (existing is not null && existing.Source == SignatureSource.Local)
                existing.WithdrawnAt = now.UtcDateTime;
            return;
        }

        if (cohort.Verdict != CohortVerdict.Campaign)
            return;

        if (existing is null)
        {
            _db.SpamSignatures.Add(
                new SpamSignature
                {
                    Kind = SignatureKind.Skeleton,
                    Value = cohort.Skeleton,
                    Source = SignatureSource.Local,
                    // Locally confirmed on our own evidence, so it is not quarantined HERE. Quarantine
                    // is for entries arriving from somebody else's instance.
                    IsQuarantined = false,
                    Corroborations = 1,
                    FirstSeenAt = now.UtcDateTime,
                    LastConfirmedAt = now.UtcDateTime,
                }
            );
            return;
        }

        existing.LastConfirmedAt = now.UtcDateTime;
        if (existing.WithdrawnAt is not null)
            return; // a withdrawn signature is not silently resurrected by a later cohort
    }

    private void Persist(
        SpamCampaignRecord record,
        CampaignCohort cohort,
        DateTimeOffset now,
        CampaignReversal? reversal
    )
    {
        record.Verdict = cohort.Verdict;
        record.IsDequalified = cohort.IsDequalified;
        record.QualifiedAt = cohort.QualifiedAt?.UtcDateTime;
        record.ExpiresAt = cohort.ExpiresAt.UtcDateTime;
        record.MayContributeToNetwork = cohort.MayContributeToNetwork;
        record.QualificationCount = cohort.QualificationSet.Count;
        record.ActionableCount = cohort.ActionSet.Count;
        record.ActionedCount = cohort.ActionedAccounts.Count;
        record.NoStandingShare = cohort.NoStandingShare;
        record.MemberAccountIds = Join(cohort.QualificationSet);
        record.StandingAccountIds = Join(cohort.StandingMembers);
        record.ActionedAccountIds = Join(cohort.ActionedAccounts);
        record.LastSeenAt = now.UtcDateTime;

        if (reversal is not null)
        {
            record.ReversedAt = now.UtcDateTime;
            record.ReversalReason = reversal.OperatorMessage;
        }
    }

    private static CohortThresholds ThresholdsFrom(SpamDefenseSettings settings) =>
        new()
        {
            QualifyNoStandingShare = settings.QualifyNoStandingShare,
            DequalifyNoStandingShare = settings.DequalifyNoStandingShare,
            MinimumDistinctAccounts = settings.MinimumCohortSize,
            Window = TimeSpan.FromSeconds(settings.WindowSeconds),
            MaximumWindow = TimeSpan.FromSeconds(settings.MaxWindowSeconds),
            ActionDelay = TimeSpan.FromSeconds(settings.ActionDelaySeconds),
        };

    /// <summary>
    /// Read a stored timestamp back as UTC.
    ///
    /// <para>Not decoration: a <see cref="DateTime"/> round-tripped through the provider comes back with
    /// <see cref="DateTimeKind.Unspecified"/>, and the implicit conversion to
    /// <see cref="DateTimeOffset"/> then applies the machine's LOCAL offset. On any host that is not at
    /// UTC that silently shifts every window and every action-delay clock by hours — cohorts stop
    /// matching their own rows, and the head start either never elapses or elapses instantly.</para>
    /// </summary>
    private static DateTimeOffset AsUtc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static IEnumerable<string> Split(string value) =>
        string.IsNullOrEmpty(value)
            ? []
            : value.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            );

    private static string Join(IEnumerable<string> values) => string.Join(',', values);
}
