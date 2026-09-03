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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
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
    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _time;

    public SpamDefenseService(IApplicationDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task<SpamDefenseSettings> GetSettingsAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        SpamDefensePolicy? stored = await LoadPolicyAsync(broadcasterId, track: false, ct);
        return stored?.ToSettings() ?? new SpamDefenseSettings();
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

        SpamDefensePolicy? policy = await LoadPolicyAsync(broadcasterId, track: true, ct);
        if (policy is null)
        {
            policy = new SpamDefensePolicy { BroadcasterId = broadcasterId };
            _db.SpamDefensePolicies.Add(policy);
        }

        policy.ApplySettings(settings);

        // The seven-day observation clock starts the first time the stack is switched on, so the
        // dashboard can answer "how long have I been watching?" rather than guessing.
        if (settings.IsEnabled && policy.EnforcementEligibleAt is null)
            policy.EnforcementEligibleAt = _time.GetUtcNow().UtcDateTime.AddDays(7);

        await _db.SaveChangesAsync(ct);
        return Result.Success(policy.ToSettings());
    }

    public async Task<SpamDefensePolicyDto> GetPolicyAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        SpamDefensePolicy? stored = await LoadPolicyAsync(broadcasterId, track: false, ct);

        return new SpamDefensePolicyDto(
            stored?.ToSettings() ?? new SpamDefenseSettings(),
            SpamSettingCatalogue
                .All.Select(d => new SpamSettingDescriptorDto(
                    d.Key,
                    d.Group,
                    d.LabelKey,
                    d.ExplanationKey,
                    d.CostKey,
                    d.Minimum,
                    d.Maximum,
                    d.IsToggle
                ))
                .ToList(),
            SpamSettingCatalogue
                .Invariants.Select(decision => new SpamInvariantDto(
                    decision,
                    SpamSettingCatalogue.GuaranteeKey(decision)
                ))
                .ToList(),
            stored?.EnforcementEligibleAt,
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

        detection.OverturnedAt = _time.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(ct);
        return Result.Success();
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
            new ContentPolicy
            {
                NearDuplicateSimilarity = settings.NearDuplicateSimilarity,
                MinimumSkeletonLength = settings.MinimumSkeletonLength,
            }
        );

        SpamTrustTier tier = await ResolveTierAsync(request, settings, ct);
        SpamDecision decision = SpamEnforcement.Decide(content.Confidence, tier, settings.DryRun);

        // Nothing fired and nothing to say: do not write a row per ordinary message. The detection log
        // is for verdicts a human might review, not a copy of chat.
        if (content.Confidence == SpamConfidence.Zero && decision.WouldHaveBeen == SpamOutcome.None)
            return new SpamEvaluationResult(
                decision,
                content.Confidence,
                tier,
                content.Signals,
                null
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
            Signals = string.Join(',', content.Signals),
            Confidence = content.Confidence,
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
            content.Confidence,
            tier,
            content.Signals,
            detection.Id
        );
    }

    /// <summary>
    /// Work out where the sender sits on the ladder, from what they have actually done in THIS channel.
    ///
    /// <para>Badges are read first because they are authoritative and free: a moderator, VIP or
    /// subscriber has standing by §L1.2 without a query. Everything else is earned from real history —
    /// when they first spoke here, how much they have said, and on how many separate days, which is the
    /// part that stops a burst of messages in one night from buying immunity.</para>
    /// </summary>
    private async Task<SpamTrustTier> ResolveTierAsync(
        SpamEvaluationRequest request,
        SpamDefenseSettings settings,
        CancellationToken ct
    )
    {
        if (request.IsBroadcaster || request.IsModerator || request.IsVip)
            return SpamTrustTier.Established;

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

        AccountFacts facts = new()
        {
            AccountAgeDays = participation.DaysSinceFirstMessageHere,
            IsFollowing = false,
            FollowAgeHours = 0,
            Username = request.DisplayName,
        };

        // A subscriber has standing anywhere on this instance (§L1.2), which is a floor rather than a
        // score — so it is passed as the assessment's standing flag, not folded into the ladder.
        AccountRiskAssessment risk = new(1.0, [], IsSemiTrusted: request.IsSubscriber);

        return TrustTierLadder.Resolve(
            facts,
            participation,
            risk,
            new TrustTierThresholds
            {
                EstablishedDays = settings.TrustThresholds.EstablishedDays,
                EstablishedMessages = settings.TrustThresholds.EstablishedMessages,
                EstablishedDistinctActiveDays = settings
                    .TrustThresholds
                    .EstablishedDistinctActiveDays,
            }
        );
    }

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
