// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Reflection;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Analytics.Entities;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.SpamDefense;

namespace NomNomzBot.Infrastructure.Moderation;

/// <summary>
/// The spam-defence stack, wired to a channel's settings and a sender's real history
/// (spam-defense.md §L0–§L5).
///
/// <para>The Domain layers are pure functions; everything stateful lives here — reading the policy,
/// working out what the sender has actually done in this channel, and writing the record. Keeping the
/// split that way is what let the layers be tested against the cases that matter rather than against a
/// database.</para>
/// </summary>
public sealed class SpamDefenseService : ISpamDefenseService
{
    /// <summary>The dry-run observation window a channel serves before it may act (spam-defense.md §6.2).</summary>
    private const int ObservationDays = 7;

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _time;
    private readonly IModerationService _moderation;
    private readonly ICurrentTenantService _tenant;
    private readonly ITwitchUsersApi _twitchUsers;
    private readonly IFollowStateService _follows;

    public SpamDefenseService(
        IApplicationDbContext db,
        TimeProvider time,
        IModerationService moderation,
        ICurrentTenantService tenant,
        ITwitchUsersApi twitchUsers,
        IFollowStateService follows
    )
    {
        _db = db;
        _time = time;
        _moderation = moderation;
        _tenant = tenant;
        _twitchUsers = twitchUsers;
        _follows = follows;
    }

    /// <summary>
    /// The tenant id the PLATFORM-WIDE defaults are stored under.
    ///
    /// <para>A sentinel rather than a second table: the defaults are the same 22 values with the same
    /// bounds and the same validation, and giving them their own entity would mean two shapes that must
    /// be kept in step forever. <see cref="Guid.Empty"/> is not a real channel, so it cannot collide.</para>
    /// </summary>
    public static Guid PlatformDefaultsScope => Guid.Empty;

    public async Task<SpamDefenseSettings> GetSettingsAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        SpamDefensePolicy? stored = await LoadPolicyAsync(broadcasterId, track: false, ct);
        if (stored is not null)
            return stored.ToSettings();

        // A channel that has never been configured TRACKS the platform defaults, so an admin changing
        // them moves every untouched channel with it (§6). Falling straight through to the shipped
        // constants would make the admin page a lie.
        if (broadcasterId != PlatformDefaultsScope)
        {
            SpamDefensePolicy? defaults = await LoadPolicyAsync(
                PlatformDefaultsScope,
                track: false,
                ct
            );
            if (defaults is not null)
                return defaults.ToSettings();
        }

        return new SpamDefenseSettings();
    }

    public async Task<Result<SpamDefenseSettings>> UpdateSettingsAsync(
        Guid broadcasterId,
        SpamDefenseSettings settings,
        CancellationToken ct = default
    )
    {
        IReadOnlyList<string> violations = ValidateRanges(settings);
        if (violations.Count > 0)
            return Result.Failure<SpamDefenseSettings>(
                string.Join(' ', violations),
                // VALIDATION_FAILED is the established code the API layer already maps to 400. Inventing
                // a new one meant it fell through to 500 — a client error reported as a server fault,
                // which the dashboard would show as "something went wrong" instead of naming the field.
                errorCode: "VALIDATION_FAILED"
            );

        // The hysteresis band is a safety property, not a preference: if de-qualify ever met or
        // exceeded qualify, a cohort on the line would flap between actioning and reversing people.
        if (settings.DequalifyNoStandingShare >= settings.QualifyNoStandingShare)
            return Result.Failure<SpamDefenseSettings>(
                "spam_setting_dequalify_below_qualify",
                errorCode: "VALIDATION_FAILED"
            );

        bool wasEnabled = (await GetSettingsAsync(broadcasterId, ct)).IsEnabled;
        SpamDefensePolicy? policy = await LoadPolicyAsync(broadcasterId, track: true, ct);
        if (policy is null)
        {
            policy = new SpamDefensePolicy { BroadcasterId = broadcasterId };
            _db.SpamDefensePolicies.Add(policy);
        }

        policy.ApplySettings(settings);

        // The seven-day observation clock starts the first time the stack is switched on (§6.2). A channel
        // that tracked the enabled defaults has been observing since it was onboarded, so its first save
        // does not restart the clock; a channel that had the stack off starts it now.
        if (settings.IsEnabled && policy.EnforcementEligibleAt is null)
            policy.EnforcementEligibleAt =
                (wasEnabled ? await ChannelCreatedAtAsync(broadcasterId, ct) : null)?.AddDays(
                    ObservationDays
                ) ?? _time.GetUtcNow().UtcDateTime.AddDays(ObservationDays);

        await SaveOutsideTheTenantWhenPlatformScopedAsync(broadcasterId, ct);
        return Result.Success(policy.ToSettings());
    }

    /// <summary>
    /// The platform-defaults row carries the <see cref="PlatformDefaultsScope"/> sentinel on purpose, but the
    /// tenant-stamp interceptor treats an added tenant-scoped row with an empty broadcaster as "forgot to set"
    /// and rewrites it to the ambient tenant. An admin who owns a channel reaches this from a request whose
    /// ambient tenant IS their own channel, so the first platform save would land as that channel's own policy
    /// (or collide with it). Saving with the ambient tenant cleared keeps the sentinel; it is restored after.
    /// </summary>
    private async Task SaveOutsideTheTenantWhenPlatformScopedAsync(
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        if (broadcasterId != PlatformDefaultsScope || !_tenant.HasTenant)
        {
            await _db.SaveChangesAsync(ct);
            return;
        }

        Guid ambient = _tenant.BroadcasterId!.Value;
        _tenant.Clear();
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        finally
        {
            _tenant.SetTenant(ambient);
        }
    }

    public async Task<SpamDefensePolicyDto> GetPolicyAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        SpamDefensePolicy? stored = await LoadPolicyAsync(broadcasterId, track: false, ct);
        // The page shows what actually runs for this channel — its own pinned row, else the platform
        // defaults it tracks — not the shipped constants a tracking channel may never have seen.
        SpamDefenseSettings effective =
            stored?.ToSettings() ?? await GetSettingsAsync(broadcasterId, ct);

        return new SpamDefensePolicyDto(
            effective,
            SpamSettingCatalogue
                .All.Select(d => new SpamSettingDescriptorDto(
                    d.Key,
                    d.Group,
                    d.LabelKey,
                    d.ExplanationKey,
                    d.CostKey,
                    d.Minimum,
                    d.Maximum,
                    d.IsToggle,
                    d.PendingSlice,
                    d.InactiveReasonKey
                ))
                .ToList(),
            SpamSettingCatalogue
                .Invariants.Select(decision => new SpamInvariantDto(
                    decision,
                    SpamSettingCatalogue.GuaranteeKey(decision)
                ))
                .ToList(),
            await EnforcementEligibleAtAsync(broadcasterId, ct),
            // Whether this channel has chosen its own values or is still tracking the shipped
            // defaults. The dashboard shows the difference so nobody mistakes a default for a decision.
            stored is not null
        );
    }

    public async Task<IReadOnlyList<SpamDetectionDto>> GetDetectionsAsync(
        Guid broadcasterId,
        int page = 1,
        int pageSize = 25,
        CancellationToken ct = default
    )
    {
        int skip = (Math.Max(page, 1) - 1) * Math.Clamp(pageSize, 1, 200);

        return await _db
            .SpamDetections.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(d => d.BroadcasterId == broadcasterId && d.DeletedAt == null)
            .OrderByDescending(d => d.DetectedAt)
            .Skip(skip)
            .Take(Math.Clamp(pageSize, 1, 200))
            .Select(d => new SpamDetectionDto(
                d.Id,
                d.SubjectPlatformUserId,
                d.SubjectDisplayName,
                d.Provider,
                d.MessageId,
                d.MessageText,
                d.Signals,
                d.Confidence,
                d.Tier,
                d.Outcome,
                d.WouldHaveBeen,
                d.WasDryRun,
                d.Reason,
                d.OverturnedAt,
                d.DetectedAt
            ))
            .ToListAsync(ct);
    }

    public async Task<Result> OverturnDetectionAsync(
        Guid broadcasterId,
        Guid detectionId,
        Guid operatorUserId,
        CancellationToken ct = default
    )
    {
        SpamDetection? detection = await _db
            .SpamDetections.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                d => d.Id == detectionId && d.BroadcasterId == broadcasterId && d.DeletedAt == null,
                ct
            );

        if (detection is null)
            return Result.Failure(
                "spam_detection_not_found",
                errorCode: "spam.detection.not_found"
            );

        if (detection.OverturnedAt is not null)
            return Result.Failure("Already overturned.", "VALIDATION_FAILED");

        // The reversal is attempted BEFORE anything on the row is stamped — same ordering as the
        // platform-wide trust & safety desk (TrustSafetyReviewService.OverturnAsync, S-ADMIN-8a): a
        // failed unban must leave the detection exactly as it was, never claiming a reversal that did
        // not actually happen. Signed with the acting moderator's OWN token so Twitch attributes it to
        // them and it works on any channel they moderate.
        Result<ModerationActionResult> reversal = await _moderation.UnbanAsync(
            broadcasterId.ToString(),
            operatorUserId,
            detection.SubjectPlatformUserId,
            operatorUserId.ToString(),
            ct
        );
        if (reversal.IsFailure)
            return Result.Failure(
                $"Could not reverse the automatic action: {reversal.ErrorMessage}",
                "REVERSAL_FAILED"
            );

        detection.OverturnedAt = _time.GetUtcNow().UtcDateTime;
        detection.OverturnedByUserId = operatorUserId;
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<IReadOnlyList<SpamCampaignDto>> GetCampaignsAsync(
        Guid broadcasterId,
        int page = 1,
        int pageSize = 25,
        CancellationToken ct = default
    )
    {
        int size = Math.Clamp(pageSize, 1, 200);

        return await _db
            .SpamCampaigns.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.BroadcasterId == broadcasterId && c.DeletedAt == null)
            .OrderByDescending(c => c.LastSeenAt)
            .Skip((Math.Max(page, 1) - 1) * size)
            .Take(size)
            .Select(c => new SpamCampaignDto(
                c.Id,
                c.Skeleton,
                c.Verdict,
                c.QualificationCount,
                c.ActionableCount,
                c.ActionedCount,
                c.NoStandingShare,
                c.MayContributeToNetwork,
                c.ReversedAt,
                c.ReversalReason,
                c.ReversedByActorId,
                c.RestoredAccountCount,
                c.RestorationFailedAccountIds,
                c.FirstSeenAt,
                c.LastSeenAt
            ))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<FollowBotBlockDto>> GetFollowBotBlocksAsync(
        Guid broadcasterId,
        int page = 1,
        int pageSize = 25,
        CancellationToken ct = default
    )
    {
        int size = Math.Clamp(pageSize, 1, 200);

        return await _db
            .FollowBotBlocks.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(b => b.BroadcasterId == broadcasterId && b.DeletedAt == null)
            .OrderByDescending(b => b.BlockedAt)
            .Skip((Math.Max(page, 1) - 1) * size)
            .Take(size)
            .Select(b => new FollowBotBlockDto(
                b.Id,
                b.BatchId,
                b.SubjectPlatformUserId,
                b.SubjectUsername,
                b.Indicators,
                b.BatchExamined,
                b.RestoredAt,
                b.BlockedAt
            ))
            .ToListAsync(ct);
    }

    public async Task<Result<int>> RestoreFollowBotBatchAsync(
        Guid broadcasterId,
        Guid batchId,
        CancellationToken ct = default
    )
    {
        List<FollowBotBlock> blocks = await _db
            .FollowBotBlocks.IgnoreQueryFilters()
            .Where(b =>
                b.BroadcasterId == broadcasterId
                && b.BatchId == batchId
                && b.DeletedAt == null
                && b.RestoredAt == null
            )
            .ToListAsync(ct);

        if (blocks.Count == 0)
            return Result.Failure<int>("spam_follow_bot_batch_not_found", errorCode: "NOT_FOUND");

        // A row is stamped restored only once the Twitch block is really lifted. A dry-run row never
        // made a block, so there is nothing to lift; a failed unblock stays open so the operator can
        // retry the batch, and the count reports only what was actually undone.
        DateTime now = _time.GetUtcNow().UtcDateTime;
        int restored = 0;
        foreach (FollowBotBlock block in blocks)
        {
            if (!block.WasDryRun)
            {
                Result unblocked = await _twitchUsers.UnblockUserAsync(
                    broadcasterId,
                    block.SubjectPlatformUserId,
                    ct
                );
                if (unblocked.IsFailure)
                    continue;
            }

            block.RestoredAt = now;
            restored++;
        }

        await _db.SaveChangesAsync(ct);
        return restored == 0
            ? Result.Failure<int>("spam_follow_bot_restore_failed", errorCode: "UPSTREAM_ERROR")
            : Result.Success(restored);
    }

    public async Task<SpamEvaluationResult?> EvaluateAsync(
        SpamEvaluationRequest request,
        CancellationToken ct = default
    )
    {
        SpamDefenseSettings settings = await GetSettingsAsync(request.BroadcasterId, ct);
        if (!settings.IsEnabled || string.IsNullOrWhiteSpace(request.Message))
            return null;

        NormalizedMessage normalized = MessageNormalizer.Normalize(request.Message);
        ContentEvaluation content = ContentSignals.Evaluate(
            normalized,
            request.Message,
            await BuildContentPolicyAsync(settings, ct)
        );

        (SpamTrustTier tier, AccountFacts facts) = await ResolveTierAsync(request, settings, ct);
        // Enforcement switched on inside the observation window still only observes: the window is the
        // safety story (§6.2), so turning dry run off early cannot skip it.
        bool observing =
            settings.DryRun || await IsInObservationWindowAsync(request.BroadcasterId, ct);

        // The capability floor (§L4). Established is immune (SD8): the check is not run for them at all,
        // so nothing a floor says can reach them. For everyone else an unearned capability lifts the
        // verdict to Medium and no further.
        IReadOnlyList<SpamCapability> unearned = TrustTierLadder.IsImmune(tier)
            ? []
            : CapabilityGate.Unearned(request.Message, tier, settings.NonLatinScriptGate);
        SpamConfidence confidence = CapabilityGate.RaiseConfidence(content.Confidence, unearned);
        IReadOnlyList<ContentSignal> signals =
            unearned.Count == 0
                ? content.Signals
                : [.. content.Signals, ContentSignal.UnearnedCapability];

        SpamDecision decision = SpamEnforcement.Decide(confidence, tier, observing);
        if (unearned.Count > 0)
            decision = decision with
            {
                Reason =
                    $"{decision.Reason} "
                    + CapabilityGate.Explain(
                        unearned,
                        CapabilityGate.FloorsFor(settings.NonLatinScriptGate)
                    ),
            };

        // The newcomer gate (a channel's own age limit) only ever raises a verdict: a message the content
        // layer already removes keeps its stricter outcome and its own reasons.
        AccountAgeGateVerdict? gate =
            decision.WouldHaveBeen < SpamOutcome.DeleteAndQueue
                ? AccountAgeGate.Evaluate(settings, facts, tier)
                : null;
        if (gate is not null)
        {
            decision = SpamEnforcement.Gate(gate, observing);
            signals = [.. signals, ContentSignal.AccountAgeGate];
        }

        // Nothing fired and nothing to say: do not write a row per ordinary message. The detection log
        // is for verdicts a human might review, not a copy of chat.
        if (
            gate is null
            && confidence == SpamConfidence.Zero
            && decision.WouldHaveBeen == SpamOutcome.None
        )
            return new SpamEvaluationResult(
                decision,
                confidence,
                tier,
                signals,
                null,
                normalized.Skeleton,
                settings
            );

        SpamDetection detection = new()
        {
            BroadcasterId = request.BroadcasterId,
            SubjectPlatformUserId = request.PlatformUserId,
            SubjectDisplayName = request.DisplayName,
            Provider = request.Provider,
            MessageId = request.MessageId,
            MessageText = Truncate(request.Message, 1000),
            Skeleton = Truncate(normalized.Skeleton, 1000),
            Signals = string.Join(',', signals),
            Confidence = confidence,
            Tier = tier,
            Outcome = decision.Outcome,
            WouldHaveBeen = decision.WouldHaveBeen,
            WasDryRun = decision.IsDryRun,
            Reason = Truncate(decision.Reason, 1000),
            DetectedAt = _time.GetUtcNow().UtcDateTime,
        };

        _db.SpamDetections.Add(detection);
        await _db.SaveChangesAsync(ct);

        return new SpamEvaluationResult(
            decision,
            confidence,
            tier,
            signals,
            detection.Id,
            normalized.Skeleton,
            settings,
            gate
        );
    }

    public async Task RecordCampaignEscalationAsync(
        SpamEvaluationRequest request,
        SpamEvaluationResult evaluated,
        SpamDecision escalated,
        CancellationToken ct = default
    )
    {
        SpamDetection? detection = evaluated.DetectionId is { } id
            ? await _db.SpamDetections.FirstOrDefaultAsync(d => d.Id == id, ct)
            : null;

        if (detection is null)
        {
            detection = new SpamDetection
            {
                BroadcasterId = request.BroadcasterId,
                SubjectPlatformUserId = request.PlatformUserId,
                SubjectDisplayName = request.DisplayName,
                Provider = request.Provider,
                MessageId = request.MessageId,
                MessageText = Truncate(request.Message, 1000),
                Skeleton = Truncate(evaluated.Skeleton, 1000),
                Signals = string.Empty,
                Tier = evaluated.Tier,
                DetectedAt = _time.GetUtcNow().UtcDateTime,
            };
            _db.SpamDetections.Add(detection);
        }

        List<string> signals = detection
            .Signals.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        if (!signals.Contains(nameof(ContentSignal.CampaignMember)))
            signals.Add(nameof(ContentSignal.CampaignMember));

        detection.Signals = string.Join(',', signals);
        detection.Confidence = SpamConfidence.High;
        detection.Outcome = escalated.Outcome;
        detection.WouldHaveBeen = escalated.WouldHaveBeen;
        detection.WasDryRun = escalated.IsDryRun;
        detection.Reason = Truncate(escalated.Reason, 1000);

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Load the corpus the content layer matches against.
    ///
    /// <para>Until this existed the corpus was always EMPTY, which meant corpus-match and near-duplicate
    /// — two of the six content signals, and the two that catch a known campaign — could never fire at
    /// all. A signal nothing can populate is a signal that does not exist.</para>
    ///
    /// <para>Only entries that may ACT are loaded. A quarantined signature from an unproven source stays
    /// out until enough independent reporters have corroborated it, which is the property that stops one
    /// bad contributor causing removals everywhere; a withdrawn one never comes back.</para>
    /// </summary>
    private async Task<ContentPolicy> BuildContentPolicyAsync(
        SpamDefenseSettings settings,
        CancellationToken ct
    )
    {
        List<SpamSignature> usable = await _db
            .SpamSignatures.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(sig =>
                sig.DeletedAt == null
                && sig.WithdrawnAt == null
                && (!sig.IsQuarantined || sig.Corroborations >= settings.RequiredCorroborations)
            )
            .ToListAsync(ct);

        return new ContentPolicy
        {
            NearDuplicateSimilarity = settings.NearDuplicateSimilarity,
            MinimumSkeletonLength = settings.MinimumSkeletonLength,
            CorpusSkeletons =
            [
                .. usable.Where(sig => sig.Kind == SignatureKind.Skeleton).Select(sig => sig.Value),
            ],
            DeniedDomains =
            [
                .. usable.Where(sig => sig.Kind == SignatureKind.Domain).Select(sig => sig.Value),
            ],
        };
    }

    /// <summary>
    /// Work out where the sender sits on the ladder, from what they have actually done in THIS channel.
    ///
    /// <para>Badges are read first because they are authoritative and free: a moderator, VIP or
    /// subscriber has standing by §L1.2 without a query. Everything else is earned from real history —
    /// when they first spoke here, how much they have said, and on how many separate days, which is the
    /// part that stops a burst of messages in one night from buying immunity.</para>
    /// </summary>
    private async Task<(SpamTrustTier Tier, AccountFacts Facts)> ResolveTierAsync(
        SpamEvaluationRequest request,
        SpamDefenseSettings settings,
        CancellationToken ct
    )
    {
        if (request.IsBroadcaster || request.IsModerator)
            return (SpamTrustTier.Established, new AccountFacts());

        // Twitch's own flag outranks a VIP badge: a restricted or monitored chatter is not waved through.
        string lowTrustStatus =
            await _db
                .ChannelLowTrustStatuses.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(s =>
                    s.BroadcasterId == request.BroadcasterId
                    && s.TwitchUserId == request.PlatformUserId
                )
                .Select(s => s.Status)
                .FirstOrDefaultAsync(ct)
            ?? LowTrustStatuses.None;

        if (request.IsVip && lowTrustStatus == LowTrustStatuses.None)
            return (SpamTrustTier.Established, new AccountFacts());

        DateTime now = _time.GetUtcNow().UtcDateTime;

        IQueryable<Domain.Chat.Entities.ChatMessage> mine = _db
            .ChatMessages.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(m =>
                m.BroadcasterId == request.BroadcasterId
                && m.UserId == request.PlatformUserId
                && m.DeletedAt == null
            );

        int messagesHere = await mine.CountAsync(ct);
        DateTime? firstHere =
            messagesHere == 0 ? null : await mine.MinAsync(m => (DateTime?)m.CreatedAt, ct);
        int distinctDays =
            messagesHere == 0
                ? 0
                : await mine.Select(m => m.CreatedAt.Date).Distinct().CountAsync(ct);

        ChannelParticipation participation = new()
        {
            DaysSinceFirstMessageHere = firstHere is null ? 0 : (now - firstHere.Value).TotalDays,
            MessagesHere = messagesHere,
            MessageCountHere = messagesHere,
            DistinctActiveDaysHere = distinctDays,
            DaysSinceLastUpheldStrike = double.MaxValue,
        };

        // Watch time is the strongest standing signal we own (§L1.2): real seconds the viewer was
        // present, here and across every channel on this instance, from the watch projection.
        IQueryable<ViewerProfile> profiles = _db
            .ViewerProfiles.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.ViewerTwitchUserId == request.PlatformUserId && p.DeletedAt == null);
        long secondsHere = await profiles
            .Where(p => p.BroadcasterId == request.BroadcasterId)
            .SumAsync(p => p.TotalWatchSeconds, ct);
        long secondsInstance = await profiles.SumAsync(p => p.TotalWatchSeconds, ct);

        DateTime? accountCreatedAt = await _db
            .Users.AsNoTracking()
            .Where(u => u.TwitchUserId == request.PlatformUserId)
            .Select(u => u.AccountCreatedAt)
            .FirstOrDefaultAsync(ct);

        // An unknown account age stays unknown (the default) instead of being guessed, so it can never
        // weigh against the viewer.
        AccountFacts facts = new()
        {
            AccountAgeDays = accountCreatedAt is null
                ? double.MaxValue
                : (now - accountCreatedAt.Value).TotalDays,
            Follow = FollowState.Unknown,
            Username = request.DisplayName,
            IsSubscriberAnywhere = request.IsSubscriber,
            LowTrustStatus = lowTrustStatus,
            WatchTimeHoursThisChannel = TimeSpan.FromSeconds(secondsHere).TotalHours,
            WatchTimeHoursInstanceWide = TimeSpan.FromSeconds(secondsInstance).TotalHours,
        };

        AccountRiskAssessment risk = AccountRisk.Assess(
            facts,
            settings.SemiTrustedWatchHoursHere,
            settings.SemiTrustedWatchHoursInstance
        );

        TrustTierThresholds thresholds = new()
        {
            EstablishedDays = settings.TrustThresholds.EstablishedDays,
            EstablishedMessages = settings.TrustThresholds.EstablishedMessages,
            EstablishedDistinctActiveDays = settings.TrustThresholds.EstablishedDistinctActiveDays,
        };

        // The follow lookup is paid for (a Helix call), so it only runs when it can change the answer:
        // either the tier, or the follow limit of a viewer the newcomer gate would still measure.
        SpamTrustTier tierWithoutFollow = TrustTierLadder.Resolve(
            facts,
            participation,
            risk,
            thresholds
        );
        bool followFeedsGate =
            settings.FollowAgeGateDays > 0 && !AccountAgeGate.IsExempt(tierWithoutFollow);
        if (
            !followFeedsGate
            && !TrustTierLadder.FollowCanChangeTier(facts, participation, risk, thresholds)
        )
            return (tierWithoutFollow, facts);

        FollowLookup follow = await _follows.ResolveAsync(
            request.BroadcasterId,
            request.Provider,
            request.PlatformUserId,
            ct
        );
        if (follow is { State: FollowState.Following, FollowedAt: not null })
            facts = facts with
            {
                Follow = FollowState.Following,
                FollowAgeHours = Math.Max(
                    0,
                    (now - follow.FollowedAt.Value.UtcDateTime).TotalHours
                ),
            };
        else if (follow.State == FollowState.NotFollowing)
            facts = facts with { Follow = FollowState.NotFollowing };

        return (TrustTierLadder.Resolve(facts, participation, risk, thresholds), facts);
    }

    /// <summary>
    /// When this channel may first act: its stamped clock, else seven days after it was onboarded (a channel
    /// that never saved settings tracks the enabled defaults, so it has been observing since then).
    /// </summary>
    private Task<DateTime?> EnforcementEligibleAtAsync(Guid broadcasterId, CancellationToken ct) =>
        SpamObservationWindow.EligibleAtAsync(_db, broadcasterId, ct);

    private Task<bool> IsInObservationWindowAsync(Guid broadcasterId, CancellationToken ct) =>
        SpamObservationWindow.IsActiveAsync(_db, _time, broadcasterId, ct);

    private Task<DateTime?> ChannelCreatedAtAsync(Guid broadcasterId, CancellationToken ct) =>
        _db
            .Channels.IgnoreQueryFilters()
            .Where(c => c.Id == broadcasterId)
            .Select(c => (DateTime?)c.CreatedAt)
            .FirstOrDefaultAsync(ct);

    private async Task<SpamDefensePolicy?> LoadPolicyAsync(
        Guid broadcasterId,
        bool track,
        CancellationToken ct
    )
    {
        // Cross-tenant-safe: evaluation runs from EventSub handlers, outside a resolved-tenant request,
        // so the broadcaster is matched explicitly rather than relying on the ambient query filter.
        IQueryable<SpamDefensePolicy> query = _db.SpamDefensePolicies.IgnoreQueryFilters();
        if (!track)
            query = query.AsNoTracking();

        return await query.FirstOrDefaultAsync(
            p => p.BroadcasterId == broadcasterId && p.DeletedAt == null,
            ct
        );
    }

    /// <summary>
    /// Range-check every numeric setting against the catalogue that documents it, by reflection.
    ///
    /// <para>Driven from <see cref="SpamSettingCatalogue"/> rather than hand-written per field, so a
    /// knob added tomorrow is validated by the same bounds the dashboard shows for it — there is no
    /// second list to keep in step.</para>
    /// </summary>
    private static IReadOnlyList<string> ValidateRanges(SpamDefenseSettings settings)
    {
        List<string> violations = [];

        foreach (
            PropertyInfo property in typeof(SpamDefenseSettings).GetProperties(
                BindingFlags.Public | BindingFlags.Instance
            )
        )
        {
            if (property.PropertyType != typeof(int) && property.PropertyType != typeof(double))
                continue;

            SpamSettingDescriptor? descriptor = SpamSettingCatalogue.For(property.Name);
            if (descriptor?.Minimum is null || descriptor.Maximum is null)
                continue;

            double value = Convert.ToDouble(property.GetValue(settings));
            if (value < descriptor.Minimum.Value || value > descriptor.Maximum.Value)
                // The setting is named by its RESOURCE KEY, not by prose: the dashboard resolves it in
                // the operator's own language. A backend that returned "Fewest accounts must be
                // between 2 and 100" would show English to a Dutch streamer.
                violations.Add($"{descriptor.LabelKey}:{descriptor.Minimum}:{descriptor.Maximum}");
        }

        return violations;
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
